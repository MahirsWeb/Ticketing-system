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
// (Render, Docker) can exhaust the host's inotify instance limit, crashing WebApplication.CreateBuilder
// before any of our own startup code even runs. Production config never changes without a redeploy anyway.
Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

var builder = WebApplication.CreateBuilder(args);

// Render (and most PaaS hosts) inject the port to bind to via $PORT.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://+:{renderPort}");
}

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    // Added after FromLogContext so a request's own reference wins; lines written outside a request show "-".
    .Enrich.WithProperty(TicketSystemTech.Api.Middleware.CorrelationIdMiddleware.LogProperty, "-")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

// Fail fast, listing everything that is missing, instead of dying later inside token validation.
TicketSystemTech.Api.Configuration.StartupConfigurationValidator.ThrowIfInvalid(
    builder.Configuration, builder.Environment.IsDevelopment());

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
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Client-facing AI chat calls a metered LLM API — cap per-IP usage so it can't be hammered into a
    // large bill. Staff's own /ask isn't limited here; they're a trusted, small, authenticated user base.
    options.AddPolicy("ai", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(5),
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
builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IFileLinkService, FileLinkService>();
builder.Services.AddScoped<ITicketAssignmentService, TicketAssignmentService>();
builder.Services.AddSingleton<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>();
builder.Services.AddSingleton<IBackgroundEmailQueue>(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketSystemTech.Api.BackgroundJobs.BackgroundEmailSender>());
builder.Services.AddScoped<IKnowledgeBaseIndexer, KnowledgeBaseIndexer>();
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
// Brevo, Gemini, embeddings and Microsoft Graph: one typed client each, with its own timeouts, retries and circuit breaker.
builder.Services.AddOutboundHttpClients();
builder.Services.AddMemoryCache();
builder.Services.AddHealthChecks().AddCheck<TicketSystemTech.Api.Health.DatabaseHealthCheck>("database", tags: new[] { "ready" });
builder.Services.AddScoped<TicketSystemTech.Api.Middleware.UserStatusValidationMiddleware>();
// Key ring persisted to the database (not local disk) so encrypted OAuth tokens survive redeploys.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("TicketSystemTech");

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(TicketSystemTech.Application.AssemblyReference).Assembly));

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

// First, so that everything after it (forwarded headers, CORS, authentication, the endpoints) logs under the request's reference.
app.UseMiddleware<TicketSystemTech.Api.Middleware.CorrelationIdMiddleware>();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

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

app.UseStaticFiles();
app.UseCors("Frontend");
app.UseMiddleware<TicketSystemTech.Api.Middleware.ApiExceptionMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TicketSystemTech.Api.Middleware.UserStatusValidationMiddleware>();
app.UseAuthorization();

// Render's health check hits the service root by default — without this it 404s and Render kills the
// instance in an endless restart loop, even though the app itself started up fine.
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
