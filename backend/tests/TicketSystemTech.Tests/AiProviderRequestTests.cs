using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>
/// What actually goes over the wire to the AI provider (T-11, T-26): the request is captured by a stand-in for the network,
/// so these tests see exactly the text Google would receive.
/// </summary>
public class AiProviderRequestTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseJson, Encoding.UTF8, "application/json") };
        }
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { ("GoogleAi:ApiKey", "test-key") }.Concat(values).Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2)))
            .Build();

    private const string ChatAnswer = """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""";

    private static async Task<(string Body, string? Answer)> AskAsync(string context, string question, params (string, string)[] config)
    {
        var handler = new CapturingHandler(ChatAnswer);
        var service = new AiChatCompletionService(new HttpClient(handler), Config(config), NullLogger<AiChatCompletionService>.Instance);

        var answer = await service.AskAsync("be helpful", context, question);
        return (handler.RequestBody!, answer);
    }

    private static string PromptOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;

    [Fact]
    public async Task E_mail_addresses_and_phone_numbers_are_masked_before_a_question_leaves_for_Google()
    {
        var (body, answer) = await AskAsync(
            context: "Ticket #1: klijent Ana (ana.horvat@firma.ba, tel +387 61 123 456) javlja da printer ne radi.",
            question: "Šta ako klijent zove sa 061/222-333?");

        var prompt = PromptOf(body);
        Assert.Equal("ok", answer);
        Assert.DoesNotContain("ana.horvat@firma.ba", body);
        Assert.DoesNotContain("123 456", body);
        Assert.DoesNotContain("222-333", body);
        Assert.Contains("[email]", prompt);
        Assert.Contains("[phone]", prompt);
        Assert.Contains("printer ne radi", prompt); // the actual problem is still there
    }

    [Fact]
    public async Task Masking_can_be_switched_off_in_configuration()
    {
        var (body, _) = await AskAsync("pišite na ana@firma.ba", "pitanje", ("Ai:RedactPersonalData", "false"));

        Assert.Contains("ana@firma.ba", body);
    }

    [Fact]
    public async Task The_answer_has_a_ceiling_in_tokens()
    {
        var (body, _) = await AskAsync("kontekst", "pitanje");
        var config = JsonDocument.Parse(body).RootElement.GetProperty("generationConfig");

        Assert.Equal(2048, config.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task The_ceiling_can_be_changed_in_configuration()
    {
        var (body, _) = await AskAsync("kontekst", "pitanje", ("Ai:MaxOutputTokens", "300"));

        Assert.Equal(300, JsonDocument.Parse(body).RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task Whatever_is_passed_in_the_amount_of_text_is_capped()
    {
        var (body, _) = await AskAsync(new string('c', 500_000), new string('q', 50_000));

        // the instructions around the question are a few hundred characters
        Assert.True(PromptOf(body).Length < AiPromptLimits.MaxContextChars + AiPromptLimits.MaxQuestionChars + 1_000);
    }

    [Fact]
    public async Task Text_to_be_embedded_is_masked_too()
    {
        var handler = new CapturingHandler("""{"embedding":{"values":[0.1,0.2,0.3]}}""");
        var service = new AiEmbeddingService(new HttpClient(handler), Config(), NullLogger<AiEmbeddingService>.Instance);

        var vector = await service.EmbedAsync("javio se ana@firma.ba, 061 123 456, ne radi štampa", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vector);
        Assert.DoesNotContain("ana@firma.ba", handler.RequestBody);
        Assert.DoesNotContain("061 123 456", handler.RequestBody);
        Assert.Contains("ne radi štampa", JsonDocument.Parse(handler.RequestBody!).RootElement
            .GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
    }
}
