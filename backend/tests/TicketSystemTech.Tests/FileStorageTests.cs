using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-02: uploads used to be public, unauthenticated static files.</summary>
public class FileStorageTests : IDisposable
{
    private const string Key = "3f2504e0-4f89-11d3-9a0c-0305e82c3301.pdf";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tst-" + Guid.NewGuid());

    public FileStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FileLinkService NewLinks()
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext!.Request.Scheme = "https";
        http.HttpContext.Request.Host = new HostString("api.example");
        return new FileLinkService(DataProtectionProvider.Create("file-storage-tests"), http);
    }

    private LocalFileStorage NewStorage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsPath"] = Path.Combine(_root, "private") })
            .Build();
        return new LocalFileStorage(new FakeEnv(_root), new HttpContextAccessor(), config);
    }

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.IndexOf("?t=", StringComparison.Ordinal) + 3)..]);

    [Fact]
    public void A_signed_link_opens_exactly_its_own_file()
    {
        var links = NewLinks();

        var url = links.ToDownloadUrl("/uploads/" + Key)!;

        Assert.StartsWith("https://api.example/api/files/" + Key + "?t=", url);
        Assert.True(links.IsValid(Key, TokenOf(url)));
    }

    [Fact]
    public void A_link_cannot_be_replayed_against_another_file()
    {
        var links = NewLinks();
        var token = TokenOf(links.ToDownloadUrl("/uploads/" + Key)!);

        Assert.False(links.IsValid("3f2504e0-4f89-11d3-9a0c-0305e82c3302.pdf", token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void Missing_or_garbage_tokens_are_rejected(string? token) => Assert.False(NewLinks().IsValid(Key, token));

    [Fact]
    public void A_tampered_token_is_rejected()
    {
        var links = NewLinks();
        var token = TokenOf(links.ToDownloadUrl("/uploads/" + Key)!);

        Assert.False(links.IsValid(Key, token[..^2] + (token.EndsWith("AA") ? "BB" : "AA")));
    }

    [Fact]
    public void A_token_from_another_key_ring_is_rejected()
    {
        var other = new FileLinkService(DataProtectionProvider.Create("a-different-app"), new HttpContextAccessor());
        var foreign = TokenOf(other.ToDownloadUrl("/uploads/" + Key)!);

        Assert.False(NewLinks().IsValid(Key, foreign));
    }

    [Fact]
    public void Legacy_absolute_urls_resolve_to_their_key()
    {
        var url = NewLinks().ToDownloadUrl("https://old-host.example/uploads/" + Key);

        Assert.Contains("/api/files/" + Key + "?t=", url);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("..\\..\\secrets.txt")]
    [InlineData("/uploads/..%2f..%2fetc")]
    [InlineData("https://evil.example/not-a-guid.html")]
    public void References_that_are_not_stored_keys_never_become_links(string reference) =>
        Assert.Null(NewLinks().ToDownloadUrl(reference));

    [Theory]
    [InlineData("..\\appsettings.json")]
    [InlineData("../appsettings.json")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301.pdf:stream")]
    public void Unsafe_keys_are_rejected_before_touching_the_disk(string key)
    {
        Assert.Null(NewStorage().OpenRead(key));
        Assert.False(NewLinks().IsValid(key, "x"));
    }

    [Fact]
    public async Task Saved_files_are_private_and_readable_by_key()
    {
        var storage = NewStorage();

        var reference = await storage.SaveAsync("report.pdf", "application/pdf", new MemoryStream(new byte[] { 1, 2, 3 }));
        var key = reference["/uploads/".Length..];

        Assert.StartsWith("/uploads/", reference);
        Assert.True(File.Exists(Path.Combine(_root, "private", key)));
        Assert.False(Directory.Exists(Path.Combine(_root, "wwwroot", "uploads"))); // never inside the web root

        await using var stream = storage.OpenRead(key)!;
        Assert.Equal(3, stream.Length);
    }

    [Fact]
    public async Task A_hostile_file_name_cannot_choose_the_stored_name()
    {
        var reference = await NewStorage().SaveAsync("..\\..\\evil.h tml", "text/html", new MemoryStream(new byte[] { 1 }));

        Assert.Matches(@"^/uploads/[0-9a-f-]{36}(\.[a-z0-9]+)?$", reference);
    }

    [Theory]
    [InlineData(".html")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    [InlineData(".js")]
    [InlineData("")]
    [InlineData(null)]
    public void Active_or_unknown_file_types_are_not_accepted(string? extension) =>
        Assert.False(AttachmentPolicy.IsAllowedExtension(extension));

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".PDF", "application/pdf")]
    public void Only_images_and_pdf_are_rendered_in_the_browser(string extension, string expected) =>
        Assert.Equal(expected, AttachmentPolicy.InlineContentTypeFor(extension));

    [Theory]
    [InlineData(".docx")]
    [InlineData(".txt")]
    [InlineData(".html")]
    public void Everything_else_is_forced_to_download(string extension) =>
        Assert.Null(AttachmentPolicy.InlineContentTypeFor(extension));

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public FakeEnv(string root)
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
        }

        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
