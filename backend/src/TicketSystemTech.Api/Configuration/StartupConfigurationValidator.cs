using Microsoft.Extensions.Configuration;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// Refuses to start with a configuration that would only fail later and obscurely (an empty JWT secret throws deep
/// inside token validation) or, worse, would silently run insecurely. All problems are reported at once.
/// </summary>
public static class StartupConfigurationValidator
{
    private const int MinSecretLength = 32;

    public static IReadOnlyList<string> Validate(IConfiguration configuration, bool isDevelopment, bool isProduction = false)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            problems.Add("ConnectionStrings:DefaultConnection is not set.");

        var jwtSecret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
            problems.Add("Jwt:Secret is not set (environment variable Jwt__Secret).");
        else if (jwtSecret.Length < MinSecretLength)
            problems.Add($"Jwt:Secret is too short: at least {MinSecretLength} characters are required to sign tokens with HS256.");

        if (!isDevelopment)
        {
            var hashKey = configuration["Pii:HashKey"];
            if (string.IsNullOrWhiteSpace(hashKey))
                problems.Add("Pii:HashKey is not set (environment variable Pii__HashKey). It keys the email lookup hashes; " +
                             "without a private value they could be brute-forced offline.");
            else if (hashKey.Length < MinSecretLength)
                problems.Add($"Pii:HashKey is too short: at least {MinSecretLength} characters are required.");
        }

        // A typo in a proxy address would otherwise surface as a FormatException from deep inside the startup code.
        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
            if (!System.Net.IPAddress.TryParse(proxy, out _))
                problems.Add($"ForwardedHeaders:KnownProxies contains '{proxy}', which is not an IP address.");

        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
            if (!System.Net.IPNetwork.TryParse(network, out _))
                problems.Add($"ForwardedHeaders:KnownNetworks contains '{network}', which is not a network such as 10.0.0.0/8.");

        ValidateEmail(configuration, problems);
        ValidateAi(configuration, problems);
        if (isProduction) ValidateProduction(configuration, problems);

        return problems;
    }

    /// <summary>A half-configured mail server would only show up later as mail that silently never leaves the outbox.</summary>
    private static void ValidateEmail(IConfiguration configuration, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"])) return; // no mail server: mail is queued and skipped, which is allowed

        var port = configuration["Smtp:Port"];
        if (!string.IsNullOrWhiteSpace(port) && (!int.TryParse(port, out var number) || number is < 1 or > 65535))
            problems.Add($"Smtp:Port is '{port}', which is not a port number (1-65535).");

        var security = configuration["Smtp:Security"];
        if (!string.IsNullOrWhiteSpace(security) && SmtpSettings.ParseSecurity(security) is null)
            problems.Add($"Smtp:Security is '{security}'; use None, StartTls or Ssl.");

        if (string.IsNullOrWhiteSpace(configuration["Smtp:FromAddress"]))
            problems.Add("Smtp:Host is set but Smtp:FromAddress is not: a mail server accepts mail only from a sender address, and there is no default.");
    }

    /// <summary>The AI provider is chosen by configuration, so a typo must not turn into "AI quietly does nothing".</summary>
    private static void ValidateAi(IConfiguration configuration, List<string> problems)
    {
        var providerText = configuration["Ai:Provider"];
        var provider = AiSettings.ParseProvider(providerText);
        if (!string.IsNullOrWhiteSpace(providerText) && provider is null)
            problems.Add($"Ai:Provider is '{providerText}'; use Gemini, OpenAiCompatible or None.");

        var endpoint = configuration["Ai:Endpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint) && !(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            problems.Add($"Ai:Endpoint is '{endpoint}', which is not an http(s) address.");

        var dimensions = configuration["Ai:EmbeddingDimensions"];
        if (!string.IsNullOrWhiteSpace(dimensions) && (!int.TryParse(dimensions, out var size) || size <= 0))
            problems.Add($"Ai:EmbeddingDimensions is '{dimensions}', which is not a positive number.");

        // A local model has no built-in defaults: where it is and what it is called must be said.
        if (provider == AiProvider.OpenAiCompatible && configuration.GetValue("Ai:Enabled", true))
        {
            if (string.IsNullOrWhiteSpace(endpoint)) problems.Add("Ai:Provider is OpenAiCompatible but Ai:Endpoint is not set (for example http://localhost:11434/v1).");
            if (string.IsNullOrWhiteSpace(configuration["Ai:ChatModel"])) problems.Add("Ai:Provider is OpenAiCompatible but Ai:ChatModel is not set.");
            if (string.IsNullOrWhiteSpace(configuration["Ai:EmbeddingModel"])) problems.Add("Ai:Provider is OpenAiCompatible but Ai:EmbeddingModel is not set.");
        }
    }

    /// <summary>What is fine on a developer's machine and wrong for a server people actually use.</summary>
    private static void ValidateProduction(IConfiguration configuration, List<string> problems)
    {
        // Every link in an e-mail (invitation, password reset) is built from this; the default points at the developer's own machine.
        var frontend = configuration["Frontend:BaseUrl"];
        if (string.IsNullOrWhiteSpace(frontend) || !Uri.TryCreate(frontend, UriKind.Absolute, out var frontendUri))
            problems.Add("Frontend:BaseUrl is not set to the address people open the application at (environment variable Frontend__BaseUrl); the links in e-mails are built from it.");
        else if (frontendUri.IsLoopback)
            problems.Add($"Frontend:BaseUrl is '{frontend}', which only works on the server itself. Set it to the address people open the application at (environment variable Frontend__BaseUrl).");

        // The key ring sits in the same database as the data it protects. On a server that is not acceptable unless somebody says so.
        if (!KeyRingProtection.IsConfigured(configuration) && !configuration.GetValue("DataProtection:AllowUnprotectedKeys", false))
            problems.Add("The Data Protection key ring would be stored in the database without encryption. Set DataProtection:CertificatePath " +
                         "(+ DataProtection:CertificatePassword) or DataProtection:CertificateThumbprint, or accept the risk explicitly with " +
                         "DataProtection:AllowUnprotectedKeys=true. A certificate is made with: dotnet TicketSystemTech.Api.dll --create-key-certificate <file.pfx> <password>.");

        // The host name of a request ends up in the addresses of files and pictures; "*" accepts any name an attacker sends.
        var hosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains("*"))
            problems.Add("AllowedHosts is '*' (or empty). List the host names the application is reached by, separated by ';' (environment variable AllowedHosts).");
    }

    /// <summary>Throws one exception that lists every problem; call before the host is built.</summary>
    public static void ThrowIfInvalid(IConfiguration configuration, bool isDevelopment, bool isProduction = false)
    {
        var problems = Validate(configuration, isDevelopment, isProduction);
        if (problems.Count == 0) return;

        throw new InvalidOperationException(
            "The application cannot start because its configuration is invalid:" + Environment.NewLine +
            string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
    }
}
