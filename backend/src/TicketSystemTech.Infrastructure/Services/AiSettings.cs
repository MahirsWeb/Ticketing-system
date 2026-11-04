using Microsoft.Extensions.Configuration;

namespace TicketSystemTech.Infrastructure.Services;

public enum AiProvider
{
    /// <summary>No AI at all (same effect as <c>Ai:Enabled = false</c>).</summary>
    None,

    /// <summary>Google's Gemini API (Google AI Studio, or Vertex AI through its compatible endpoint).</summary>
    Gemini,

    /// <summary>Any server that speaks the OpenAI REST dialect (<c>/chat/completions</c>, <c>/embeddings</c>): Ollama, vLLM,
    /// LM Studio, Azure OpenAI and OpenAI itself. This is how a model on the customer's own machine is used.</summary>
    OpenAiCompatible
}

/// <summary>
/// The <c>Ai</c> section, read at the moment of each call so that a changed setting applies without a restart.
/// Nothing leaves the installation unless <see cref="IsAvailable"/>: <c>Ai:Enabled = false</c> is a hard stop.
/// The old <c>GoogleAi:ApiKey</c> keeps working as the Gemini key.
/// </summary>
public sealed record AiSettings(
    bool Enabled, AiProvider Provider, string Endpoint, string? ApiKey,
    string ChatModel, string EmbeddingModel, int? ConfiguredDimensions,
    bool RedactPersonalData, int MaxOutputTokens, double BackgroundMinIntervalSeconds)
{
    public const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta";
    public const string DefaultGeminiChatModel = "gemini-flash-lite-latest";
    public const string DefaultGeminiEmbeddingModel = "gemini-embedding-001";

    /// <summary>What <c>gemini-embedding-001</c> returns when no size is asked for.</summary>
    public const int DefaultGeminiDimensions = 3072;

    /// <summary>True when a call can actually be made: switched on, a known provider, and what that provider needs.</summary>
    public bool IsAvailable => Enabled && Provider switch
    {
        AiProvider.Gemini => !string.IsNullOrWhiteSpace(ApiKey),
        AiProvider.OpenAiCompatible => !string.IsNullOrWhiteSpace(Endpoint)
                                       && !string.IsNullOrWhiteSpace(ChatModel) && !string.IsNullOrWhiteSpace(EmbeddingModel),
        _ => false
    };

    /// <summary>The size of the vectors this setup produces, if it is known without asking the provider.</summary>
    public int? ExpectedDimensions => ConfiguredDimensions
        ?? (Provider == AiProvider.Gemini && EmbeddingModel == DefaultGeminiEmbeddingModel ? DefaultGeminiDimensions : null);

    /// <summary>
    /// What is written next to every stored vector. A size that is not the model's own default is part of the name, because
    /// vectors of different sizes cannot be compared and changing the size must make the old ones count as "another model".
    /// </summary>
    public string EmbeddingModelId =>
        ConfiguredDimensions is { } dims && !(Provider == AiProvider.Gemini && EmbeddingModel == DefaultGeminiEmbeddingModel && dims == DefaultGeminiDimensions)
            ? $"{EmbeddingModel}@{dims}"
            : EmbeddingModel;

    public static AiSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Ai");
        var provider = ParseProvider(section["Provider"]) ?? AiProvider.Gemini;

        var apiKey = FirstNonEmpty(section["ApiKey"], configuration["GoogleAi:ApiKey"]);
        var gemini = provider == AiProvider.Gemini;

        return new AiSettings(
            Enabled: section.GetValue("Enabled", true),
            Provider: provider,
            Endpoint: (FirstNonEmpty(section["Endpoint"]) ?? (gemini ? DefaultGeminiEndpoint : string.Empty)).TrimEnd('/'),
            ApiKey: apiKey,
            ChatModel: FirstNonEmpty(section["ChatModel"]) ?? (gemini ? DefaultGeminiChatModel : string.Empty),
            EmbeddingModel: FirstNonEmpty(section["EmbeddingModel"]) ?? (gemini ? DefaultGeminiEmbeddingModel : string.Empty),
            ConfiguredDimensions: section.GetValue<int?>("EmbeddingDimensions"),
            RedactPersonalData: section.GetValue("RedactPersonalData", true),
            MaxOutputTokens: section.GetValue("MaxOutputTokens", 2048),
            BackgroundMinIntervalSeconds: Math.Max(0, section.GetValue("BackgroundMinIntervalSeconds", 2.5)));
    }

    /// <summary>The accepted spellings of <c>Ai:Provider</c>; null means the text is not one of them.</summary>
    public static AiProvider? ParseProvider(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "none" => AiProvider.None,
        "gemini" or "google" or "googleai" => AiProvider.Gemini,
        "openaicompatible" or "openai" or "ollama" => AiProvider.OpenAiCompatible,
        _ => null
    };

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
