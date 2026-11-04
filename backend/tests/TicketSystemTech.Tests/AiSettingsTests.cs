using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Services;

namespace TicketSystemTech.Tests;

/// <summary>
/// DEP-03: the AI provider is a setting, not a constant. Where a request goes, with which model and key, and whether anything
/// is sent at all, is decided by the <c>Ai</c> section; the network is replaced by a stand-in that records every request.
/// </summary>
public class AiSettingsTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<(string Method, string Url, string? Authorization, string? GoogleKey, string Body)> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("x-goog-api-key", out var key) ? key.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private const string GeminiVector = """{"embedding":{"values":[0.5,0.25,0.125]}}""";
    private const string OpenAiVector = """{"data":[{"embedding":[0.5,0.25,0.125]}]}""";
    private const string GeminiAnswer = """{"candidates":[{"content":{"parts":[{"text":"from gemini"}]}}]}""";
    private const string OpenAiAnswer = """{"choices":[{"message":{"content":"from the local model"}}]}""";

    private static readonly (string, string?)[] Local =
    {
        ("Ai:Provider", "OpenAiCompatible"), ("Ai:Endpoint", "http://127.0.0.1:11434/v1"),
        ("Ai:ChatModel", "llama3"), ("Ai:EmbeddingModel", "nomic-embed-text")
    };

    private static AiEmbeddingService Embedding(RecordingHandler handler, IConfiguration config) =>
        new(new HttpClient(handler), config, NullLogger<AiEmbeddingService>.Instance);

    private static AiChatCompletionService Chat(RecordingHandler handler, IConfiguration config) =>
        new(new HttpClient(handler), config, NullLogger<AiChatCompletionService>.Instance);

    // ======================= what the settings add up to =======================

    [Fact]
    public void Without_any_setting_the_provider_is_Gemini_and_it_needs_a_key()
    {
        var settings = AiSettings.From(Config());

        Assert.Equal(AiProvider.Gemini, settings.Provider);
        Assert.Equal(AiSettings.DefaultGeminiEndpoint, settings.Endpoint);
        Assert.Equal(AiSettings.DefaultGeminiEmbeddingModel, settings.EmbeddingModelId);
        Assert.False(settings.IsAvailable);
    }

    [Fact]
    public void The_old_GoogleAi_key_still_works_as_the_Gemini_key()
    {
        var settings = AiSettings.From(Config(("GoogleAi:ApiKey", "old-key")));

        Assert.True(settings.IsAvailable);
        Assert.Equal("old-key", settings.ApiKey);
        Assert.Equal(AiSettings.DefaultGeminiDimensions, settings.ExpectedDimensions);
    }

    [Fact]
    public void A_new_key_wins_over_the_old_one()
    {
        Assert.Equal("new-key", AiSettings.From(Config(("GoogleAi:ApiKey", "old-key"), ("Ai:ApiKey", "new-key"))).ApiKey);
    }

    [Fact]
    public void Switching_AI_off_makes_it_unavailable_whatever_else_is_set()
    {
        Assert.False(AiSettings.From(Config(("GoogleAi:ApiKey", "k"), ("Ai:Enabled", "false"))).IsAvailable);
        Assert.False(AiSettings.From(Config(("GoogleAi:ApiKey", "k"), ("Ai:Provider", "None"))).IsAvailable);
    }

    [Fact]
    public void A_local_model_needs_an_endpoint_and_both_model_names_but_no_key()
    {
        Assert.True(AiSettings.From(Config(Local)).IsAvailable);
        Assert.False(AiSettings.From(Config(("Ai:Provider", "OpenAiCompatible"), ("Ai:Endpoint", "http://x/v1"))).IsAvailable);
        Assert.False(AiSettings.From(Config(("Ai:Provider", "OpenAiCompatible"), ("Ai:ChatModel", "m"), ("Ai:EmbeddingModel", "e"))).IsAvailable);
    }

    [Theory]
    [InlineData("gemini", AiProvider.Gemini)]
    [InlineData("Google", AiProvider.Gemini)]
    [InlineData("OpenAiCompatible", AiProvider.OpenAiCompatible)]
    [InlineData("ollama", AiProvider.OpenAiCompatible)]
    [InlineData("none", AiProvider.None)]
    public void The_provider_accepts_the_documented_spellings(string text, AiProvider expected) =>
        Assert.Equal(expected, AiSettings.ParseProvider(text));

    [Theory]
    [InlineData("claude")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_provider(string? text) => Assert.Null(AiSettings.ParseProvider(text));

    [Fact]
    public void A_vector_size_that_is_not_the_models_own_becomes_part_of_the_model_name_so_old_vectors_count_as_another_model()
    {
        Assert.Equal("gemini-embedding-001", AiSettings.From(Config(("GoogleAi:ApiKey", "k"))).EmbeddingModelId);
        Assert.Equal("gemini-embedding-001", AiSettings.From(Config(("GoogleAi:ApiKey", "k"), ("Ai:EmbeddingDimensions", "3072"))).EmbeddingModelId);
        Assert.Equal("gemini-embedding-001@768", AiSettings.From(Config(("GoogleAi:ApiKey", "k"), ("Ai:EmbeddingDimensions", "768"))).EmbeddingModelId);
        Assert.Equal("nomic-embed-text", AiSettings.From(Config(Local)).EmbeddingModelId);
        Assert.Null(AiSettings.From(Config(Local)).ExpectedDimensions);
    }

    // ======================= where requests go =======================

    [Fact]
    public async Task With_a_local_model_the_requests_go_to_that_endpoint_and_nowhere_else()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/embeddings") ? Json(OpenAiVector) : Json(OpenAiAnswer));
        var config = Config(Local);

        var vector = await Embedding(handler, config).EmbedAsync("printer ne radi", TestContext.Current.CancellationToken);
        var answer = await Chat(handler, config).AskAsync("be brief", "context", "question", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 0.5f, 0.25f, 0.125f }, vector);
        Assert.Equal("from the local model", answer);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("http://127.0.0.1:11434/v1/embeddings", handler.Requests[0].Url);
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", handler.Requests[1].Url);
        Assert.DoesNotContain(handler.Requests, r => r.Url.Contains("googleapis"));
        Assert.Equal("nomic-embed-text", JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("model").GetString());
        var chat = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        Assert.Equal("llama3", chat.GetProperty("model").GetString());
        Assert.Equal("be brief", chat.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_key_for_a_local_model_is_sent_as_a_bearer_token_and_no_key_means_no_header()
    {
        var withKey = new RecordingHandler(_ => Json(OpenAiVector));
        await Embedding(withKey, Config(Local.Append(("Ai:ApiKey", "secret")).ToArray())).EmbedAsync("x y z", TestContext.Current.CancellationToken);
        var withoutKey = new RecordingHandler(_ => Json(OpenAiVector));
        await Embedding(withoutKey, Config(Local)).EmbedAsync("x y z", TestContext.Current.CancellationToken);

        Assert.Equal("Bearer secret", withKey.Requests.Single().Authorization);
        Assert.Null(withoutKey.Requests.Single().Authorization);
    }

    [Fact]
    public async Task Gemini_can_be_pointed_at_another_endpoint_and_models_and_keeps_its_key_in_a_header()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.Contains("embedContent") ? Json(GeminiVector) : Json(GeminiAnswer));
        var config = Config(("Ai:Endpoint", "https://gateway.example/v1beta/"), ("Ai:ApiKey", "g-key"),
            ("Ai:EmbeddingModel", "my-embedder"), ("Ai:ChatModel", "my-chat"), ("Ai:EmbeddingDimensions", "768"));

        await Embedding(handler, config).EmbedAsync("hello there", TestContext.Current.CancellationToken);
        await Chat(handler, config).AskAsync("s", "c", "q", TestContext.Current.CancellationToken);

        Assert.Equal("https://gateway.example/v1beta/models/my-embedder:embedContent", handler.Requests[0].Url);
        Assert.Equal("https://gateway.example/v1beta/models/my-chat:generateContent", handler.Requests[1].Url);
        Assert.All(handler.Requests, r => Assert.Equal("g-key", r.GoogleKey));
        Assert.All(handler.Requests, r => Assert.DoesNotContain("g-key", r.Url));
        Assert.Equal(768, JsonDocument.Parse(handler.Requests[0].Body).RootElement.GetProperty("outputDimensionality").GetInt32());
    }

    [Fact]
    public async Task With_AI_switched_off_nothing_is_sent_anywhere()
    {
        var handler = new RecordingHandler(_ => Json(GeminiVector));
        var config = Config(("GoogleAi:ApiKey", "k"), ("Ai:Enabled", "false"));
        var embedding = Embedding(handler, config);
        var chat = Chat(handler, config);

        Assert.Null(await embedding.EmbedAsync("anything", TestContext.Current.CancellationToken));
        Assert.Null(await chat.AskAsync("s", "c", "q", TestContext.Current.CancellationToken));
        Assert.False(embedding.IsAvailable);
        Assert.False(chat.IsAvailable);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task With_a_local_model_that_is_half_configured_nothing_is_sent()
    {
        var handler = new RecordingHandler(_ => Json(OpenAiVector));
        var config = Config(("Ai:Provider", "OpenAiCompatible"), ("Ai:Endpoint", "http://127.0.0.1:11434/v1"));

        Assert.Null(await Embedding(handler, config).EmbedAsync("anything", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_setting_that_changes_while_running_applies_to_the_next_call()
    {
        var handler = new RecordingHandler(_ => Json(OpenAiVector));
        var source = new Dictionary<string, string?>(Local.Select(l => new KeyValuePair<string, string?>(l.Item1, l.Item2)));
        var config = new ConfigurationBuilder().Add(new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = source }).Build();
        var service = Embedding(handler, config);

        Assert.NotNull(await service.EmbedAsync("first", TestContext.Current.CancellationToken));
        config["Ai:Enabled"] = "false";
        Assert.Null(await service.EmbedAsync("second", TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    // ======================= how patient it is =======================

    [Fact]
    public async Task A_person_waiting_for_a_search_is_not_made_to_queue_behind_background_indexing()
    {
        var handler = new RecordingHandler(_ => Json(GeminiVector));
        var service = Embedding(handler, Config(("GoogleAi:ApiKey", "k"), ("Ai:BackgroundMinIntervalSeconds", "1.5")));

        // Warm the spacing up with one background call, then ask interactively right after.
        await service.EmbedAsync("indexing one", EmbeddingPriority.Background, TestContext.Current.CancellationToken);
        var started = DateTime.UtcNow;
        await service.EmbedAsync("a search", EmbeddingPriority.Interactive, TestContext.Current.CancellationToken);
        await service.EmbedAsync("another search", EmbeddingPriority.Interactive, TestContext.Current.CancellationToken);
        var interactive = DateTime.UtcNow - started;

        Assert.True(interactive < TimeSpan.FromSeconds(1), $"interactive calls took {interactive.TotalSeconds:0.0}s: they must not wait for the background spacing");
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Background_calls_are_spaced_out_so_a_bulk_run_stays_inside_the_quota()
    {
        var handler = new RecordingHandler(_ => Json(GeminiVector));
        var service = Embedding(handler, Config(("GoogleAi:ApiKey", "k"), ("Ai:BackgroundMinIntervalSeconds", "0.6")));

        await service.EmbedAsync("one", EmbeddingPriority.Background, TestContext.Current.CancellationToken);
        var started = DateTime.UtcNow;
        await service.EmbedAsync("two", EmbeddingPriority.Background, TestContext.Current.CancellationToken);
        await service.EmbedAsync("three", EmbeddingPriority.Background, TestContext.Current.CancellationToken);

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(1), "two more calls need two gaps of 0.6 s");
    }

    [Fact]
    public async Task A_provider_that_says_wait_an_hour_does_not_hold_a_person_for_an_hour()
    {
        var handler = new RecordingHandler(_ =>
        {
            var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return tooMany;
        });
        var service = Embedding(handler, Config(("GoogleAi:ApiKey", "k")));
        var started = DateTime.UtcNow;

        var vector = await service.EmbedAsync("a search", EmbeddingPriority.Interactive, TestContext.Current.CancellationToken);

        Assert.Null(vector);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "the wait is capped");
        Assert.Equal(2, handler.Requests.Count); // one retry for a person, not four
    }
}
