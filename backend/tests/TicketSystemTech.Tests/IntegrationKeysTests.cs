using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TicketSystemTech.Api.Configuration;
using TicketSystemTech.Api.Integration;

namespace TicketSystemTech.Tests;

/// <summary>
/// The keys of the integration API: how one is made, how the application recognises it, and the refusal to start with a configuration whose
/// keys cannot be recognised. Whether the endpoints really turn away a wrong key is in <c>IntegrationApiTests</c>.
/// </summary>
public class IntegrationKeysTests
{
    private static IntegrationKeyStore Store(params (string Name, string Sha256)[] keys)
    {
        var values = new List<KeyValuePair<string, string?>>();
        for (var i = 0; i < keys.Length; i++)
        {
            values.Add(new($"Integration:Keys:{i}:Name", keys[i].Name));
            values.Add(new($"Integration:Keys:{i}:Sha256", keys[i].Sha256));
        }
        return new IntegrationKeyStore(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    // ======================= making a key =======================

    [Fact]
    public void A_key_is_random_recognisable_and_its_hash_is_the_sha256_of_it()
    {
        var (key, hash) = IntegrationKeys.Generate();
        var (other, _) = IntegrationKeys.Generate();

        Assert.StartsWith("tix_", key);
        Assert.NotEqual(key, other);
        Assert.True(key.Length >= 40, "256 random bits written as text are 43 characters");
        Assert.Matches("^[A-Za-z0-9_-]+$", key[4..]);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant(), hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void The_hash_is_the_well_known_sha256()
    {
        // SHA-256("abc"), from the standard: the application's hash must be this one, whatever else changes.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", IntegrationKeys.Hash("abc"));
    }

    [Theory]
    [InlineData("vrijeme")]
    [InlineData("time-tracking.v2")]
    [InlineData("A_b-c.9")]
    public void A_name_is_a_label_of_letters_digits_dot_dash_and_underscore(string name)
    {
        Assert.True(IntegrationKeys.IsValidName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("quote\"s")]
    [InlineData("new\nline")]
    [InlineData("čćž")]
    public void Anything_else_is_not_a_name(string? name)
    {
        Assert.False(IntegrationKeys.IsValidName(name));
    }

    [Fact]
    public void A_name_is_at_most_fifty_characters()
    {
        Assert.True(IntegrationKeys.IsValidName(new string('a', 50)));
        Assert.False(IntegrationKeys.IsValidName(new string('a', 51)));
    }

    // ======================= the command =======================

    [Fact]
    public void The_command_shows_the_key_once_and_the_line_for_the_configuration_with_its_hash()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = IntegrationKeys.RunCreateCommand(["--create-integration-key", "vrijeme"], output, error);

        Assert.Equal(0, exit);
        Assert.Equal("", error.ToString());
        var text = output.ToString();
        var key = text.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("tix_", StringComparison.Ordinal));
        Assert.Contains($"\"Name\": \"vrijeme\"", text);
        Assert.Contains($"\"Sha256\": \"{IntegrationKeys.Hash(key)}\"", text);
        Assert.Equal(1, text.Split(key).Length - 1); // the key is written once; its hash is not the key
    }

    [Fact]
    public void The_key_the_command_shows_is_the_one_the_hash_in_the_line_recognises()
    {
        using var output = new StringWriter();
        IntegrationKeys.RunCreateCommand(["--create-integration-key", "vrijeme"], output, new StringWriter());
        var lines = output.ToString().Split('\n').Select(l => l.Trim()).ToList();
        var key = lines.Single(l => l.StartsWith("tix_", StringComparison.Ordinal));
        var hash = lines.Single(l => l.StartsWith("{ \"Name\"", StringComparison.Ordinal)).Split('"')[7];

        Assert.Equal("vrijeme", Store(("vrijeme", hash)).Authenticate(key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void The_command_with_the_wrong_number_of_arguments_says_how_it_is_used(int count)
    {
        var args = new[] { "--create-integration-key", "vrijeme", "extra" }.Take(count).ToArray();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = IntegrationKeys.RunCreateCommand(args, output, error);

        Assert.Equal(2, exit);
        Assert.Contains("Usage: --create-integration-key <name>", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void The_command_refuses_a_name_that_is_not_a_label()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = IntegrationKeys.RunCreateCommand(["--create-integration-key", "bad name"], output, error);

        Assert.Equal(2, exit);
        Assert.Equal("", output.ToString());
    }

    // ======================= recognising a key =======================

    [Fact]
    public void A_key_is_recognised_by_its_hash_and_gives_the_name_it_was_made_for()
    {
        var (key, hash) = IntegrationKeys.Generate();

        Assert.Equal("vrijeme", Store(("vrijeme", hash)).Authenticate(key));
    }

    [Fact]
    public void Any_of_several_keys_is_recognised_and_each_gives_its_own_name()
    {
        var (first, firstHash) = IntegrationKeys.Generate();
        var (second, secondHash) = IntegrationKeys.Generate();
        var store = Store(("vrijeme", firstHash), ("izvjestaji", secondHash));

        Assert.Equal("vrijeme", store.Authenticate(first));
        Assert.Equal("izvjestaji", store.Authenticate(second));
    }

    [Fact]
    public void The_hash_may_be_written_in_capitals()
    {
        var (key, hash) = IntegrationKeys.Generate();

        Assert.Equal("vrijeme", Store(("vrijeme", hash.ToUpperInvariant())).Authenticate(key));
    }

    [Fact]
    public void A_key_that_is_not_one_of_ours_is_not_recognised()
    {
        var (_, hash) = IntegrationKeys.Generate();
        var (stranger, _) = IntegrationKeys.Generate();
        var store = Store(("vrijeme", hash));

        Assert.Null(store.Authenticate(stranger));
        Assert.Null(store.Authenticate("tix_"));
        Assert.Null(store.Authenticate("x"));
    }

    [Fact]
    public void The_hash_is_not_a_key()
    {
        // Somebody who has read the configuration has the hashes. They must not be able to use one as the key.
        var (_, hash) = IntegrationKeys.Generate();

        Assert.Null(Store(("vrijeme", hash)).Authenticate(hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_presented_is_not_recognised(string? presented)
    {
        var (_, hash) = IntegrationKeys.Generate();

        Assert.Null(Store(("vrijeme", hash)).Authenticate(presented));
    }

    [Fact]
    public void A_key_longer_than_any_we_make_is_turned_away_without_being_hashed()
    {
        var (_, hash) = IntegrationKeys.Generate();

        Assert.Null(Store(("vrijeme", hash)).Authenticate(new string('a', IntegrationKeys.MaxLength + 1)));
    }

    [Fact]
    public void With_no_keys_configured_nothing_is_recognised()
    {
        var (key, _) = IntegrationKeys.Generate();

        Assert.Null(Store().Authenticate(key));
    }

    [Fact]
    public void A_malformed_entry_is_skipped_and_does_not_stop_the_good_ones()
    {
        var (key, hash) = IntegrationKeys.Generate();
        var store = Store(("bad", "not-a-hash"), ("short", "abcd"), ("not hex", new string('z', 64)), ("vrijeme", hash));

        Assert.Equal("vrijeme", store.Authenticate(key));
    }

    // ======================= the configuration is checked when the application starts =======================

    private static IReadOnlyList<string> Problems(params (string Key, string? Value)[] extra) =>
        StartupConfigurationValidator.Validate(
            new ConfigurationBuilder().AddInMemoryCollection(new (string, string?)[]
                {
                    ("ConnectionStrings:DefaultConnection", "Server=x;Database=y"),
                    ("Jwt:Secret", "jwt-secret-jwt-secret-jwt-secret-1234567890"),
                    ("Pii:HashKey", "hash-key-hash-key-hash-key-hash-key-12345")
                }.Concat(extra).Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build(),
            isDevelopment: false, isProduction: false);

    private static readonly string GoodHash = IntegrationKeys.Hash("whatever");

    [Fact]
    public void No_keys_at_all_is_the_normal_case()
    {
        Assert.Empty(Problems());
    }

    [Fact]
    public void Good_keys_are_accepted()
    {
        Assert.Empty(Problems(("Integration:Keys:0:Name", "vrijeme"), ("Integration:Keys:0:Sha256", GoodHash),
            ("Integration:Keys:1:Name", "izvjestaji"), ("Integration:Keys:1:Sha256", IntegrationKeys.Hash("other"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void A_hash_that_is_not_sixty_four_hexadecimal_characters_stops_the_start_and_says_how_to_make_a_key(string hash)
    {
        var problems = Problems(("Integration:Keys:0:Name", "vrijeme"), ("Integration:Keys:0:Sha256", hash));

        var problem = Assert.Single(problems);
        Assert.Contains("Integration:Keys entry 1 ('vrijeme')", problem);
        Assert.Contains("--create-integration-key", problem);
    }

    [Fact]
    public void A_key_without_a_name_or_with_a_bad_one_stops_the_start()
    {
        Assert.Contains(Problems(("Integration:Keys:0:Sha256", GoodHash)), p => p.Contains("no valid Name"));
        Assert.Contains(Problems(("Integration:Keys:0:Name", "bad name"), ("Integration:Keys:0:Sha256", GoodHash)), p => p.Contains("no valid Name"));
    }

    [Fact]
    public void The_same_name_twice_stops_the_start()
    {
        var problems = Problems(("Integration:Keys:0:Name", "vrijeme"), ("Integration:Keys:0:Sha256", GoodHash),
            ("Integration:Keys:1:Name", "vrijeme"), ("Integration:Keys:1:Sha256", IntegrationKeys.Hash("other")));

        Assert.Contains(problems, p => p.Contains("'vrijeme' twice"));
    }

    [Fact]
    public void Every_bad_entry_is_reported_not_only_the_first()
    {
        var problems = Problems(("Integration:Keys:0:Name", "a"), ("Integration:Keys:0:Sha256", "bad"), ("Integration:Keys:1:Name", "b"), ("Integration:Keys:1:Sha256", "worse"));

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("entry 1"));
        Assert.Contains(problems, p => p.Contains("entry 2"));
    }
}
