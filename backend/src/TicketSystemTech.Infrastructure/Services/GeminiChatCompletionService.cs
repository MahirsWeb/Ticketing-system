using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TicketSystemTech.Application.Common.Interfaces;

namespace TicketSystemTech.Infrastructure.Services;

/// <summary>
/// Answers questions using Google's Gemini API, strictly grounded in the context passed in
/// (retrieved knowledge-base chunks). Never falls back to the model's general knowledge.
/// </summary>
public class GeminiChatCompletionService : IChatCompletionService
{
    private const string Model = "gemini-flash-lite-latest";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiChatCompletionService> _logger;

    public GeminiChatCompletionService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiChatCompletionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> AskAsync(string systemInstruction, string context, string question, CancellationToken ct = default)
    {
        var apiKey = _configuration["GoogleAi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[AI:NOT_CONFIGURED] GoogleAi:ApiKey is not set — AI chat is disabled.");
            return null;
        }

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";

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

            var response = await _httpClient.PostAsJsonAsync(url, new
            {
                system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                generationConfig = new { temperature = 0.2 }
            }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var reason = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "RATE_LIMITED" : "API_ERROR";
                _logger.LogWarning("[AI:{Reason}] Gemini chat request failed with status {Status}: {Body}",
                    reason, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI:UNEXPECTED_ERROR] Failed to get a Gemini chat completion.");
            return null;
        }
    }
}
