using Microsoft.Extensions.Configuration;
using TicketSystemTech.Api.Configuration;

namespace TicketSystemTech.Tests;

/// <summary>
/// The application refuses to start with settings that would only misbehave later: a half-configured mail server, a mistyped
/// AI provider, and (on a real server) the developer defaults that make e-mail links point at the server itself.
/// </summary>
public class StartupConfigurationTests
{
    private static readonly (string, string?)[] Valid =
    {
        ("ConnectionStrings:DefaultConnection", "Server=x;Database=y"),
        ("Jwt:Secret", "jwt-secret-jwt-secret-jwt-secret-1234567890"),
        ("Pii:HashKey", "hash-key-hash-key-hash-key-hash-key-12345")
    };

    private static IReadOnlyList<string> Problems(bool production, params (string Key, string? Value)[] extra) =>
        StartupConfigurationValidator.Validate(
            new ConfigurationBuilder().AddInMemoryCollection(Valid.Concat(extra).Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build(),
            isDevelopment: false, isProduction: production);

    private static readonly (string, string?)[] ProductionDefaults =
    {
        ("Frontend:BaseUrl", "https://tickets.example.com"), ("AllowedHosts", "tickets.example.com"),
        ("DataProtection:CertificatePath", "keyring.pfx")
    };

    // ======================= mail server =======================

    [Fact]
    public void No_mail_server_at_all_is_allowed()
    {
        Assert.Empty(Problems(production: false));
    }

    [Fact]
    public void A_complete_mail_server_is_accepted()
    {
        Assert.Empty(Problems(false, ("Smtp:Host", "mail.example.com"), ("Smtp:Port", "465"), ("Smtp:Security", "Ssl"), ("Smtp:FromAddress", "tickets@example.com")));
    }

    [Fact]
    public void A_mail_server_without_a_sender_address_is_refused_because_there_is_no_default_sender()
    {
        var problems = Problems(false, ("Smtp:Host", "mail.example.com"));

        Assert.Contains(problems, p => p.Contains("Smtp:FromAddress"));
    }

    [Theory]
    [InlineData("Smtp:Port", "abc")]
    [InlineData("Smtp:Port", "0")]
    [InlineData("Smtp:Port", "70000")]
    [InlineData("Smtp:Security", "maybe")]
    public void A_mistyped_mail_setting_is_reported_with_its_value(string key, string value)
    {
        var problems = Problems(false, ("Smtp:Host", "mail.example.com"), ("Smtp:FromAddress", "a@b.test"), (key, value));

        Assert.Contains(problems, p => p.Contains(key) && p.Contains(value));
    }

    // ======================= AI =======================

    [Fact]
    public void The_AI_settings_are_not_required()
    {
        Assert.Empty(Problems(false));
        Assert.Empty(Problems(false, ("Ai:Enabled", "false")));
        Assert.Empty(Problems(false, ("GoogleAi:ApiKey", "k")));
    }

    [Fact]
    public void A_local_model_must_say_where_it_is_and_what_it_is_called()
    {
        var problems = Problems(false, ("Ai:Provider", "OpenAiCompatible"));

        Assert.Contains(problems, p => p.Contains("Ai:Endpoint"));
        Assert.Contains(problems, p => p.Contains("Ai:ChatModel"));
        Assert.Contains(problems, p => p.Contains("Ai:EmbeddingModel"));
    }

    [Fact]
    public void A_complete_local_model_is_accepted_and_one_that_is_switched_off_needs_nothing()
    {
        Assert.Empty(Problems(false, ("Ai:Provider", "OpenAiCompatible"), ("Ai:Endpoint", "http://localhost:11434/v1"), ("Ai:ChatModel", "llama3"), ("Ai:EmbeddingModel", "nomic-embed-text")));
        Assert.Empty(Problems(false, ("Ai:Provider", "OpenAiCompatible"), ("Ai:Enabled", "false")));
    }

    [Theory]
    [InlineData("Ai:Provider", "claude")]
    [InlineData("Ai:Endpoint", "not a url")]
    [InlineData("Ai:Endpoint", "ftp://example.com")]
    [InlineData("Ai:EmbeddingDimensions", "0")]
    [InlineData("Ai:EmbeddingDimensions", "many")]
    public void A_mistyped_AI_setting_is_reported_with_its_value(string key, string value)
    {
        Assert.Contains(Problems(false, (key, value)), p => p.Contains(key) && p.Contains(value));
    }

    // ======================= a real server =======================

    [Fact]
    public void On_a_server_the_application_address_and_the_allowed_host_names_must_be_set()
    {
        Assert.Empty(Problems(production: true, ProductionDefaults));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://[::1]:5173")]
    public void E_mail_links_must_not_point_at_the_server_itself(string? frontend)
    {
        var problems = Problems(true, ("Frontend:BaseUrl", frontend), ("AllowedHosts", "tickets.example.com"), ("DataProtection:CertificatePath", "keyring.pfx"));

        Assert.Contains(problems, p => p.Contains("Frontend:BaseUrl"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("tickets.example.com;*")]
    public void Any_host_name_is_not_acceptable_on_a_server(string? hosts)
    {
        var problems = Problems(true, ("Frontend:BaseUrl", "https://tickets.example.com"), ("AllowedHosts", hosts), ("DataProtection:CertificatePath", "keyring.pfx"));

        Assert.Contains(problems, p => p.Contains("AllowedHosts"));
    }

    [Fact]
    public void On_a_server_the_key_ring_needs_a_certificate_unless_leaving_it_open_is_said_out_loud()
    {
        var withoutCertificate = Problems(true, ("Frontend:BaseUrl", "https://tickets.example.com"), ("AllowedHosts", "tickets.example.com"));
        Assert.Contains(withoutCertificate, p => p.Contains("DataProtection:CertificatePath") && p.Contains("--create-key-certificate"));

        Assert.Empty(Problems(true, ("Frontend:BaseUrl", "https://tickets.example.com"), ("AllowedHosts", "tickets.example.com"), ("DataProtection:CertificateThumbprint", "ABCDEF")));
        Assert.Empty(Problems(true, ("Frontend:BaseUrl", "https://tickets.example.com"), ("AllowedHosts", "tickets.example.com"), ("DataProtection:AllowUnprotectedKeys", "true")));
    }

    [Fact]
    public void The_same_defaults_are_fine_in_development_and_testing()
    {
        Assert.Empty(Problems(production: false, ("Frontend:BaseUrl", "http://localhost:5173"), ("AllowedHosts", "*")));
    }

    // ======================= the file that ships =======================

    /// <summary>
    /// The values a fresh installation starts with are the ones in <c>appsettings.json</c>. The tests above give their own settings, so
    /// only reading the shipped file shows that its safe defaults are still the safe ones.
    /// </summary>
    private static IConfiguration Shipped() =>
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false).Build();

    [Fact]
    public void The_shipped_settings_do_not_show_a_generated_password_and_do_not_accept_any_host_name()
    {
        var shipped = Shipped();

        Assert.False(shipped.GetValue("SeedAdmin:PrintGeneratedPassword", true));   // a password shown by default would reach every log that keeps the console
        Assert.NotEqual("*", shipped["AllowedHosts"]?.Trim());                        // any Host name would end up in addresses stored in the database
        Assert.Contains("localhost", shipped["AllowedHosts"]!.Split(';'));            // but the developer's own machine works as it is
    }

    [Fact]
    public void The_shipped_settings_name_no_sender_and_do_not_waive_the_encryption_of_the_key_ring()
    {
        var shipped = Shipped();

        Assert.True(string.IsNullOrWhiteSpace(shipped["Smtp:FromAddress"]));          // nobody's private address in the source code
        Assert.True(string.IsNullOrWhiteSpace(shipped["Smtp:Host"]));
        Assert.False(shipped.GetValue("DataProtection:AllowUnprotectedKeys", true));  // the exception has to be asked for, not shipped
    }
}
