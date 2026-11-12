using Microsoft.Extensions.Configuration;
using TicketSystemTech.Api.Configuration;
using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

/// <summary>T-11, T-26 and T-03: what may leave for the AI provider, and how much of it; and the proxy settings.</summary>
public class AiGuardrailsTests
{
    // ---------------- personal data is masked before text goes to an outside provider ----------------

    [Fact]
    public void The_names_of_the_people_a_text_is_about_are_masked_whole_word_in_any_case()
    {
        var names = new[] { "Edin Kovač", "Ivana Gavrić-Marić" };
        Assert.Equal("[name] je javio da [name] nije stigla. Marićka ostaje, kao i Edinburgh.",
            PiiRedactor.RedactNames("Edin je javio da gavrić nije stigla. Marićka ostaje, kao i Edinburgh.", names));
        Assert.Equal("[name] [name] [name]", PiiRedactor.RedactNames("KOVAČ Edin Ivana", names));
    }

    [Fact]
    public void The_name_of_the_company_is_masked_as_a_whole_phrase_in_any_case_and_only_as_a_whole_word()
    {
        Assert.Equal("Javio se [company] i [company]; Alfabet ostaje.",
            PiiRedactor.RedactCompany("Javio se Alfa d.o.o. i ALFA D.O.O.; Alfabet ostaje.", "Alfa d.o.o."));
        Assert.Equal("Alfa je tu.", PiiRedactor.RedactCompany("Alfa je tu.", "Al"));
        Assert.Equal("Alfa je tu.", PiiRedactor.RedactCompany("Alfa je tu.", null));
    }

    [Fact]
    public void A_text_with_no_names_listed_or_with_parts_too_short_to_mask_is_left_alone()
    {
        Assert.Equal("Ana i Al su zvali.", PiiRedactor.RedactNames("Ana i Al su zvali.", new[] { "Al Je" }));
        Assert.Equal("Edin je zvao.", PiiRedactor.RedactNames("Edin je zvao.", Array.Empty<string>()));
        Assert.Equal(string.Empty, PiiRedactor.RedactNames(null, new[] { "Edin" }));
    }

    [Theory]
    [InlineData("Pišite mi na ana.horvat@firma.ba hvala", "Pišite mi na [email] hvala")]
    [InlineData("kontakt: Ana_H+tiketi@sub.firma.co.uk.", "kontakt: [email].")]
    public void E_mail_addresses_are_masked(string text, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(text));

    [Theory]
    [InlineData("Nazovite me na +387 61 123 456 poslije podne", "Nazovite me na [phone] poslije podne")]
    [InlineData("tel 00387 61 123 456.", "tel [phone].")]
    [InlineData("+387 (0)61 123 456", "[phone]")]
    [InlineData("mob 061/123-456 ili 061 123 456", "mob [phone] ili [phone]")]
    [InlineData("fiksni 033-123-456", "fiksni [phone]")]
    [InlineData("0612345678", "[phone]")]
    public void Phone_numbers_are_masked(string text, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(text));

    [Theory]
    [InlineData("Vaš tiket #661223 kreiran 20/01/2025 11:47:59 je zatvoren.")]
    [InlineData("Datum 05/03/2025 14:30, rok 19.10.2026.")]
    [InlineData("Tiket OST-331471 i 100002, verzija Windows 10.")]
    [InlineData("IP adresa servera je 192.168.1.15, port 1433.")]
    [InlineData("Zatvoreno 2024-07-22T08:39:57Z, iznos 1.234,56 KM.")]
    [InlineData("IBAN BA39 1290 0794 0102 8494")]
    [InlineData("Trajanje 10:30 do 12:45, 3 sata.")]
    public void Ordinary_text_is_left_alone(string text) =>
        Assert.Equal(text, PiiRedactor.Redact(text));

    // TS-11.2: a password written into a note must not travel to the provider.
    [Theory]
    [InlineData("kontakt Test, lozinka: Proba123. Hvala", "kontakt Test, lozinka: [secret] Hvala")]
    [InlineData("Lozinka = Proba123", "Lozinka = [secret]")]
    [InlineData("šifra:abc-9", "šifra:[secret]")]
    [InlineData("password: hunter2 i token=abc.def", "password: [secret] i token=[secret]")]
    [InlineData("API key: AIzaSyD-EXAMPLE", "API key: [secret]")]
    [InlineData("Authorization: Bearer abcdefghijklmnop1234567890", "Authorization: [secret]")]
    [InlineData("jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl istekao", "jwt [secret] istekao")]
    public void Credentials_written_next_to_a_word_that_names_them_are_masked(string text, string expected) =>
        Assert.Equal(expected, PiiRedactor.Redact(text));

    [Theory]
    [InlineData("Lozinka je istekla, korisnik traži reset.")]
    [InlineData("The password has expired and a pin pad is broken.")]
    [InlineData("Token za štampač nije dostupan, pass kroz proxy radi.")]
    [InlineData("Tajna služba nije povezana s ovim tiketom.")]
    public void Ordinary_sentences_that_only_mention_those_words_are_left_alone(string text) =>
        Assert.Equal(text, PiiRedactor.Redact(text));

    [Fact]
    public void Digits_inside_an_email_address_are_not_read_as_a_phone_number() =>
        Assert.Equal("[email]", PiiRedactor.Redact("0612345678@sms.firma.ba"));

    [Fact]
    public void Nothing_or_empty_is_fine()
    {
        Assert.Equal(string.Empty, PiiRedactor.Redact(null));
        Assert.Equal(string.Empty, PiiRedactor.Redact(""));
    }

    // ---------------- how much text one question may cost ----------------

    [Fact]
    public void A_long_passage_is_cut_and_the_cut_is_visible()
    {
        var context = AiPromptLimits.BuildContext(new[] { new string('a', AiPromptLimits.MaxEntryChars + 500) });

        Assert.Equal(AiPromptLimits.MaxEntryChars + 1, context.Length); // the cut text plus the ellipsis
        Assert.EndsWith("…", context);
    }

    [Fact]
    public void Passages_that_would_pass_the_ceiling_are_left_out_best_match_first()
    {
        var entries = Enumerable.Range(0, 100).Select(i => $"{i:D3}" + new string('x', AiPromptLimits.MaxEntryChars - 3)).ToList();

        var context = AiPromptLimits.BuildContext(entries);

        Assert.True(context.Length <= AiPromptLimits.MaxContextChars);
        Assert.StartsWith("000", context);                 // the best match is kept
        Assert.DoesNotContain("099", context);             // the worst ones are not
        Assert.Contains("\n---\n", context);
    }

    [Fact]
    public void A_few_short_passages_are_joined_unchanged() =>
        Assert.Equal("one\n---\ntwo\n---\nthree", AiPromptLimits.BuildContext(new[] { "one", "two", "three" }));

    [Fact]
    public void No_passages_means_an_empty_context() =>
        Assert.Equal(string.Empty, AiPromptLimits.BuildContext(Array.Empty<string>()));

    // ---------------- proxies that may speak for a client's address ----------------

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private const string Secret = "a-secret-that-is-comfortably-longer-than-thirty-two-characters";

    private static IReadOnlyList<string> Problems(params (string Key, string Value)[] extra) =>
        StartupConfigurationValidator.Validate(
            Config(new[] { ("ConnectionStrings:DefaultConnection", "Server=x"), ("Jwt:Secret", Secret) }.Concat(extra).ToArray()),
            isDevelopment: true);

    [Fact]
    public void Listed_proxies_and_networks_are_accepted() =>
        Assert.Empty(Problems(("ForwardedHeaders:KnownProxies:0", "10.1.2.3"), ("ForwardedHeaders:KnownProxies:1", "2001:db8::1"),
            ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8")));

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "10.0.0.0/8", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0", "KnownNetworks")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "nonsense/8", "KnownNetworks")]
    public void A_mistyped_proxy_setting_is_reported_with_its_value(string key, string value, string name)
    {
        var problem = Assert.Single(Problems((key, value)));

        Assert.Contains(name, problem);
        Assert.Contains(value, problem);
    }
}
