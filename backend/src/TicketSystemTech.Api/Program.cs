using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.KnowledgeBase;
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
using TicketSystemTech.Api.Configuration;
using TicketSystemTech.Api.Realtime;

// Disable appsettings.json hot-reload watching: it uses inotify under the hood, and long-lived containers
// (Docker) can exhaust the host's inotify instance limit, crashing WebApplication.CreateBuilder
// before any of our own startup code even runs. Production config never changes without a redeploy anyway.
Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

// Commands that do their one job and exit, without starting the web host:
//   --healthcheck                                   asks the application in this container if it is alive (Docker HEALTHCHECK)
//   --create-key-certificate <file.pfx> <password>  makes the certificate that protects the key ring (see DataProtection in the README)
if (args.Contains("--healthcheck"))
{
    Environment.ExitCode = await TicketSystemTech.Api.Startup.ContainerHealthProbe.RunAsync();
    return;
}
if (args.Length >= 1 && args[0] == "--create-key-certificate")
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Usage: --create-key-certificate <file.pfx> <password>");
        Environment.ExitCode = 2;
        return;
    }
    TicketSystemTech.Api.Configuration.KeyRingProtection.CreateSelfSigned(args[1], args[2]);
    Console.WriteLine($"Created {args[1]}. Set DataProtection:CertificatePath and DataProtection:CertificatePassword, and keep the file and the password OUT of the database backups.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

Log.Logger = LoggingSetup.Configure(new LoggerConfiguration(), builder.Configuration, builder.Environment.ContentRootPath).CreateLogger();
builder.Host.UseSerilog();

// Fail fast, listing everything that is missing, instead of dying later inside token validation. The reason goes to the log as well
// (and the log is flushed), so that an application that keeps being restarted leaves a record of why in the file and not only on stderr.
try
{
    TicketSystemTech.Api.Configuration.StartupConfigurationValidator.ThrowIfInvalid(
        builder.Configuration, builder.Environment.IsDevelopment(), builder.Environment.IsProduction());
}
catch (Exception ex)
{
    Log.Fatal(ex, "The configuration is not valid; the application will not start");
    await Log.CloseAndFlushAsync();
    Environment.ExitCode = 1;
    return;
}

// ---- Options ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection("Frontend"));
builder.Services.Configure<TemporaryPasswordOptions>(builder.Configuration.GetSection("TemporaryPassword"));
builder.Services.Configure<PiiOptions>(builder.Configuration.GetSection("Pii"));
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        // A dropped connection, a failover or a deadlock victim is retried a few times instead of becoming a 500.
        // (Safe here: the application never opens an explicit transaction of its own.)
        sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);

            // EF Core 10 assumes the newest SQL Server (compatibility level 170) unless told otherwise. The application is
            // meant to run on SQL Server 2019 and later, so the SQL it writes is held to that level.
            sql.UseCompatibilityLevel(150);

            // A list passed to Contains(...) (e.g. "the clients of this branch", hundreds of ids) is sent as ONE parameter, as in
            // EF Core 8 and 9. EF Core 10's default is one parameter per item, which would fail with a list of more than
            // ~2,000 ids (SQL Server accepts at most 2,100 parameters per statement).
            sql.UseParameterizedCollectionMode(Microsoft.EntityFrameworkCore.ParameterTranslationMode.Parameter);
        }));

// Encrypts PII columns (email, phone, address) at rest. Must be registered as a singleton — see
// PiiProtector's doc comment — and the custom ILookupNormalizer must be registered before AddIdentity()
// so it wins over Identity's default TryAddScoped<ILookupNormalizer, UpperInvariantLookupNormalizer>().
builder.Services.AddSingleton<IPiiProtector, PiiProtector>();
builder.Services.AddSingleton<ILookupNormalizer, PiiLookupNormalizer>();

// ---- Identity ----
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false; // Clients can log in with temp password before verifying email.

        // Brute-force protection: lock an account out after repeated failed login attempts.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// ---- JWT Authentication ----
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Allow SignalR clients to pass the JWT via query string (WebSockets can't set headers).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Defense-in-depth against credential stuffing / brute force on auth endpoints (login, forgot-password, etc.).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-in and the other account endpoints, per network address (RateLimits:AuthPerMinute). Sign-in has a second ceiling per
    // account, see LoginAttemptLimiter.
    var authPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 15);
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // The AI endpoints call a metered LLM API, so each person gets a ceiling that keeps one of them from running up the bill.
    // The ceiling is per PERSON (their user id), not per network address: behind a reverse proxy every request may arrive
    // from the proxy's address, and one shared bucket would let one person use up everybody's allowance (see ForwardedHeaders
    // in the README). The limiter runs after authentication for this reason; a request without a user falls back to its address.
    static string PersonOrAddress(HttpContext context) =>
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    // Client-facing AI chat.
    options.AddPolicy("ai", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PersonOrAddress(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    // Staff: AI search, questions, "similar tickets" and the AI summary of a period. Generous for real work (a question a
    // few seconds apart for ten minutes), tight for a script. Configurable: RateLimits:AiStaffPerTenMinutes.
    var aiStaffLimit = builder.Configuration.GetValue("RateLimits:AiStaffPerTenMinutes", 60);
    options.AddPolicy("ai-staff", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PersonOrAddress(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiStaffLimit,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));
});

// ---- Application services ----
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IHtmlSanitizerService, HtmlSanitizerService>();
builder.Services.Configure<SlaOptions>(builder.Configuration.GetSection("Sla"));
builder.Services.AddSingleton<ISlaCalculator>(sp =>
    new TicketSystemTech.Application.Sla.SlaCalculator(sp.GetRequiredService<IOptions<SlaOptions>>().Value));
builder.Services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
// One clock for the whole application, so anything that depends on the time can be tested with a fixed one.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TicketSystemTech.Application.Common.NoticeThrottle>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<TicketSystemTech.Api.Security.LoginAttemptLimiter>();
builder.Services.AddScoped<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IFileLinkService, FileLinkService>();
builder.Services.AddScoped<ITicketAssignmentService, TicketAssignmentService>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketQueries, TicketSystemTech.Api.Tickets.TicketQueries>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketAssigneeService, TicketSystemTech.Api.Tickets.TicketAssigneeService>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketWorkSessionService, TicketSystemTech.Api.Tickets.TicketWorkSessionService>();
builder.Services.AddScoped<TicketSystemTech.Api.Services.IBranchRules, TicketSystemTech.Api.Services.BranchRules>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketRouting, TicketSystemTech.Api.Tickets.TicketRouting>();
builder.Services.AddScoped<TicketSystemTech.Api.Users.IUserQueries, TicketSystemTech.Api.Users.UserQueries>();
builder.Services.AddScoped<TicketSystemTech.Api.Auth.IAuthService, TicketSystemTech.Api.Auth.AuthService>();
builder.Services.AddScoped<TicketSystemTech.Api.Reports.IReportService, TicketSystemTech.Api.Reports.ReportService>();
builder.Services.AddScoped<TicketSystemTech.Api.WorkTasks.IWorkTaskQueries, TicketSystemTech.Api.WorkTasks.WorkTaskQueries>();
builder.Services.AddScoped<TicketSystemTech.Api.WorkTasks.IWorkTaskService, TicketSystemTech.Api.WorkTasks.WorkTaskService>();
builder.Services.AddScoped<TicketSystemTech.Api.Lookups.ICompanyService, TicketSystemTech.Api.Lookups.CompanyService>();
builder.Services.AddScoped<TicketSystemTech.Api.Lookups.IBranchService, TicketSystemTech.Api.Lookups.BranchService>();
builder.Services.AddScoped<TicketSystemTech.Api.Lookups.ILookupTableService, TicketSystemTech.Api.Lookups.LookupTableService>();
builder.Services.AddScoped<TicketSystemTech.Api.KnowledgeBase.IKnowledgeBaseSearch, TicketSystemTech.Api.KnowledgeBase.KnowledgeBaseSearch>();
builder.Services.AddScoped<TicketSystemTech.Api.KnowledgeBase.IKnowledgeBaseDocuments, TicketSystemTech.Api.KnowledgeBase.KnowledgeBaseDocuments>();
builder.Services.AddScoped<TicketSystemTech.Api.Users.IUserAccountService, TicketSystemTech.Api.Users.UserAccountService>();
builder.Services.AddScoped<TicketSystemTech.Api.Users.IUserPrivacyService, TicketSystemTech.Api.Users.UserPrivacyService>();
builder.Services.AddScoped<TicketSystemTech.Api.Users.IUserProfileService, TicketSystemTech.Api.Users.UserProfileService>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketIdempotency, TicketSystemTech.Api.Tickets.TicketIdempotency>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketCreationService, TicketSystemTech.Api.Tickets.TicketCreationService>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketMessageService, TicketSystemTech.Api.Tickets.TicketMessageService>();
builder.Services.AddScoped<TicketSystemTech.Api.Tickets.ITicketLifecycleService, TicketSystemTech.Api.Tickets.TicketLifecycleService>();
builder.Services.AddSingleton<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>();
builder.Services.AddSingleton<IBackgroundEmailQueue>(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>());
// E-mail goes through a durable outbox: callers file a message (IEmailSender), the dispatcher delivers it through SMTP
// (IEmailTransport) and tries again when the mail server is down.
builder.Services.AddSingleton<IEmailSender, OutboxEmailSender>();
builder.Services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
builder.Services.Configure<EmailOutboxOptions>(builder.Configuration.GetSection("Email:Outbox"));
builder.Services.AddSingleton<TicketSystemTech.Api.BackgroundJobs.EmailOutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.EmailOutboxDispatcher>());
builder.Services.AddScoped<IKnowledgeBaseIndexer, KnowledgeBaseIndexer>();
// Closing a ticket or adding a note asks for indexing; a background worker does it, so the answer does not wait for the AI provider.
builder.Services.AddSingleton<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseIndexQueue>();
builder.Services.AddSingleton<IKnowledgeBaseIndexQueue>(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseIndexQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseIndexQueue>());
builder.Services.AddSingleton<IVectorIndex, VectorIndex>();
builder.Services.AddSingleton<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseReindexService>();
builder.Services.AddSingleton<IKnowledgeBaseReindexJob>(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseReindexService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.KnowledgeBaseReindexService>());
builder.Services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddScoped<IKnowledgeBaseDocumentIndexer, KnowledgeBaseDocumentIndexer>();
builder.Services.Configure<MicrosoftGraphOptions>(builder.Configuration.GetSection("MicrosoftGraph"));
builder.Services.AddSingleton<INotificationPublisher, SignalRNotificationPublisher>();
builder.Services.AddHostedService<TicketSystemTech.Api.BackgroundJobs.OverdueTicketNotifier>();
builder.Services.AddHttpContextAccessor();
// Gemini, embeddings and Microsoft Graph: one typed client each, with its own timeouts, retries and circuit breaker.
builder.Services.AddOutboundHttpClients();
builder.Services.AddMemoryCache();
builder.Services.AddHealthChecks().AddCheck<TicketSystemTech.Api.Health.DatabaseHealthCheck>("database", tags: new[] { "ready" });
builder.Services.AddScoped<TicketSystemTech.Api.Middleware.UserStatusValidationMiddleware>();
// Key ring persisted to the database (not local disk) so encrypted OAuth tokens survive redeploys.
var keyRing = builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("TicketSystemTech");

// With a certificate (DataProtection:CertificatePath / CertificateThumbprint) the keys are stored encrypted, and the certificate,
// which is not in the database or its backups, is what opens them. Without one the keys sit in the database in the clear: allowed
// on a developer's machine, refused on a server unless that is accepted explicitly (see StartupConfigurationValidator).
var keyRingCertificate = KeyRingProtection.Load(builder.Configuration, builder.Environment.ContentRootPath);
if (keyRingCertificate is not null)
    keyRing.ProtectKeysWithCertificate(keyRingCertificate).UnprotectKeysWithAnyCertificate(keyRingCertificate);
else
    Log.Warning("The Data Protection key ring is stored in the database WITHOUT encryption (DataProtection:CertificatePath is not set). " +
                "Anyone who gets a copy of the database gets the keys with it.");

// ---- CORS ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              // Without this the browser hides the reference header from the frontend's code.
              .WithExposedHeaders(TicketSystemTech.Api.Middleware.CorrelationIdMiddleware.HeaderName)
              .AllowCredentials();
    });
});

// The documentation upload endpoint accepts up to 200 MB in one request ([RequestSizeLimit]), but multipart forms are
// capped at 128 MB by default, so the larger uploads failed with an unexplained error. The form limit follows the endpoint's.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 210L * 1024 * 1024);

builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        // Model-validation failures used to come back as ProblemDetails (title/errors, no "message"), while every
        // other error in the API is { message }. The frontend reads `message`, so those failures showed a generic text.
        // Same shape everywhere now, with the per-field detail kept under "errors".
        options.InvalidModelStateResponseFactory = actionContext =>
        {
            var errors = actionContext.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) ? "Invalid value." : x.ErrorMessage).ToArray());
            var first = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "The request is not valid.";
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = first, errors });
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo { Title = "Ticket System Tech API", Version = "v1" });
    // `string` and `string?` are different things in the description (the frontend's types are generated from it): without this every text may be null.
    options.SupportNonNullableReferenceTypes();
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.Http
    });
    options.AddSecurityRequirement(document => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

var app = builder.Build();

try
{
    await TicketSystemTech.Api.Startup.StartupTasks.RunAsync(
        app.Services, builder.Configuration, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"));
}
catch (Exception ex)
{
    // Logged through Serilog before the process ends: an exception that simply escapes Main prints to stderr only, which
    // is easy to miss and leaves a crash-looping service with nothing to diagnose.
    Log.Fatal(ex, "Startup tasks failed; the application will not start");
    await Log.CloseAndFlushAsync();
    Environment.ExitCode = 1;
    return;
}

// `dotnet run -- --migrate-only` applies migrations and the other startup tasks and exits, so a deployment can do that as a
// step of its own (see Startup:Migrate).
if (args.Contains("--migrate-only"))
{
    Log.Information("--migrate-only: startup tasks finished, exiting");
    await Log.CloseAndFlushAsync();
    return;
}

// `--reset-admin-password <email>` gives that Admin a new temporary password (printed once, to this terminal only) and exits.
var resetAt = Array.IndexOf(args, "--reset-admin-password");
if (resetAt >= 0)
{
    using var resetScope = app.Services.CreateScope();
    Environment.ExitCode = await TicketSystemTech.Api.Startup.AdminPasswordReset.RunAsync(
        resetScope.ServiceProvider, resetAt + 1 < args.Length ? args[resetAt + 1] : null, Console.Out);
    await Log.CloseAndFlushAsync();
    return;
}

// First, so that everything after it (forwarded headers, CORS, authentication, the endpoints) logs under the request's reference.
app.UseMiddleware<TicketSystemTech.Api.Middleware.CorrelationIdMiddleware>();

// X-Forwarded-For / X-Forwarded-Proto are believed ONLY when they come from a proxy that is listed, and no proxy is listed by
// default (not even this machine): a request cannot lie about its address. Behind a reverse proxy, list it:
// ForwardedHeaders:KnownProxies (addresses) and/or ForwardedHeaders:KnownNetworks (e.g. "10.0.0.0/8"). Without that every request
// appears to come from the proxy. See ForwardedHeadersSetup.
app.UseForwardedHeaders(ForwardedHeadersSetup.Create(builder.Configuration));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection();
}
else
{
    app.UseHsts(); // only emitted on HTTPS requests; browsers ignore it over plain HTTP
}

// Baseline response headers. This API only returns JSON and downloads, so nothing in it ever needs to be framed,
// sniffed or to run script. The CSP is skipped for file downloads (a PDF opened inline needs the browser's own
// viewer) and in Development (Swagger UI relies on inline script).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!app.Environment.IsDevelopment() && !context.Request.Path.StartsWithSegments("/api/files"))
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });
    await next();
});

// Uploads are private and go through /api/files with a signed link. An older installation may still have files in wwwroot/uploads
// (they were public before); the static file middleware would serve those to anybody who knew the name, so that folder is not served.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
app.UseStaticFiles();
app.UseCors("Frontend");
app.UseMiddleware<TicketSystemTech.Api.Middleware.ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TicketSystemTech.Api.Middleware.UserStatusValidationMiddleware>();
app.UseAuthorization();
// After authentication, so that the AI limits can count per person (see the policies above). A request that is refused as
// unauthenticated or forbidden never reaches an endpoint, so there is nothing for the limiter to protect there.
app.UseRateLimiter();

// The service root answers too, for monitors and hosts whose default check hits "/".
app.MapGet("/", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// /health/live: the process is up (no dependencies checked, so a database outage does not get a healthy instance killed
// and restarted pointlessly). /health/ready and /api/health: it can really serve requests, i.e. the database answers and
// its schema is current. A host's health check should point at one of the latter.
var healthOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description })
        });
    }
};
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", healthOptions).AllowAnonymous();
app.MapHealthChecks("/api/health", healthOptions).AllowAnonymous();
app.MapControllers();
// A long-lived WebSocket would otherwise keep receiving per-user and per-role pushes after its JWT has expired.
app.MapHub<NotificationsHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);

try
{
    app.Run();
}
catch (Exception ex) when (ex is not Microsoft.Extensions.Hosting.HostAbortedException)
{
    Log.Fatal(ex, "The application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Lets the integration tests (WebApplicationFactory<Program>) reach the entry point generated for the top-level statements above.
public partial class Program { }
