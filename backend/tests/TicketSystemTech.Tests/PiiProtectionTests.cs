using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TicketSystemTech.Api.Configuration;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-04: a published fallback hash key, and decryption that failed open.</summary>
public class PiiProtectionTests
{
    private static readonly string LongSecret = new('k', 40);

    private static PiiProtector New(string environment = "Development", string hashKey = "", string appName = "pii-tests") =>
        new(DataProtectionProvider.Create(appName),
            Options.Create(new PiiOptions { HashKey = hashKey }),
            new FakeHostEnvironment(environment));

    [Fact]
    public void Round_trips_and_marks_ciphertext()
    {
        var pii = New();

        var cipher = pii.Encrypt("ana@example.com");

        Assert.NotEqual("ana@example.com", cipher);
        Assert.True(pii.IsProtected(cipher));
        Assert.Equal("ana@example.com", pii.Decrypt(cipher));
    }

    [Fact]
    public void Legacy_plaintext_passes_through_so_the_backfill_can_find_it()
    {
        var pii = New();

        Assert.False(pii.IsProtected("ana@example.com"));
        Assert.Equal("ana@example.com", pii.Decrypt("ana@example.com"));
    }

    [Fact]
    public void Ciphertext_from_a_lost_key_ring_throws_instead_of_being_returned_as_data()
    {
        var cipher = New(appName: "original-key-ring").Encrypt("ana@example.com");
        var afterKeyLoss = New(appName: "a-different-key-ring");

        Assert.True(afterKeyLoss.IsProtected(cipher));
        Assert.ThrowsAny<CryptographicException>(() => afterKeyLoss.Decrypt(cipher));
    }

    [Fact]
    public void Missing_hash_key_is_a_startup_error_outside_development()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => New(environment: "Production"));

        Assert.Contains("Pii:HashKey", ex.Message);
    }

    [Fact]
    public void Development_may_run_without_a_hash_key()
    {
        Assert.NotNull(New(environment: "Development").Hash("ana@example.com"));
    }

    [Fact]
    public void Hash_is_stable_case_insensitive_and_depends_on_the_key()
    {
        var a = New(hashKey: LongSecret);

        Assert.Equal(a.Hash("Ana@Example.com"), a.Hash(" ana@example.com "));
        Assert.NotEqual(a.Hash("ana@example.com"), New(hashKey: new string('z', 40)).Hash("ana@example.com"));
    }

    // ---- startup configuration ----

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public void A_complete_production_configuration_is_valid()
    {
        var config = Config(
            ("ConnectionStrings:DefaultConnection", "Server=x"), ("Jwt:Secret", LongSecret), ("Pii:HashKey", LongSecret));

        Assert.Empty(StartupConfigurationValidator.Validate(config, isDevelopment: false));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = StartupConfigurationValidator.Validate(Config(("Jwt:Secret", "short")), isDevelopment: false);

        Assert.Contains(problems, p => p.Contains("ConnectionStrings"));
        Assert.Contains(problems, p => p.Contains("Jwt:Secret"));
        Assert.Contains(problems, p => p.Contains("Pii:HashKey"));
    }

    [Fact]
    public void Development_does_not_require_a_pii_hash_key()
    {
        var config = Config(("ConnectionStrings:DefaultConnection", "Server=x"), ("Jwt:Secret", LongSecret));

        Assert.Empty(StartupConfigurationValidator.Validate(config, isDevelopment: true));
    }

    [Fact]
    public void ThrowIfInvalid_lists_the_problems()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupConfigurationValidator.ThrowIfInvalid(Config(), isDevelopment: false));

        Assert.Contains("Jwt:Secret", ex.Message);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
