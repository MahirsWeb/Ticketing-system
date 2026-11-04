using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Answers questions through the configured provider (<c>Ai:Provider</c>: Gemini, or any OpenAI-compatible server such as a
/// local Ollama), strictly grounded in the context passed in (retrieved knowledge-base chunks). Never falls back to the
/// model's general knowledge. With <c>Ai:Enabled = false</c>, or with nothing to call, it returns null and makes no network call.
/// </summary>
public class AiChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiChatCompletionService> _logger;

    public AiChatCompletionService(HttpClient httpClient, IConfiguration configuration, ILogger<AiChatCompletionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsAvailable => AiSettings.From(_configuration).IsAvailable;

    public async Task<string?> AskAsync(string systemInstruction, string context, string question, CancellationToken ct = default)
    {
        var settings = AiSettings.From(_configuration);
        if (!settings.Enabled || settings.Provider == AiProvider.None)
        {
            _logger.LogDebug("[AI:DISABLED] Ai:Enabled is off — no AI request was made.");
            return null;
        }
        if (!settings.IsAvailable)
        {
            _logger.LogWarning("[AI:NOT_CONFIGURED] The AI provider ({Provider}) is not fully configured (Ai:ApiKey / Ai:Endpoint / models) — AI chat is disabled.", settings.Provider);
            return null;
        }

        // What leaves the system: e-mail addresses and phone numbers are masked (Ai:RedactPersonalData, on by default), and the
        // amount of text is capped whatever the caller passed in. The answer is capped too (Ai:MaxOutputTokens).
        if (settings.RedactPersonalData)
        {
            context = PiiRedactor.Redact(context);
            question = PiiRedactor.Redact(question);
        }
        if (context.Length > AiPromptLimits.MaxContextChars) context = context[..AiPromptLimits.MaxContextChars];
        if (question.Length > AiPromptLimits.MaxQuestionChars) question = question[..AiPromptLimits.MaxQuestionChars];

        try
        {
            var userPrompt =
                $"""
                Context (past support tickets and documentation — this is the ONLY information you may use):
                ---
                {context}
                ---

                Question: {question}

                Answer using only the context above. If the context does not contain the answer, say clearly that
                nothing relevant was found in the knowledge base — do not guess or use outside knowledge.
                """;

            using var request = BuildRequest(settings, systemInstruction, userPrompt);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var reason = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "RATE_LIMITED" : "API_ERROR";
                _logger.LogWarning("[AI:{Reason}] The chat request failed with status {Status}: {Body}",
                    reason, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            return ReadAnswer(settings, doc.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller gave up (browser closed, "Stop" pressed): not an AI failure, and not worth a warning
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI:UNEXPECTED_ERROR] Failed to get a chat completion.");
            return null;
        }
    }

    private static HttpRequestMessage BuildRequest(AiSettings settings, string systemInstruction, string userPrompt)
    {
        if (settings.Provider == AiProvider.Gemini)
        {
            // The key travels in a header, not in the query string: HttpClient's built-in logging writes the full
            // request URI at Information level, which would put the key into every log line.
            var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/models/{settings.ChatModel}:generateContent")
            {
                Content = JsonContent.Create(new
                {
                    system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                    contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                    generationConfig = new { temperature = 0.2, maxOutputTokens = settings.MaxOutputTokens }
                })
            };
            request.Headers.Add("x-goog-api-key", settings.ApiKey);
            return request;
        }

        var openAi = new HttpRequestMessage(HttpMethod.Post, $"{settings.Endpoint}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = settings.ChatModel,
                messages = new object[]
                {
                    new { role = "system", content = systemInstruction },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2,
                max_tokens = settings.MaxOutputTokens
            })
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            openAi.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
        return openAi;
    }

    private static string? ReadAnswer(AiSettings settings, JsonElement root) =>
        settings.Provider == AiProvider.Gemini
            ? root.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()
            : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
}
