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
using TicketSystemTech.Infrastructure.Identity;
using TicketSystemTech.Infrastructure.Persistence;
using TicketSystemTech.Infrastructure.Services;
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
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// ---- Options ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection("Frontend"));
builder.Services.Configure<TemporaryPasswordOptions>(builder.Configuration.GetSection("TemporaryPassword"));
builder.Services.Configure<PiiOptions>(builder.Configuration.GetSection("Pii"));
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddScoped<IEmailSender, BrevoEmailSender>();
builder.Services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();
builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IEmbeddingService, GoogleAiEmbeddingService>();
builder.Services.AddScoped<IKnowledgeBaseIndexer, KnowledgeBaseIndexer>();
builder.Services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddScoped<IKnowledgeBaseDocumentIndexer, KnowledgeBaseDocumentIndexer>();
builder.Services.AddScoped<IChatCompletionService, GeminiChatCompletionService>();
builder.Services.AddScoped<IEmailIntegrationService, GraphEmailIntegrationService>();
builder.Services.Configure<MicrosoftGraphOptions>(builder.Configuration.GetSection("MicrosoftGraph"));
builder.Services.AddSingleton<INotificationPublisher, SignalRNotificationPublisher>();
builder.Services.AddHostedService<TicketSystemTech.Api.BackgroundJobs.OverdueTicketNotifier>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
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
              .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Ticket System Tech API", Version = "v1" });
    var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var seedPasswordGenerator = scope.ServiceProvider.GetRequiredService<ITemporaryPasswordGenerator>();
    var tempPasswordOptions = scope.ServiceProvider.GetRequiredService<IOptions<TemporaryPasswordOptions>>();
    var piiProtector = scope.ServiceProvider.GetRequiredService<IPiiProtector>();
    await db.Database.MigrateAsync();
    await TicketSystemTech.Infrastructure.Persistence.PiiBackfillMigration.RunAsync(db, piiProtector, startupLogger);
    await TicketSystemTech.Infrastructure.Persistence.DbSeeder.SeedAsync(
        db, userManager, startupLogger, builder.Configuration, seedPasswordGenerator, tempPasswordOptions);
}

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

app.UseStaticFiles();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TicketSystemTech.Api.Middleware.UserStatusValidationMiddleware>();
app.UseAuthorization();

// Render's health check hits the service root by default — without this it 404s and Render kills the
// instance in an endless restart loop, even though the app itself started up fine.
app.MapGet("/", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

app.Run();
