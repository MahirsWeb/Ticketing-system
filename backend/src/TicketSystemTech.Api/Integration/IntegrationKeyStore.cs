using System.Security.Cryptography;
using System.Text;

namespace TicketSystemTech.Api.Integration;

/// <summary>
/// The keys of <c>Integration:Keys</c>, and the question "does this key belong to somebody?". The key that is presented is hashed and the hash is
/// compared with every stored hash in fixed time, without stopping at the first match, so how long the answer takes says nothing about which
/// entry is close. A malformed entry is skipped here (the start-up check of the configuration refuses to start with one).
/// </summary>
public sealed class IntegrationKeyStore
{
    private readonly List<(string Name, byte[] Sha256)> _keys = [];

    public IntegrationKeyStore(IConfiguration configuration)
    {
        foreach (var entry in configuration.GetSection("Integration:Keys").GetChildren())
        {
            var name = entry["Name"];
            var hash = entry["Sha256"];
            if (!IntegrationKeys.IsValidName(name) || hash is not { Length: 64 }) continue;
            try
            {
                _keys.Add((name!, Convert.FromHexString(hash)));
            }
            catch (FormatException)
            {
                // not hexadecimal: skipped (and reported when the application starts)
            }
        }
    }

    /// <summary>The name of the key that was presented, or null when it is none of ours.</summary>
    public string? Authenticate(string? presented)
    {
        if (string.IsNullOrEmpty(presented) || presented.Length > IntegrationKeys.MaxLength || _keys.Count == 0) return null;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        string? found = null;
        foreach (var (name, stored) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, stored)) found ??= name;
        }
        return found;
    }
}
