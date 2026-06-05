using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class GoogleCalendarClient
{
    private const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    private const string CalendarTimeZone = "Asia/Taipei";
    private const string RedirectUri = "http://127.0.0.1:53682/";
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    private const string DefaultAuthUri = "https://accounts.google.com/o/oauth2/v2/auth";
    private static readonly TimeSpan TaipeiOffset = TimeSpan.FromHours(8);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private string accessToken = "";
    private DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

    public bool IsConfigured => File.Exists(TokenPath)
        || !string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.GoogleRefreshToken));

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await GetAccessTokenAsync(cancellationToken, allowInteractive: true);
    }

    public async Task<string> ListTodayEventsAsync(CancellationToken cancellationToken)
    {
        return await ListEventsByDateAsync(GetTaipeiNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), cancellationToken);
    }

    public async Task<string> ListEventsByDateAsync(string date, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return "日期格式請使用 YYYY-MM-DD。";

        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TaipeiOffset);
        var end = start.AddDays(1);
        var events = await ListEventsAsync(start, end, "", 50, cancellationToken, allowInteractive: true);
        if (events.Count == 0)
            return "這天沒有行程。";

        var builder = new StringBuilder();
        builder.AppendLine($"{day:yyyy-MM-dd} 行程：");
        foreach (var calendarEvent in events)
            builder.AppendLine($"- {FormatEventTime(calendarEvent)} {calendarEvent.Summary}");
        return builder.ToString().TrimEnd();
    }

    public async Task<string> AddCalendarEventAsync(string title, string startDatetime, string endDatetime, string description, CancellationToken cancellationToken)
    {
        title = (title ?? "").Trim();
        description = (description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            return "請提供事件標題。";
        if (!TryParseIsoDateTime(startDatetime, out var start))
            return "開始時間格式請使用 ISO 格式，例如 2026-06-01T15:00:00+08:00。";
        if (!TryParseIsoDateTime(endDatetime, out var end))
            return "結束時間格式請使用 ISO 格式，例如 2026-06-01T16:00:00+08:00。";
        if (end <= start)
            return "結束時間必須晚於開始時間。";

        var token = await GetAccessTokenAsync(cancellationToken, allowInteractive: true);
        var body = JsonSerializer.Serialize(new
        {
            summary = title,
            description,
            start = new
            {
                dateTime = FormatIso(start),
                timeZone = CalendarTimeZone
            },
            end = new
            {
                dateTime = FormatIso(end),
                timeZone = CalendarTimeZone
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/calendars/primary/events?sendUpdates=none");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateGoogleApiException(response, responseText, "Google 行事曆新增");

        using var document = JsonDocument.Parse(responseText);
        var created = ParseEvent(document.RootElement);
        var eventId = created?.Id ?? "";
        var timeText = $"{start.ToOffset(TaipeiOffset):yyyy-MM-dd HH:mm} - {end.ToOffset(TaipeiOffset):HH:mm}";
        return string.Join(Environment.NewLine, new[]
        {
            "已新增行程：" + title,
            "時間：" + timeText,
            "event_id：" + eventId
        });
    }

    public async Task<string> SearchEventsAsync(string keyword, int daysAhead, CancellationToken cancellationToken)
    {
        var events = await SearchEventItemsAsync(keyword, daysAhead, cancellationToken);
        keyword = (keyword ?? "").Trim();
        if (events.Count == 0)
            return $"找不到包含「{keyword}」的未來行程。";

        var builder = new StringBuilder();
        builder.AppendLine($"找到 {events.Count} 筆 Google Calendar 行程：");
        foreach (var calendarEvent in events)
            builder.AppendLine($"- event_id: {calendarEvent.Id} | {FormatEventTime(calendarEvent)} | {calendarEvent.Summary}");
        return builder.ToString().TrimEnd();
    }

    public async Task<IReadOnlyList<CalendarEventInfo>> SearchEventItemsAsync(string keyword, int daysAhead, CancellationToken cancellationToken)
    {
        keyword = (keyword ?? "").Trim();
        if (string.IsNullOrWhiteSpace(keyword))
            return [];

        if (daysAhead <= 0)
            daysAhead = 30;

        var start = new DateTimeOffset(GetTaipeiNow().Date, TaipeiOffset);
        var end = start.AddDays(daysAhead);
        var events = await ListEventsAsync(start, end, keyword, 50, cancellationToken, allowInteractive: true);
        return events
            .Where(calendarEvent =>
                calendarEvent.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || calendarEvent.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<string> DeleteCalendarEventAsync(string eventId, CancellationToken cancellationToken)
    {
        eventId = (eventId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(eventId))
            return "請提供要刪除的 Google Calendar event_id。";

        var token = await GetAccessTokenAsync(cancellationToken, allowInteractive: true);
        var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events/"
            + Uri.EscapeDataString(eventId)
            + "?sendUpdates=none";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return "找不到指定事件，可能已經刪除或 event_id 不正確。";
        if (!response.IsSuccessStatusCode)
            throw CreateGoogleApiException(response, responseText, "Google 行事曆刪除");

        return "已刪除 Google Calendar 事件：" + eventId;
    }

    public async Task<string> DeleteCalendarEventsByDateAsync(string date, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "日期格式請使用 YYYY-MM-DD。";

        var events = await ListEventItemsByDateAsync(date, cancellationToken);
        if (events.Count == 0)
            return "這天沒有可刪除的行程。";

        var deletedTitles = new List<string>();
        foreach (var calendarEvent in events)
        {
            if (string.IsNullOrWhiteSpace(calendarEvent.Id))
                continue;

            await DeleteCalendarEventAsync(calendarEvent.Id, cancellationToken);
            deletedTitles.Add(calendarEvent.Summary);
        }

        if (deletedTitles.Count == 0)
            return "這天沒有可刪除的行程。";

        var builder = new StringBuilder();
        builder.AppendLine($"已刪除 {date} 的 {deletedTitles.Count} 筆 Google Calendar 行程：");
        foreach (var title in deletedTitles)
            builder.AppendLine("- " + title);
        return builder.ToString().TrimEnd();
    }

    public async Task<string> DeleteCalendarEventsByKeywordAsync(string keyword, int daysAhead, CancellationToken cancellationToken)
    {
        var events = await SearchEventItemsAsync(keyword, daysAhead, cancellationToken);
        keyword = (keyword ?? "").Trim();
        if (events.Count == 0)
            return $"找不到包含「{keyword}」的未來行程，沒有刪除任何事件。";

        var deletedTitles = new List<string>();
        foreach (var calendarEvent in events)
        {
            if (string.IsNullOrWhiteSpace(calendarEvent.Id))
                continue;

            await DeleteCalendarEventAsync(calendarEvent.Id, cancellationToken);
            deletedTitles.Add(calendarEvent.Summary);
        }

        if (deletedTitles.Count == 0)
            return $"找不到包含「{keyword}」的可刪除行程。";

        var builder = new StringBuilder();
        builder.AppendLine($"已刪除包含「{keyword}」的 {deletedTitles.Count} 筆 Google Calendar 行程：");
        foreach (var title in deletedTitles)
            builder.AppendLine("- " + title);
        return builder.ToString().TrimEnd();
    }

    public async Task<IReadOnlyList<CalendarEventInfo>> GetUpcomingEventsAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken, allowInteractive: false);
        if (string.IsNullOrWhiteSpace(token))
            return [];

        var start = DateTimeOffset.UtcNow;
        var end = start.Add(window);
        return await ListEventsAsync(start, end, "", 20, cancellationToken, allowInteractive: false);
    }

    private static string ProjectRoot => FindProjectRoot();

    private static string CredentialsPath => Path.Combine(ProjectRoot, "credentials.json");

    private static string TokenPath => Path.Combine(ProjectRoot, "token.json");

    private async Task<IReadOnlyList<CalendarEventInfo>> ListEventItemsByDateAsync(string date, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return [];

        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TaipeiOffset);
        var end = start.AddDays(1);
        return await ListEventsAsync(start, end, "", 250, cancellationToken, allowInteractive: true);
    }

    private static DateTime GetTaipeiNow()
    {
        return DateTimeOffset.UtcNow.ToOffset(TaipeiOffset).DateTime;
    }

    private static string FindProjectRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "credentials.json"))
                    || File.Exists(Path.Combine(directory.FullName, "VPet.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }

    private async Task<IReadOnlyList<CalendarEventInfo>> ListEventsAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        string query,
        int maxResults,
        CancellationToken cancellationToken,
        bool allowInteractive)
    {
        var token = await GetAccessTokenAsync(cancellationToken, allowInteractive);
        if (string.IsNullOrWhiteSpace(token))
            return [];

        var parameters = new Dictionary<string, string>
        {
            ["singleEvents"] = "true",
            ["orderBy"] = "startTime",
            ["maxResults"] = maxResults.ToString(CultureInfo.InvariantCulture),
            ["timeMin"] = timeMin.ToString("o", CultureInfo.InvariantCulture),
            ["timeMax"] = timeMax.ToString("o", CultureInfo.InvariantCulture),
            ["timeZone"] = CalendarTimeZone
        };
        if (!string.IsNullOrWhiteSpace(query))
            parameters["q"] = query;

        var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events?" + BuildQueryString(parameters);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateGoogleApiException(response, responseText, "Google 行事曆讀取");

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("items", out var items))
            return [];

        return items.EnumerateArray()
            .Select(ParseEvent)
            .Where(x => x != null)
            .Cast<CalendarEventInfo>()
            .OrderBy(x => x.Start)
            .ToList();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool allowInteractive)
    {
        if (!string.IsNullOrWhiteSpace(accessToken) && accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return accessToken;

        var storedToken = LoadStoredToken() ?? LoadLegacyToken();
        if (storedToken != null)
        {
            try
            {
                EnsureScopeIsEnough(storedToken);
            }
            catch (InvalidOperationException) when (!allowInteractive)
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(storedToken.AccessToken)
                && storedToken.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                accessToken = storedToken.AccessToken;
                accessTokenExpiresAt = storedToken.ExpiresAtUtc;
                return accessToken;
            }

            if (!string.IsNullOrWhiteSpace(storedToken.RefreshToken))
            {
                OAuthClient oauthClient;
                TokenResponse refreshed;
                try
                {
                    oauthClient = ResolveOAuthClient(storedToken);
                    refreshed = await RefreshAccessTokenAsync(storedToken, oauthClient, cancellationToken);
                }
                catch (InvalidOperationException) when (!allowInteractive)
                {
                    return "";
                }

                if (storedToken.LoadedFromTokenJson)
                    SaveStoredToken(refreshed, oauthClient, storedToken.RefreshToken);

                accessToken = refreshed.AccessToken;
                accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60));
                return accessToken;
            }
        }

        if (!allowInteractive)
            return "";

        var authorized = await RequestUserAuthorizationAsync(cancellationToken);
        accessToken = authorized.AccessToken;
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, authorized.ExpiresIn - 60));
        return accessToken;
    }

    private static async Task<TokenResponse> RefreshAccessTokenAsync(StoredToken storedToken, OAuthClient oauthClient, CancellationToken cancellationToken)
    {
        return await RequestTokenAsync(oauthClient.TokenUri, new Dictionary<string, string>
        {
            ["refresh_token"] = storedToken.RefreshToken,
            ["client_id"] = oauthClient.ClientId,
            ["client_secret"] = oauthClient.ClientSecret,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);
    }

    private static async Task<TokenResponse> RequestUserAuthorizationAsync(CancellationToken cancellationToken)
    {
        var oauthClient = LoadOAuthClient();
        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        var state = Guid.NewGuid().ToString("N");
        var authUrl = oauthClient.AuthUri +
            "?response_type=code" +
            "&access_type=offline" +
            "&prompt=consent" +
            "&include_granted_scopes=false" +
            "&client_id=" + Uri.EscapeDataString(oauthClient.ClientId) +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
            "&scope=" + Uri.EscapeDataString(CalendarScope) +
            "&state=" + Uri.EscapeDataString(state);

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        var contextTask = listener.GetContextAsync();
        using var registration = cancellationToken.Register(() => listener.Close());
        var context = await contextTask;
        var code = context.Request.QueryString["code"];
        var error = context.Request.QueryString["error"];
        var returnedState = context.Request.QueryString["state"];

        var html = Encoding.UTF8.GetBytes("<html><body>Google 行事曆授權流程已完成，可以關閉這個分頁。</body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html, cancellationToken);
        context.Response.Close();

        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException("Google OAuth 授權未完成：" + error);
        if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("Google OAuth 驗證失敗，請重新授權。");
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google 沒有回傳授權碼。");

        var token = await RequestTokenAsync(oauthClient.TokenUri, new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = oauthClient.ClientId,
            ["client_secret"] = oauthClient.ClientSecret,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

        if (token.RefreshToken == "")
            throw new InvalidOperationException("Google 沒有回傳 refresh token。請檢查 OAuth 同意畫面設定後，刪除 token.json 再試一次。");

        SaveStoredToken(token, oauthClient, token.RefreshToken);
        return token;
    }

    private static async Task<TokenResponse> RequestTokenAsync(string tokenUri, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync(string.IsNullOrWhiteSpace(tokenUri) ? DefaultTokenUri : tokenUri, content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractGoogleErrorMessage(responseText);
            if (message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Google OAuth token 已失效，請刪除 token.json 後重新授權。");

            throw new InvalidOperationException($"Google OAuth 失敗：{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(responseText);
        return new TokenResponse
        {
            AccessToken = document.RootElement.GetProperty("access_token").GetString() ?? "",
            RefreshToken = document.RootElement.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            ExpiresIn = document.RootElement.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600,
            Scope = document.RootElement.TryGetProperty("scope", out var scope) ? scope.GetString() ?? "" : ""
        };
    }

    private static OAuthClient LoadOAuthClient()
    {
        if (File.Exists(CredentialsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
                var root = document.RootElement;
                if (!root.TryGetProperty("installed", out var clientElement)
                    && !root.TryGetProperty("web", out clientElement))
                    throw new InvalidOperationException("credentials.json 格式不正確，請下載 OAuth Desktop App 憑證。");

                return new OAuthClient
                {
                    ClientId = clientElement.GetProperty("client_id").GetString() ?? "",
                    ClientSecret = clientElement.GetProperty("client_secret").GetString() ?? "",
                    AuthUri = clientElement.TryGetProperty("auth_uri", out var authUri) ? authUri.GetString() ?? DefaultAuthUri : DefaultAuthUri,
                    TokenUri = clientElement.TryGetProperty("token_uri", out var tokenUri) ? tokenUri.GetString() ?? DefaultTokenUri : DefaultTokenUri
                };
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("credentials.json 格式無法讀取，請重新從 Google Cloud Console 下載 OAuth Desktop App 憑證。");
            }
        }

        var clientId = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        var clientSecret = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientSecret);
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            return new OAuthClient
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                AuthUri = DefaultAuthUri,
                TokenUri = DefaultTokenUri
            };
        }

        throw new InvalidOperationException(
            "找不到 credentials.json。請到 Google Cloud Console 建立 OAuth Client（Desktop app），下載後命名為 credentials.json，放到專案根目錄："
            + ProjectRoot);
    }

    private static OAuthClient ResolveOAuthClient(StoredToken storedToken)
    {
        if (!string.IsNullOrWhiteSpace(storedToken.ClientId) && !string.IsNullOrWhiteSpace(storedToken.ClientSecret))
        {
            return new OAuthClient
            {
                ClientId = storedToken.ClientId,
                ClientSecret = storedToken.ClientSecret,
                AuthUri = DefaultAuthUri,
                TokenUri = string.IsNullOrWhiteSpace(storedToken.TokenUri) ? DefaultTokenUri : storedToken.TokenUri
            };
        }

        return LoadOAuthClient();
    }

    private static StoredToken? LoadStoredToken()
    {
        if (!File.Exists(TokenPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(TokenPath));
            var root = document.RootElement;
            return new StoredToken
            {
                AccessToken = ReadString(root, "token", "access_token"),
                RefreshToken = ReadString(root, "refresh_token"),
                TokenUri = ReadString(root, "token_uri"),
                ClientId = ReadString(root, "client_id"),
                ClientSecret = ReadString(root, "client_secret"),
                Scopes = ReadScopes(root),
                ExpiresAtUtc = ReadExpiry(root),
                LoadedFromTokenJson = true
            };
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("token.json 格式無法讀取，請刪除 token.json 後重新授權。");
        }
    }

    private static StoredToken? LoadLegacyToken()
    {
        var refreshToken = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleRefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        return new StoredToken
        {
            RefreshToken = refreshToken,
            ClientId = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId),
            ClientSecret = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientSecret),
            TokenUri = DefaultTokenUri,
            LoadedFromTokenJson = false
        };
    }

    private static void SaveStoredToken(TokenResponse token, OAuthClient oauthClient, string refreshToken)
    {
        var directory = Path.GetDirectoryName(TokenPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new
        {
            token = token.AccessToken,
            refresh_token = refreshToken,
            token_uri = oauthClient.TokenUri,
            client_id = oauthClient.ClientId,
            client_secret = oauthClient.ClientSecret,
            scopes = new[] { CalendarScope },
            expiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60)).ToString("o", CultureInfo.InvariantCulture)
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(TokenPath, json);
    }

    private static void EnsureScopeIsEnough(StoredToken storedToken)
    {
        if (storedToken.Scopes.Count == 0)
            return;

        if (!storedToken.Scopes.Any(scope => scope.Equals(CalendarScope, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Google Calendar token 權限不足。請刪除 token.json 後重新授權，並確認授權範圍包含新增與刪除行程。");
    }

    private static string ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        return "";
    }

    private static List<string> ReadScopes(JsonElement root)
    {
        if (root.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array)
            return scopes.EnumerateArray()
                .Where(scope => scope.ValueKind == JsonValueKind.String)
                .Select(scope => scope.GetString() ?? "")
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .ToList();

        if (root.TryGetProperty("scope", out var scopeText) && scopeText.ValueKind == JsonValueKind.String)
            return (scopeText.GetString() ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        return [];
    }

    private static DateTimeOffset ReadExpiry(JsonElement root)
    {
        if (!root.TryGetProperty("expiry", out var expiry) || expiry.ValueKind != JsonValueKind.String)
            return DateTimeOffset.MinValue;

        return DateTimeOffset.TryParse(expiry.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : DateTimeOffset.MinValue;
    }

    private static CalendarEventInfo? ParseEvent(JsonElement item)
    {
        if (!item.TryGetProperty("start", out var startElement))
            return null;

        var isAllDay = false;
        DateTime start;
        if (startElement.TryGetProperty("dateTime", out var startDateTime))
        {
            start = DateTimeOffset.Parse(startDateTime.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None)
                .ToOffset(TaipeiOffset)
                .DateTime;
        }
        else if (startElement.TryGetProperty("date", out var startDate))
        {
            isAllDay = true;
            start = DateTime.Parse(startDate.GetString() ?? "", CultureInfo.InvariantCulture);
        }
        else
        {
            return null;
        }

        var end = start;
        if (item.TryGetProperty("end", out var endElement))
        {
            if (endElement.TryGetProperty("dateTime", out var endDateTime))
                end = DateTimeOffset.Parse(endDateTime.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None)
                    .ToOffset(TaipeiOffset)
                    .DateTime;
            else if (endElement.TryGetProperty("date", out var endDate))
                end = DateTime.Parse(endDate.GetString() ?? "", CultureInfo.InvariantCulture);
        }

        return new CalendarEventInfo
        {
            Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Summary = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "(\u7121\u6a19\u984c)" : "(\u7121\u6a19\u984c)",
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            Description = item.TryGetProperty("description", out var description) ? description.GetString() ?? "" : ""
        };
    }

    private static bool TryParseIsoDateTime(string text, out DateTimeOffset value)
    {
        text = (text ?? "").Trim();
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string FormatIso(DateTimeOffset value)
    {
        return value.ToOffset(TaipeiOffset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string FormatEventTime(CalendarEventInfo calendarEvent)
    {
        if (calendarEvent.IsAllDay)
            return "全天";

        return calendarEvent.Start.Date == calendarEvent.End.Date
            ? $"{calendarEvent.Start:MM/dd HH:mm}-{calendarEvent.End:HH:mm}"
            : $"{calendarEvent.Start:MM/dd HH:mm}-{calendarEvent.End:MM/dd HH:mm}";
    }

    private static string BuildQueryString(Dictionary<string, string> parameters)
    {
        return string.Join("&", parameters.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    private static InvalidOperationException CreateGoogleApiException(HttpResponseMessage response, string responseText, string action)
    {
        var message = ExtractGoogleErrorMessage(responseText);
        if (response.StatusCode is HttpStatusCode.Unauthorized)
            return new InvalidOperationException("Google Calendar 授權已失效，請刪除 token.json 後重新授權。");

        if (response.StatusCode is HttpStatusCode.Forbidden)
        {
            if (message.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                || message.Contains("scope", StringComparison.OrdinalIgnoreCase))
                return new InvalidOperationException("Google Calendar token 權限不足。請刪除 token.json 後重新授權，並確認授權範圍包含新增與刪除行程。");

            if (message.Contains("has not been used", StringComparison.OrdinalIgnoreCase)
                || message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                || message.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase))
                return new InvalidOperationException("Google Calendar API 尚未啟用。請到 Google Cloud Console 啟用 Google Calendar API 後再試一次。");
        }

        return new InvalidOperationException($"{action}失敗：{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private static string ExtractGoogleErrorMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return "";

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error_description", out var description) && description.ValueKind == JsonValueKind.String)
                return description.GetString() ?? "";
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? "";
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? "";
                if (error.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
                    return status.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }

        return responseText;
    }

    private sealed class OAuthClient
    {
        public string ClientId { get; init; } = "";
        public string ClientSecret { get; init; } = "";
        public string AuthUri { get; init; } = DefaultAuthUri;
        public string TokenUri { get; init; } = DefaultTokenUri;
    }

    private sealed class StoredToken
    {
        public string AccessToken { get; init; } = "";
        public string RefreshToken { get; init; } = "";
        public string TokenUri { get; init; } = DefaultTokenUri;
        public string ClientId { get; init; } = "";
        public string ClientSecret { get; init; } = "";
        public List<string> Scopes { get; init; } = [];
        public DateTimeOffset ExpiresAtUtc { get; init; } = DateTimeOffset.MinValue;
        public bool LoadedFromTokenJson { get; init; }
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; init; } = "";
        public string RefreshToken { get; init; } = "";
        public int ExpiresIn { get; init; }
        public string Scope { get; init; } = "";
    }
}
