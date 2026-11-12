using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketSystemTech.Api.Integration;

/// <summary>
/// The keys that let another application read tickets (<c>/api/integration</c>). A key is 256 random bits and is shown once, when it is made;
/// only its SHA-256 goes into the configuration (<c>Integration:Keys</c>), so a copy of the configuration is not a copy of the key. Withdrawing a
/// key is removing its entry (and restarting, like every other setting).
/// </summary>
public static partial class IntegrationKeys
{
    /// <summary>Makes a key easy to recognise (in a password manager, in a log somebody pasted by mistake).</summary>
    public const string Prefix = "tix_";

    /// <summary>The longest key that is even looked at; anything longer is not one of ours, and hashing it would be work for nothing.</summary>
    public const int MaxLength = 200;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,50}$")]
    private static partial Regex NamePattern();

    /// <summary>What a key is called in the configuration and in the log: letters, digits, dot, dash and underscore.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    public static (string Key, string Sha256) Generate()
    {
        var key = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (key, Hash(key));
    }

    /// <summary>The SHA-256 of the key as 64 lower-case hexadecimal characters: what is kept in the configuration.</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>
    /// The command <c>--create-integration-key &lt;name&gt;</c>; returns the exit code (0 made, 2 wrong use). The key is written once, to the
    /// terminal only, together with the line to add to the configuration.
    /// </summary>
    public static int RunCreateCommand(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || !IsValidName(args[1]))
        {
            error.WriteLine("Usage: --create-integration-key <name>   (the name: letters, digits, '.', '-' and '_', at most 50 characters; it is only a label)");
            return 2;
        }

        var (key, hash) = Generate();
        output.WriteLine($"Key for \"{args[1]}\" (shown only now; give it to the application that will read the tickets, it is sent in the header X-Api-Key):");
        output.WriteLine();
        output.WriteLine("  " + key);
        output.WriteLine();
        output.WriteLine("Add this to Integration:Keys in appsettings.Production.json (the key itself is not stored anywhere), then restart the application:");
        output.WriteLine();
        output.WriteLine($"  {{ \"Name\": \"{args[1]}\", \"Sha256\": \"{hash}\" }}");
        return 0;
    }
}
