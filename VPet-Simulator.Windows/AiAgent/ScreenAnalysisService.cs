using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class ScreenAnalysisService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly string visionModel;
    private readonly string baseUrl;

    public ScreenAnalysisService()
    {
        visionModel = AiAgentEnvironment.Get(AiAgentEnvironment.VisionModel);
        if (string.IsNullOrWhiteSpace(visionModel))
            visionModel = "llava:7b";
        baseUrl = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://localhost:11434";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(visionModel);

    public async Task<string> AnalyzeAsync(string base64Image, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return "";

        try
        {
            var payload = new
            {
                model = visionModel,
                stream = false,
                keep_alive = "10m",
                options = new { num_predict = 128, temperature = 0.3 },
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = "描述這張圖片中的人在電腦前做什麼，用一句繁體中文。如果看不出來就說「沒什麼特別的」。",
                        images = new[] { base64Image }
                    }
                }
            };

            var requestBody = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return "";

            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
                return content.GetString()?.Trim() ?? "";

            return "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<string> GenerateProactiveMessageAsync(string screenDescription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(screenDescription))
            return "";

        try
        {
            var payload = new
            {
                model = visionModel,
                stream = false,
                keep_alive = "10m",
                options = new { num_predict = 96, temperature = 0.7 },
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = $"你是一隻可愛的桌寵，偷偷看到使用者在：{screenDescription}\n請用桌寵的口吻，給一句簡短、自然、有溫度的回應或建議。不要問句，不要加表情符號，10秒以內能說完的長度。"
                    }
                }
            };

            var requestBody = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return "";

            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
                return content.GetString()?.Trim() ?? "";

            return "";
        }
        catch
        {
            return "";
        }
    }
}
