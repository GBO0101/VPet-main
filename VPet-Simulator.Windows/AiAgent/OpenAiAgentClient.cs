using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.AiAgent.Chat;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class OpenAiAgentClient : IAiReplyClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<string> GetReplyAsync(string userText, string petStatus, string calendarSummary, CancellationToken cancellationToken)
    {
        var context = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(petStatus))
            context.AppendLine(petStatus);
        if (!string.IsNullOrWhiteSpace(calendarSummary))
            context.AppendLine("\u53ef\u7528\u80cc\u666f\u8cc7\u8a0a\uff1a").AppendLine(calendarSummary);

        return await GenerateReplyAsync(new AiReplyGenerationRequest
        {
            SystemPrompt = new PersonalitySkill().BuildSystemPrompt(new PersonalitySkill().GetProfile(AiConversationContext.ForTest(userText))),
            ContextPrompt = context.ToString(),
            UserInput = userText
        }, cancellationToken);
    }

    public async Task<string> GenerateReplyAsync(AiReplyGenerationRequest request, CancellationToken cancellationToken)
    {
        var apiKey = AiAgentEnvironment.GetRemoteApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return "\u5c1a\u672a\u8a2d\u5b9a遠端 API Key\u3002";

        var model = AiAgentEnvironment.GetRemoteApiModel();
        if (string.IsNullOrWhiteSpace(model))
            return "\u5c1a\u672a\u9078\u64c7遠端 API 模型\u3002";

        var baseUrl = GetRemoteBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "\u5c1a\u672a\u8a2d\u5b9a遠端 API Base URL\u3002";

        var inputBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.ContextPrompt))
            inputBuilder.AppendLine(request.ContextPrompt);
        inputBuilder.AppendLine("\u4f7f\u7528\u8005\u8a0a\u606f\uff1a").AppendLine(request.UserInput);

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            instructions = request.SystemPrompt,
            input = inputBuilder.ToString()
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return await GetChatCompletionReplyAsync(baseUrl, apiKey, model, request, inputBuilder.ToString(), cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
            return outputText.GetString() ?? "";

        if (document.RootElement.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var text))
                        return text.GetString() ?? "";
                }
            }
        }

        return "\u9060\u7aef API \u6709\u56de\u61c9\uff0c\u4f46\u7a0b\u5f0f\u7121\u6cd5\u89e3\u6790\u56de\u61c9\u5167\u5bb9\u3002";
    }

    private static async Task<string> GetChatCompletionReplyAsync(string baseUrl, string apiKey, string model, AiReplyGenerationRequest request, string input, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = input }
            }
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"\u9060\u7aef API \u56de\u61c9\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";

        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.TryGetProperty("choices", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var content))
                    return content.GetString() ?? "";
            }
        }

        return "\u9060\u7aef API \u6709\u56de\u61c9\uff0c\u4f46\u7a0b\u5f0f\u7121\u6cd5\u89e3\u6790\u56de\u61c9\u5167\u5bb9\u3002";
    }

    private static string GetRemoteBaseUrl()
    {
        var baseUrl = AiAgentEnvironment.Get(AiAgentEnvironment.RemoteApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return baseUrl;

        return string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiApiKey))
            ? ""
            : "https://api.openai.com";
    }
}
