using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TicketSystemTech.Api.Configuration;
using TicketSystemTech.Api.Security;
using TicketSystemTech.Api.Startup;

namespace TicketSystemTech.Tests;

/// <summary>
/// The operational findings of the second round of testing, each checked the way it would go wrong: who a proxy may speak for
/// (TS-03.1, TS-ARC-01.1), the certificate that protects the key ring (TS-04.2, TS-HOST-02.1), the log file (TS-ONP-05.1), the
/// probe Docker asks (TS-50.1) and the ceiling per account (TS-03.1).
/// </summary>
public class OperationsHardeningTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tst-ops-" + Guid.NewGuid().ToString("N")[..8]);

    public OperationsHardeningTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    // ======================= who a proxy may speak for =======================

    /// <summary>A request from <paramref name="remote"/> that claims, in X-Forwarded-For, to come from <paramref name="claimed"/>:
    /// what address does the application end up believing?</summary>
    private static async Task<string> BelievedAddressAsync(IConfiguration configuration, string remote, string claimed)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(remote); return next(context); }); // the socket's address
                    app.UseForwardedHeaders(ForwardedHeadersSetup.Create(configuration));
                    app.Run(context => context.Response.WriteAsync(context.Connection.RemoteIpAddress?.ToString() ?? "none"));
                }))
            .StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", claimed);
        return await (await host.GetTestClient().SendAsync(request)).Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Without_any_proxy_listed_nobody_can_choose_their_address_not_even_a_request_from_this_machine()
    {
        Assert.Equal("127.0.0.1", await BelievedAddressAsync(Config(), remote: "127.0.0.1", claimed: "203.0.113.50"));
        Assert.Equal("::1", await BelievedAddressAsync(Config(), remote: "::1", claimed: "203.0.113.50"));
        Assert.Equal("198.51.100.4", await BelievedAddressAsync(Config(), remote: "198.51.100.4", claimed: "203.0.113.50"));
    }

    [Fact]
    public async Task A_listed_proxy_is_believed_and_one_that_is_not_listed_is_not()
    {
        var config = Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"));

        Assert.Equal("203.0.113.50", await BelievedAddressAsync(config, remote: "10.0.0.5", claimed: "203.0.113.50"));
        Assert.Equal("10.0.0.6", await BelievedAddressAsync(config, remote: "10.0.0.6", claimed: "203.0.113.50"));
        Assert.Equal("127.0.0.1", await BelievedAddressAsync(config, remote: "127.0.0.1", claimed: "203.0.113.50"));
    }

    [Fact]
    public async Task A_proxy_on_this_machine_is_believed_only_when_it_is_listed()
    {
        var config = Config(("ForwardedHeaders:KnownProxies:0", "127.0.0.1"));

        Assert.Equal("203.0.113.50", await BelievedAddressAsync(config, remote: "127.0.0.1", claimed: "203.0.113.50"));
    }

    [Fact]
    public async Task A_listed_network_is_believed_as_a_whole()
    {
        var config = Config(("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        Assert.Equal("203.0.113.50", await BelievedAddressAsync(config, remote: "10.1.2.3", claimed: "203.0.113.50"));
        Assert.Equal("11.0.0.1", await BelievedAddressAsync(config, remote: "11.0.0.1", claimed: "203.0.113.50"));
    }

    [Fact]
    public async Task Only_one_hop_is_read_so_a_visitor_cannot_stack_addresses_in_front_of_the_real_one()
    {
        var config = Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"));

        // "1.1.1.1" was written by the visitor; "203.0.113.50" is what the proxy saw. The proxy's own entry is the last one.
        Assert.Equal("203.0.113.50", await BelievedAddressAsync(config, remote: "10.0.0.5", claimed: "1.1.1.1, 203.0.113.50"));
    }

    // ======================= the ceiling per account =======================

    [Fact]
    public void An_account_gets_a_fixed_number_of_attempts_a_minute_and_the_next_one_is_refused()
    {
        using var limiter = new LoginAttemptLimiter(Config(("RateLimits:LoginPerAccountPerMinute", "3")));

        Assert.True(limiter.TryAcquire("ana@firma.test"));
        Assert.True(limiter.TryAcquire("ana@firma.test"));
        Assert.True(limiter.TryAcquire("ana@firma.test"));
        Assert.False(limiter.TryAcquire("ana@firma.test"));
    }

    [Fact]
    public void The_count_is_per_account_and_does_not_care_about_capitals_or_spaces()
    {
        using var limiter = new LoginAttemptLimiter(Config(("RateLimits:LoginPerAccountPerMinute", "2")));

        Assert.True(limiter.TryAcquire("Ana@Firma.test"));
        Assert.True(limiter.TryAcquire("  ana@firma.test "));
        Assert.False(limiter.TryAcquire("ANA@FIRMA.TEST"));
        Assert.True(limiter.TryAcquire("someone.else@firma.test")); // another account is not affected
    }

    [Fact]
    public void An_account_that_does_not_exist_is_counted_like_one_that_does_and_an_empty_field_is_counted_too()
    {
        using var limiter = new LoginAttemptLimiter(Config(("RateLimits:LoginPerAccountPerMinute", "1")));

        Assert.True(limiter.TryAcquire("no-such-account@nowhere.test"));
        Assert.False(limiter.TryAcquire("no-such-account@nowhere.test"));
        Assert.True(limiter.TryAcquire(null));
        Assert.False(limiter.TryAcquire(""));
    }

    [Fact]
    public void The_default_is_generous_for_a_person_and_a_ceiling_for_a_script()
    {
        using var limiter = new LoginAttemptLimiter(Config());

        var allowed = Enumerable.Range(0, 100).Count(_ => limiter.TryAcquire("ana@firma.test"));

        Assert.Equal(30, allowed);
    }

    // ======================= the key ring certificate =======================

    private string NewCertificate(string password = "pw-123456")
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N")[..6] + ".pfx");
        KeyRingProtection.CreateSelfSigned(path, password);
        return path;
    }

    [Fact]
    public void A_key_ring_certificate_can_be_made_and_loaded_with_its_private_key()
    {
        var path = NewCertificate();

        var certificate = KeyRingProtection.Load(Config(("DataProtection:CertificatePath", path), ("DataProtection:CertificatePassword", "pw-123456")), _folder);

        Assert.NotNull(certificate);
        Assert.True(certificate!.HasPrivateKey);
        Assert.True(certificate.NotAfter > DateTime.UtcNow.AddYears(9));
    }

    [Fact]
    public void An_existing_certificate_file_is_never_overwritten_and_a_password_is_required()
    {
        var path = NewCertificate();

        Assert.Throws<InvalidOperationException>(() => KeyRingProtection.CreateSelfSigned(path, "other"));
        Assert.Throws<ArgumentException>(() => KeyRingProtection.CreateSelfSigned(Path.Combine(_folder, "x.pfx"), ""));
    }

    [Fact]
    public void Nothing_configured_means_no_certificate_and_a_relative_path_is_taken_from_the_application_folder()
    {
        Assert.Null(KeyRingProtection.Load(Config(), _folder));
        Assert.False(KeyRingProtection.IsConfigured(Config()));

        var path = NewCertificate();
        var relative = Path.GetFileName(path);
        Assert.NotNull(KeyRingProtection.Load(Config(("DataProtection:CertificatePath", relative), ("DataProtection:CertificatePassword", "pw-123456")), _folder));
    }

    [Fact]
    public void A_certificate_that_cannot_be_used_stops_the_start_with_a_plain_message_that_does_not_repeat_the_password()
    {
        var path = NewCertificate();

        var wrongPassword = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtection.Load(Config(("DataProtection:CertificatePath", path), ("DataProtection:CertificatePassword", "guess-789")), _folder));
        var missing = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtection.Load(Config(("DataProtection:CertificatePath", Path.Combine(_folder, "gone.pfx"))), _folder));
        var unknownThumbprint = Assert.Throws<InvalidOperationException>(() =>
            KeyRingProtection.Load(Config(("DataProtection:CertificateThumbprint", "0123456789ABCDEF0123456789ABCDEF01234567")), _folder));

        Assert.Contains("password", wrongPassword.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guess-789", wrongPassword.Message);
        Assert.Contains("does not exist", missing.Message);
        Assert.Contains("thumbprint", unknownThumbprint.Message, StringComparison.OrdinalIgnoreCase);
    }

    private IDataProtectionProvider Provider(string keysFolder, X509Certificate2? certificate)
    {
        var services = new ServiceCollection();
        var builder = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysFolder)).SetApplicationName("TicketSystemTech");
        if (certificate is not null) builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void Keys_written_with_a_certificate_are_encrypted_at_rest_and_only_that_certificate_opens_them()
    {
        var keys = Path.Combine(_folder, "keys");
        Directory.CreateDirectory(keys);
        var path = NewCertificate();
        var certificate = KeyRingProtection.Load(Config(("DataProtection:CertificatePath", path), ("DataProtection:CertificatePassword", "pw-123456")), _folder)!;

        var sealedValue = Provider(keys, certificate).CreateProtector("test").Protect("secret personal data");

        var stored = string.Concat(Directory.GetFiles(keys, "*.xml").Select(File.ReadAllText));
        Assert.Contains("EncryptedData", stored);      // the key is encrypted...
        Assert.DoesNotContain("<masterKey", stored);    // ...and the raw key material is not there
        // a fresh provider with the same certificate (a restart, another machine with the certificate) reads it back
        Assert.Equal("secret personal data", Provider(keys, certificate).CreateProtector("test").Unprotect(sealedValue));

        // whoever has the keys but not the certificate cannot
        var other = KeyRingProtection.Load(Config(("DataProtection:CertificatePath", NewCertificate("other-pw")), ("DataProtection:CertificatePassword", "other-pw")), _folder)!;
        Assert.ThrowsAny<CryptographicException>(() => Provider(keys, other).CreateProtector("test").Unprotect(sealedValue));
    }

    [Fact]
    public void Without_a_certificate_the_same_keys_are_stored_in_the_clear_which_is_what_the_check_looks_for()
    {
        var keys = Path.Combine(_folder, "plain-keys");
        Directory.CreateDirectory(keys);

        Provider(keys, null).CreateProtector("test").Protect("secret personal data");

        var stored = string.Concat(Directory.GetFiles(keys, "*.xml").Select(File.ReadAllText));
        Assert.Contains("<masterKey", stored);
        Assert.DoesNotContain("EncryptedData", stored);
    }

    // ======================= the log file =======================

    [Fact]
    public void The_log_is_written_to_a_daily_file_as_well_as_the_console()
    {
        var path = Path.Combine(_folder, "logs", "app-.log");
        var config = Config(("Logging:File:Path", path));

        using (var logger = LoggingSetup.Configure(new LoggerConfiguration(), config, _folder).CreateLogger())
            logger.Information("something happened to {Thing}", "the ticket");

        var file = Assert.Single(Directory.GetFiles(Path.Combine(_folder, "logs")));
        Assert.Matches(@"app-\d{8}\.log$", file);
        var text = File.ReadAllText(file);
        Assert.Contains("something happened to the ticket", text);
        Assert.Contains("[INF]", text);
    }

    [Fact]
    public void The_log_file_can_be_switched_off_and_a_relative_path_is_taken_from_the_application_folder()
    {
        using (var logger = LoggingSetup.Configure(new LoggerConfiguration(), Config(("Logging:File:Enabled", "false")), _folder).CreateLogger())
            logger.Information("not written to a file");
        Assert.False(Directory.Exists(Path.Combine(_folder, "logs")));

        using (var logger = LoggingSetup.Configure(new LoggerConfiguration(), Config(), _folder).CreateLogger())
            logger.Information("written to the default place");
        Assert.Single(Directory.GetFiles(Path.Combine(_folder, "logs")));
    }

    [Fact]
    public void The_log_file_keeps_only_as_many_days_as_configured()
    {
        var logs = Path.Combine(_folder, "logs");
        Directory.CreateDirectory(logs);
        foreach (var day in new[] { "20260101", "20260102", "20260103" }) File.WriteAllText(Path.Combine(logs, $"keep-{day}.log"), "old");

        using (var logger = LoggingSetup.Configure(new LoggerConfiguration(), Config(("Logging:File:Path", Path.Combine(logs, "keep-.log")), ("Logging:File:RetainedFiles", "2")), _folder).CreateLogger())
            logger.Information("today");

        // today's file plus the newest one before it
        Assert.Equal(2, Directory.GetFiles(logs).Length);
    }

    // ======================= the probe Docker asks =======================

    [Theory]
    [InlineData(null, null, 8080)]
    [InlineData("9000", null, 9000)]
    [InlineData("9000;9001", null, 9000)]
    [InlineData(null, "http://+:5114", 5114)]
    [InlineData(null, "http://*:5114;https://*:5115", 5114)]
    [InlineData(null, "http://localhost:7000", 7000)]
    [InlineData("9000", "http://+:5114", 9000)]
    [InlineData("not a port", "garbage", 8080)]
    public void The_probe_asks_the_port_the_application_was_told_to_listen_on(string? httpPorts, string? urls, int expected)
    {
        string? Env(string name) => name switch { "ASPNETCORE_HTTP_PORTS" => httpPorts, "ASPNETCORE_URLS" => urls, _ => null };

        Assert.Equal(expected, ContainerHealthProbe.ListeningPort(Env));
    }

    private static async Task<(WebApplication App, int Port)> ApplicationAsync(int healthStatus)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapGet("/health/live", () => Results.StatusCode(healthStatus));
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        return (app, port);
    }

    [Fact]
    public async Task The_probe_exits_0_for_a_live_application_and_1_for_one_that_is_not()
    {
        var (live, livePort) = await ApplicationAsync(200);
        var (sick, sickPort) = await ApplicationAsync(503);
        try
        {
            Assert.Equal(0, await ContainerHealthProbe.RunAsync(name => name == "ASPNETCORE_HTTP_PORTS" ? livePort.ToString() : null));
            Assert.Equal(1, await ContainerHealthProbe.RunAsync(name => name == "ASPNETCORE_HTTP_PORTS" ? sickPort.ToString() : null));
            Assert.Equal(1, await ContainerHealthProbe.RunAsync(name => name == "ASPNETCORE_HTTP_PORTS" ? FakeSmtpServer.UnusedPort().ToString() : null, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            await live.StopAsync();
            await sick.StopAsync();
        }
    }

    [Theory]
    [InlineData(null, "localhost")]
    [InlineData("*", "localhost")]
    [InlineData("tickets.firma.ba", "tickets.firma.ba")]
    [InlineData("tickets.firma.ba;localhost", "tickets.firma.ba")]
    [InlineData(" tickets.firma.ba ; other.ba", "tickets.firma.ba")]
    [InlineData("*.firma.ba", "health.firma.ba")]
    public void The_probe_names_a_host_the_application_accepts(string? allowedHosts, string expected)
    {
        string? Env(string name) => name == "AllowedHosts" ? allowedHosts : null;

        Assert.Equal(expected, ContainerHealthProbe.HostToSend(Env, _folder));
    }

    [Fact]
    public void Without_the_environment_variable_the_probe_reads_AllowedHosts_from_appsettings_like_the_application_does()
    {
        File.WriteAllText(Path.Combine(_folder, "appsettings.json"), """{ "AllowedHosts": "localhost;127.0.0.1" }""");
        Assert.Equal("localhost", ContainerHealthProbe.HostToSend(_ => null, _folder));

        // the environment file wins over the general one, as it does for the application
        File.WriteAllText(Path.Combine(_folder, "appsettings.Production.json"), """{ "AllowedHosts": "tickets.firma.ba" }""");
        Assert.Equal("tickets.firma.ba", ContainerHealthProbe.HostToSend(_ => null, _folder));
        Assert.Equal("localhost", ContainerHealthProbe.HostToSend(name => name == "ASPNETCORE_ENVIRONMENT" ? "Development" : null, _folder));
    }

    [Fact]
    public async Task The_probe_gets_through_host_filtering_on_a_server_that_only_accepts_its_real_name()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(o => o.AllowedHosts = ["tickets.firma.test"]);
        var app = builder.Build();
        app.UseHostFiltering();
        app.MapGet("/health/live", () => Results.Ok());
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        try
        {
            string? Env(string name) => name switch { "ASPNETCORE_HTTP_PORTS" => port.ToString(), "AllowedHosts" => "tickets.firma.test", _ => null };
            Assert.Equal(0, await ContainerHealthProbe.RunAsync(Env, baseDirectory: _folder));

            // with the list the application does not use, the probe asks for localhost and is turned away: the case the host name fixes
            string? WrongEnv(string name) => name == "ASPNETCORE_HTTP_PORTS" ? port.ToString() : null;
            Assert.Equal(1, await ContainerHealthProbe.RunAsync(WrongEnv, baseDirectory: _folder));
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
