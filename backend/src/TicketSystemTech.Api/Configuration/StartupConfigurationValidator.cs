using Microsoft.Extensions.Configuration;

namespace TicketSystemTech.Api.Configuration;

/// <summary>
/// Refuses to start with a configuration that would only fail later and obscurely (an empty JWT secret throws deep
/// inside token validation) or, worse, would silently run insecurely. All problems are reported at once.
/// </summary>
public static class StartupConfigurationValidator
{
    private const int MinSecretLength = 32;

    public static IReadOnlyList<string> Validate(IConfiguration configuration, bool isDevelopment)
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

        return problems;
    }

    /// <summary>Throws one exception that lists every problem; call before the host is built.</summary>
    public static void ThrowIfInvalid(IConfiguration configuration, bool isDevelopment)
    {
        var problems = Validate(configuration, isDevelopment);
        if (problems.Count == 0) return;

        throw new InvalidOperationException(
            "The application cannot start because its configuration is invalid:" + Environment.NewLine +
            string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
    }
}
