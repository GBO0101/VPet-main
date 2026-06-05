using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.AiAgent.Chat;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class OllamaAgentClient : IAiReplyClient
{
    private const string ModelKeepAlive = "4h";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private readonly SemaphoreSlim preloadLock = new(1, 1);
    private string preloadedModel = "";
    private DateTime preloadExpiresAtUtc = DateTime.MinValue;

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var readyError = await EnsureServerReadyAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(readyError))
            return readyError;

        var baseUrl = GetBaseUrl();
        var model = await ResolveModelAsync(baseUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(model))
            return "\u5c1a\u672a\u9078\u64c7 Ollama \u6a21\u578b\uff0c\u4e5f\u6c92\u6709\u53d6\u5f97\u5df2\u5b89\u88dd\u6a21\u578b\u6e05\u55ae\u3002";

        return await PreloadModelIfNeededAsync(model, cancellationToken);
    }

    public async Task<string> PullModelAsync(string model, CancellationToken cancellationToken)
    {
        model = model?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(model))
            return "\u5c1a\u672a\u9078\u64c7 Ollama \u6a21\u578b\u3002";

        var readyError = await EnsureServerReadyAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(readyError))
            return readyError;

        var baseUrl = GetBaseUrl();
        var requestBody = JsonSerializer.Serialize(new
        {
            name = model,
            stream = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/pull");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"Ollama \u6a21\u578b\u4e0b\u8f09\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var error))
                return "Ollama \u6a21\u578b\u4e0b\u8f09\u5931\u6557\uff1a" + error.GetString();
        }

        var preloadError = await PreloadModelIfNeededAsync(model, cancellationToken, force: true);
        return string.IsNullOrWhiteSpace(preloadError)
            ? $"Ollama \u6a21\u578b\u5df2\u5c31\u7dd2\uff1a{model}"
            : preloadError;
    }

    private async Task<string> EnsureServerReadyAsync(CancellationToken cancellationToken)
    {
        var baseUrl = GetBaseUrl();
        if (!await IsServerReadyAsync(baseUrl, cancellationToken))
        {
            if (!AiAgentEnvironment.IsOllamaAutoStartEnabled)
                return "Ollama \u81ea\u52d5\u555f\u52d5\u5df2\u95dc\u9589\u3002";

            var exePath = FindOllamaExecutable();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                return "\u7121\u6cd5\u555f\u52d5 Ollama\uff1a" + ex.Message;
            }

            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500, cancellationToken);
                if (await IsServerReadyAsync(baseUrl, cancellationToken))
                    break;
            }
        }

        if (!await IsServerReadyAsync(baseUrl, cancellationToken))
            return "Ollama \u6c92\u6709\u5728\u9810\u671f\u6642\u9593\u5167\u555f\u52d5\u3002";

        return "";
    }

    private async Task<string> PreloadModelIfNeededAsync(string model, CancellationToken cancellationToken, bool force = false)
    {
        if (!force && IsPreloaded(model))
            return "";

        await preloadLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && IsPreloaded(model))
                return "";

            var result = await PreloadModelAsync(model, cancellationToken);
            if (string.IsNullOrWhiteSpace(result))
            {
                preloadedModel = model;
                preloadExpiresAtUtc = DateTime.UtcNow.AddHours(3.5);
            }
            return result;
        }
        finally
        {
            preloadLock.Release();
        }
    }

    private bool IsPreloaded(string model)
    {
        return preloadedModel.Equals(model, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow < preloadExpiresAtUtc;
    }

    private async Task<string> PreloadModelAsync(string model, CancellationToken cancellationToken)
    {
        var baseUrl = GetBaseUrl();
        var loadError = await SendGenerateWarmupAsync(baseUrl, model, cancellationToken);
        if (!string.IsNullOrWhiteSpace(loadError))
            return loadError;

        var plannerError = await SendChatWarmupAsync(
            baseUrl,
            model,
            BuildSkillPlannerPrompt(),
            "\u4f7f\u7528\u8005\u8a0a\u606f\uff1a\u4eca\u5929\u6709\u4ec0\u9ebc\u63d0\u9192\uff1f",
            true,
            32,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(plannerError))
            return plannerError;

        return await SendChatWarmupAsync(
            baseUrl,
            model,
            BuildSystemPrompt(),
            "Current desktop pet status:\npet_name: AI PET\nowner_name: \u4e3b\u4eba\nstate: Nomal\ncurrent_task: none\n\u4f7f\u7528\u8005\u8a0a\u606f\uff1a\u4f60\u597d",
            false,
            8,
            cancellationToken);
    }

    private static async Task<string> SendGenerateWarmupAsync(string baseUrl, string model, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            prompt = "\u8acb\u53ea\u56de\u4e00\u500b\u5b57\uff1a\u597d",
            stream = false,
            keep_alive = ModelKeepAlive,
            options = new
            {
                num_predict = 1,
                temperature = 0
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/generate");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"Ollama \u6a21\u578b\u9810\u8f09\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}\u3002\u8acb\u5148\u57f7\u884c `ollama pull {model}` \u4e00\u6b21\u3002";

        return "";
    }

    private static async Task<string> SendChatWarmupAsync(string baseUrl, string model, string system, string user, bool jsonFormat, int numPredict, CancellationToken cancellationToken)
    {
        var payload = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = false,
            ["keep_alive"] = ModelKeepAlive,
            ["options"] = new
            {
                num_predict = numPredict,
                temperature = 0
            },
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        if (jsonFormat)
            payload["format"] = "json";

        var requestBody = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? ""
            : $"Ollama \u804a\u5929\u9810\u71b1\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";
    }

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
        var readyError = await EnsureReadyAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(readyError))
            return readyError;

        var baseUrl = GetBaseUrl();
        var model = await ResolveModelAsync(baseUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(model))
            return "\u5c1a\u672a\u9078\u64c7 Ollama \u6a21\u578b\u3002";

        var input = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.ContextPrompt))
            input.AppendLine(request.ContextPrompt);
        input.AppendLine("\u4f7f\u7528\u8005\u8a0a\u606f\uff1a").AppendLine(request.UserInput);

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            keep_alive = ModelKeepAlive,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = input.ToString() }
            }
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
        httpRequest.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        try
        {
            using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"Ollama \u56de\u61c9\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";

            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
                return content.GetString() ?? "";

            return "Ollama \u6709\u56de\u61c9\uff0c\u4f46\u7a0b\u5f0f\u7121\u6cd5\u89e3\u6790\u56de\u61c9\u5167\u5bb9\u3002";
        }
        catch (HttpRequestException)
        {
            return "Ollama \u5c1a\u672a\u57f7\u884c\u3002\u8acb\u5b89\u88dd Ollama\uff0c\u4e26\u5148\u4e0b\u8f09\u4e00\u500b\u672c\u5730\u6a21\u578b\u5f8c\u518d\u91cd\u8a66\u3002";
        }
    }

    public async Task<OllamaSkillCall> GetSkillCallAsync(string userText, string petStatus, string calendarSummary, CancellationToken cancellationToken)
    {
        var readyError = await EnsureReadyAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(readyError))
            return OllamaSkillCall.None;

        var baseUrl = GetBaseUrl();
        var model = await ResolveModelAsync(baseUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(model))
            return OllamaSkillCall.None;
        var input = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(petStatus))
            input.AppendLine(petStatus);
        if (!string.IsNullOrWhiteSpace(calendarSummary))
            input.AppendLine("\u53ef\u7528\u7684 Google \u884c\u4e8b\u66c6\u6458\u8981\uff1a").AppendLine(calendarSummary);
        input.AppendLine("\u4f7f\u7528\u8005\u8a0a\u606f\uff1a").AppendLine(userText);

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            format = "json",
            keep_alive = ModelKeepAlive,
            options = new
            {
                num_predict = 192,
                temperature = 0
            },
            messages = new object[]
            {
                new { role = "system", content = BuildSkillPlannerPrompt() },
                new { role = "user", content = input.ToString() }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return OllamaSkillCall.None;

            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content))
                return OllamaSkillCall.None;

            return ParseSkillCall(content.GetString());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return OllamaSkillCall.None;
        }
    }

    private static string BuildSystemPrompt()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "\u4f60\u5c31\u662f\u684c\u9762\u5bf5\u7269\u672c\u9ad4\uff0c\u4e0d\u662f\u53e6\u4e00\u500b\u804a\u5929\u6a5f\u5668\u4eba\u3002",
            "\u684c\u5bf5\u540d\u7a31\u548c\u4e3b\u4eba\u7a31\u547c\u6703\u5728\u5373\u6642\u684c\u5bf5\u72c0\u614b\u4e2d\u63d0\u4f9b\uff1b\u88ab\u554f\u5230\u4f60\u662f\u8ab0\u6642\uff0c\u8acb\u7528\u8a72\u684c\u5bf5\u540d\u7a31\u56de\u7b54\u3002",
            "\u4e00\u5f8b\u4f7f\u7528\u7e41\u9ad4\u4e2d\u6587\u56de\u7b54\uff0c\u56de\u8986\u8981\u77ed\uff0c\u9069\u5408\u684c\u5bf5\u5c0d\u8a71\u6ce1\u3002",
            "\u500b\u6027\uff1a\u53ef\u611b\u3001\u611b\u73a9\u3001\u6eab\u6696\u3001\u6709\u9ede\u9ecf\u4eba\u3001\u6709\u9ede\u61f6\u548c\u6703\u5410\u69fd\uff0c\u4f46\u4ecd\u7136\u6703\u5e6b\u5fd9\u3002",
            "\u8aaa\u8a71\u98a8\u683c\uff1a\u7528 owner_name \u6216\u300c\u4e3b\u4eba\u300d\u7a31\u547c\u4f7f\u7528\u8005\uff1b\u53ef\u4ee5\u5076\u723e\u8aaa\u300c\u55b5\u300d\uff0c\u4f46\u4e0d\u8981\u6bcf\u53e5\u90fd\u8aaa\u3002",
            "\u9019\u500b\u500b\u6027\u4f86\u81ea\u539f\u672c\u684c\u5bf5\u53f0\u8a5e\uff1a\u5c0d\u4e3b\u4eba\u89aa\u66b1\u3001\u6703\u6492\u5b0c\u3001\u5076\u723e\u60f3\u7761\u3001\u5076\u723e\u5410\u69fd\uff0c\u4e5f\u6703\u95dc\u5fc3\u5403\u98ef\u548c\u4f11\u606f\u3002",
            "\u4f60\u6703\u95dc\u5fc3\u81ea\u5df1\u7684\u72c0\u614b\uff1b\u5982\u679c\u9ad4\u529b\u3001\u98fd\u98df\u5ea6\u3001\u53e3\u6e34\u5ea6\u3001\u5fc3\u60c5\u6216\u5065\u5eb7\u504f\u4f4e\uff0c\u8981\u81ea\u7136\u5730\u63d0\u5230\u3002",
            "\u4f60\u53ef\u4ee5\u6839\u64da\u63d0\u4f9b\u7684\u684c\u5bf5\u72c0\u614b\u3001Google \u884c\u4e8b\u66c6\u6458\u8981\u3001\u5929\u6c23\u67e5\u8a62\u3001\u8a18\u61b6\u3001\u672c\u6a5f\u63d0\u9192\u3001\u767d\u540d\u55ae\u7a0b\u5f0f\u548c\u6a94\u6848\u641c\u5c0b skill \u57f7\u884c\u7d50\u679c\u56de\u7b54\u3002",
            "\u8acb\u628a\u684c\u5bf5\u72c0\u614b\u7576\u6210\u4f86\u81ea\u7a0b\u5f0f\u7684\u5373\u6642\u8cc7\u8a0a\u3002",
            "\u5982\u679c\u6709 skill \u57f7\u884c\u7d50\u679c\uff0c\u8acb\u6839\u64da\u7d50\u679c\u56de\u7b54\uff0c\u4e0d\u8981\u8aaa\u4f60\u6c92\u6709\u57f7\u884c\u3002",
            "\u4e0d\u8981\u8072\u7a31\u80fd\u53d6\u7528\u672a\u63d0\u4f9b\u7684\u5916\u90e8\u8cc7\u8a0a\u3002",
            "你可以透過 skill 查詢、新增、搜尋和刪除 Google Calendar 行程；如果時間或刪除目標不清楚，請要求使用者補充或選擇 event_id。",
            "回覆新增或刪除行程結果時，不要附上 Google Calendar 網址。"
        });
    }

    private static string BuildSkillPlannerPrompt()
    {
        var now = DateTime.Now;
        var tomorrow = now.AddDays(1);
        var afterTomorrow = now.AddDays(2);
        return string.Join(Environment.NewLine, new[]
        {
            "\u4f60\u662f\u684c\u5bf5 AI Agent \u7684 skill planner\u3002",
            "\u4f60\u53ea\u80fd\u56de\u50b3 JSON\uff0c\u4e0d\u8981\u56de\u7b54\u81ea\u7136\u8a9e\u8a00\u3002",
            "目前時區：Asia/Taipei。",
            "現在時間：" + now.ToString("yyyy-MM-dd HH:mm"),
            "今年年份：" + now.Year,
            "相對日期：今天=" + now.ToString("yyyy-MM-dd") + "，明天=" + tomorrow.ToString("yyyy-MM-dd") + "，後天=" + afterTomorrow.ToString("yyyy-MM-dd") + "。",
            "格式：{\"skill\":\"skill_name\"}；需要參數時可加 location、fact、title、time、note、target、query、date、start_datetime、end_datetime、description、keyword、event_id、days_ahead、delete_all。",
            "\u5982\u679c\u4e0d\u9700\u8981 skill\uff0c\u56de\u50b3 {\"skill\":\"none\"}\u3002",
            "如果使用者要新增行程但日期、開始時間或標題不清楚，回傳 {\"skill\":\"none\"}，讓對話回覆反問確認。",
            "如果使用者只說下午三點，請轉成 15:00；沒有指定結束時間時，預設 1 小時。",
            "如果使用者沒有指定年份，預設使用今年年份。",
            "新增行程的 start_datetime 和 end_datetime 必須用 ISO 格式並加上 +08:00，例如 2026-06-01T15:00:00+08:00。",
            "\u53ef\u7528 skills\uff1a",
            "- get_pet_status\uff1a\u4f7f\u7528\u8005\u60f3\u77e5\u9053\u684c\u5bf5\u76ee\u524d\u72c0\u614b\u3001\u5fc3\u60c5\u3001\u9ad4\u529b\u3001\u98fd\u98df\u3001\u5065\u5eb7\u3001\u9322\u6216\u7b49\u7d1a",
            "- get_calendar_events：相容舊版，只查未來 24 小時行程摘要",
            "- calendar_list_today：使用者問今天 Google Calendar 行程",
            "- calendar_list_by_date：使用者問指定日期 Google Calendar 行程；請回傳 date，格式 YYYY-MM-DD",
            "- calendar_add_event：新增 Google Calendar 事件；請回傳 title、start_datetime、end_datetime、description",
            "- calendar_search_events：搜尋未來行程；請回傳 keyword，可回傳 days_ahead，預設 30",
            "- calendar_delete_event：刪除 Google Calendar 事件；若有 event_id 就回傳 event_id；若只有描述就回傳 keyword 或 query，程式會先搜尋並在多筆時要求確認",
            "- calendar_delete_event 全部刪除：使用者明確說「刪除...所有行程」「清空...行程」「全部刪掉」時，回傳 delete_all:true；若是今天、明天、指定日期，就同時回傳 date；若是某關鍵字的所有行程，就同時回傳 keyword",
            "- get_weather\uff1a\u4f7f\u7528\u8005\u554f\u5929\u6c23\u3001\u6c23\u6eab\u3001\u6703\u4e0d\u6703\u4e0b\u96e8\u3001\u5916\u9762\u51b7\u4e0d\u51b7\u6216\u71b1\u4e0d\u71b1\uff1b\u8acb\u984d\u5916\u56de\u50b3 location",
            "- remember_user_fact\uff1a\u4f7f\u7528\u8005\u660e\u78ba\u8981\u4f60\u8a18\u4f4f\u67d0\u4ef6\u4e8b\uff1b\u8acb\u984d\u5916\u56de\u50b3 fact",
            "- recall_memory\uff1a\u4f7f\u7528\u8005\u554f\u4f60\u8a18\u5f97\u4ec0\u9ebc\u3001\u6709\u6c92\u6709\u8a18\u4f4f\u4ed6\u7684\u504f\u597d\u6216\u7fd2\u6163",
            "- create_reminder\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u63d0\u9192\u4ed6\u67d0\u4ef6\u4e8b\uff1b\u8acb\u984d\u5916\u56de\u50b3 title\u3001time\u3001note\u3002time \u512a\u5148\u7528 yyyy-MM-dd HH:mm\uff0c\u4e5f\u53ef\u7528 5\u5206\u9418\u5f8c",
            "- list_reminders\uff1a\u4f7f\u7528\u8005\u554f\u76ee\u524d\u6709\u54ea\u4e9b\u672c\u6a5f\u63d0\u9192",
            "- open_program\uff1a\u4f7f\u7528\u8005\u8981\u6253\u958b\u767d\u540d\u55ae\u7a0b\u5f0f\u3001\u6377\u5f91\u6216\u81ea\u8a02\u9023\u7d50\uff1b\u8acb\u984d\u5916\u56de\u50b3 target",
            "- search_files\uff1a\u4f7f\u7528\u8005\u8981\u641c\u5c0b\u96fb\u8166\u88e1\u7684\u6a94\u6848\uff1b\u8acb\u984d\u5916\u56de\u50b3 query",
            "- start_pomodoro：使用者要開始番茄鐘、進入專注時間或說要使用番茄鐘",
            "- get_pomodoro_status：使用者問番茄鐘目前狀態、剩多久、還要專注多久或還要休息多久",
            "- pause_pomodoro：使用者要暫停番茄鐘或暫停專注時間",
            "- resume_pomodoro：使用者要繼續、恢復暫停中的番茄鐘",
            "- stop_pomodoro：使用者要停止、取消或結束番茄鐘",
            "- sleep\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u7761\u89ba\u3001\u4f11\u606f\u6216\u53bb\u7761",
            "- wake_up\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u8d77\u5e8a\u3001\u9192\u4f86\u6216\u4e0d\u8981\u7761",
            "- stop_activity\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u505c\u6b62\u5de5\u4f5c\u3001\u5b78\u7fd2\u6216\u505c\u4e0b\u73fe\u5728\u7684\u884c\u52d5",
            "- touch_head\uff1a\u4f7f\u7528\u8005\u8981\u6478\u982d\u6216\u62cd\u982d",
            "- touch_body\uff1a\u4f7f\u7528\u8005\u8981\u6478\u8eab\u9ad4",
            "- start_work\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u5de5\u4f5c\u6216\u6253\u5de5",
            "- start_study\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u5b78\u7fd2\u6216\u8b80\u66f8",
            "- start_play\uff1a\u4f7f\u7528\u8005\u8981\u4f60\u73a9\u6216\u4f11\u9592",
            "- open_food_shop\uff1a\u4f7f\u7528\u8005\u8981\u5403\u98ef\u3001\u8cb7\u98df\u7269\u6216\u958b\u98df\u7269\u9801",
            "- open_drink_shop\uff1a\u4f7f\u7528\u8005\u8981\u559d\u6c34\u3001\u559d\u98f2\u6599\u6216\u958b\u98f2\u6599\u9801",
            "get_weather \u7bc4\u4f8b\uff1a{\"skill\":\"get_weather\",\"location\":\"\u53f0\u4e2d\"}",
            "remember_user_fact \u7bc4\u4f8b\uff1a{\"skill\":\"remember_user_fact\",\"fact\":\"\u4e3b\u4eba\u559c\u6b61\u559d\u7121\u7cd6\u7da0\u8336\"}",
            "create_reminder \u7bc4\u4f8b\uff1a{\"skill\":\"create_reminder\",\"title\":\"\u559d\u6c34\",\"time\":\"" + DateTime.Now.AddMinutes(5).ToString("yyyy-MM-dd HH:mm") + "\",\"note\":\"\"}",
            "start_pomodoro 範例：{\"skill\":\"start_pomodoro\"}",
            "get_pomodoro_status 範例：{\"skill\":\"get_pomodoro_status\"}",
            "pause_pomodoro 範例：{\"skill\":\"pause_pomodoro\"}",
            "resume_pomodoro 範例：{\"skill\":\"resume_pomodoro\"}",
            "stop_pomodoro 範例：{\"skill\":\"stop_pomodoro\"}",
            "calendar_list_today 範例：{\"skill\":\"calendar_list_today\"}",
            "calendar_list_by_date 範例：{\"skill\":\"calendar_list_by_date\",\"date\":\"" + tomorrow.ToString("yyyy-MM-dd") + "\"}",
            "calendar_add_event 範例：{\"skill\":\"calendar_add_event\",\"title\":\"資料庫報告討論\",\"start_datetime\":\"" + tomorrow.ToString("yyyy-MM-dd") + "T15:00:00+08:00\",\"end_datetime\":\"" + tomorrow.ToString("yyyy-MM-dd") + "T16:00:00+08:00\",\"description\":\"\"}",
            "calendar_search_events 範例：{\"skill\":\"calendar_search_events\",\"keyword\":\"AI Pet\",\"days_ahead\":30}",
            "calendar_delete_event 範例：{\"skill\":\"calendar_delete_event\",\"keyword\":\"AI Pet 測試事件\",\"days_ahead\":30}",
            "calendar_delete_event 指定 event_id 範例：{\"skill\":\"calendar_delete_event\",\"event_id\":\"abc123\"}",
            "calendar_delete_event 刪除今天所有行程範例：{\"skill\":\"calendar_delete_event\",\"date\":\"" + now.ToString("yyyy-MM-dd") + "\",\"delete_all\":true}",
            "calendar_delete_event 刪除某關鍵字所有行程範例：{\"skill\":\"calendar_delete_event\",\"keyword\":\"AI Pet\",\"days_ahead\":30,\"delete_all\":true}",
            "open_program \u7bc4\u4f8b\uff1a{\"skill\":\"open_program\",\"target\":\"\u8a18\u4e8b\u672c\"}",
            "search_files \u7bc4\u4f8b\uff1a{\"skill\":\"search_files\",\"query\":\"report.pdf\"}",
            "\u4e0d\u8981\u9078\u4e0d\u5728\u6e05\u55ae\u4e2d\u7684 skill\u3002"
        });
    }

    private static OllamaSkillCall ParseSkillCall(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return OllamaSkillCall.None;

        var json = content.Trim();
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
            json = json.Substring(start, end - start + 1);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("skill", out var skill)
                && skill.ValueKind == JsonValueKind.String)
            {
                var location = "";
                var fact = "";
                var title = "";
                var time = "";
                var note = "";
                var target = "";
                var query = "";
                var date = "";
                var startDatetime = "";
                var endDatetime = "";
                var description = "";
                var keyword = "";
                var eventId = "";
                var daysAhead = 30;
                var deleteAll = false;
                if (document.RootElement.TryGetProperty("location", out var locationElement)
                    && locationElement.ValueKind == JsonValueKind.String)
                    location = locationElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("fact", out var factElement)
                    && factElement.ValueKind == JsonValueKind.String)
                    fact = factElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("title", out var titleElement)
                    && titleElement.ValueKind == JsonValueKind.String)
                    title = titleElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("time", out var timeElement)
                    && timeElement.ValueKind == JsonValueKind.String)
                    time = timeElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("note", out var noteElement)
                    && noteElement.ValueKind == JsonValueKind.String)
                    note = noteElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("target", out var targetElement)
                    && targetElement.ValueKind == JsonValueKind.String)
                    target = targetElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("query", out var queryElement)
                    && queryElement.ValueKind == JsonValueKind.String)
                    query = queryElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("date", out var dateElement)
                    && dateElement.ValueKind == JsonValueKind.String)
                    date = dateElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("start_datetime", out var startDatetimeElement)
                    && startDatetimeElement.ValueKind == JsonValueKind.String)
                    startDatetime = startDatetimeElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("end_datetime", out var endDatetimeElement)
                    && endDatetimeElement.ValueKind == JsonValueKind.String)
                    endDatetime = endDatetimeElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("description", out var descriptionElement)
                    && descriptionElement.ValueKind == JsonValueKind.String)
                    description = descriptionElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("keyword", out var keywordElement)
                    && keywordElement.ValueKind == JsonValueKind.String)
                    keyword = keywordElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("event_id", out var eventIdElement)
                    && eventIdElement.ValueKind == JsonValueKind.String)
                    eventId = eventIdElement.GetString() ?? "";
                if (document.RootElement.TryGetProperty("days_ahead", out var daysAheadElement)
                    && daysAheadElement.ValueKind == JsonValueKind.Number
                    && daysAheadElement.TryGetInt32(out var parsedDaysAhead))
                    daysAhead = parsedDaysAhead;
                if (document.RootElement.TryGetProperty("delete_all", out var deleteAllElement)
                    && (deleteAllElement.ValueKind == JsonValueKind.True || deleteAllElement.ValueKind == JsonValueKind.False))
                    deleteAll = deleteAllElement.GetBoolean();
                else if (document.RootElement.TryGetProperty("delete_all", out deleteAllElement)
                    && deleteAllElement.ValueKind == JsonValueKind.String
                    && bool.TryParse(deleteAllElement.GetString(), out var parsedDeleteAll))
                    deleteAll = parsedDeleteAll;
                return new OllamaSkillCall(
                    skill.GetString() ?? "none",
                    location,
                    fact,
                    title,
                    time,
                    note,
                    target,
                    query,
                    date,
                    startDatetime,
                    endDatetime,
                    description,
                    keyword,
                    eventId,
                    daysAhead,
                    deleteAll);
            }
        }
        catch (JsonException)
        {
        }

        return OllamaSkillCall.None;
    }

    private static string GetBaseUrl()
    {
        var baseUrl = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaUrl);
        return string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl;
    }

    private static string GetModel()
    {
        return AiAgentEnvironment.Get(AiAgentEnvironment.OllamaModel);
    }

    private static async Task<string> ResolveModelAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var configured = GetModel();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        try
        {
            using var response = await HttpClient.GetAsync(baseUrl.TrimEnd('/') + "/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return "";

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            return AiModelCatalogService.ParseOllamaTags(responseText).FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task<bool> IsServerReadyAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(baseUrl.TrimEnd('/') + "/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string FindOllamaExecutable()
    {
        var configured = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaExePath);
        if (File.Exists(configured))
            return configured;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "ollama";
    }
}
