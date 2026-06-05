using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class EarthquakeSkillClient
{
    private readonly HttpClient httpClient;

    public EarthquakeSkillClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
    {
    }

    internal EarthquakeSkillClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<EarthquakeReportResult> GetLatestReportAsync(CancellationToken cancellationToken)
    {
        var apiKey = AiAgentEnvironment.Get(AiAgentEnvironment.CwaApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return EarthquakeReportResult.Empty("尚未設定中央氣象署 API Key，請先設定 VPET_CWA_API_KEY。");

        try
        {
            var url = "https://opendata.cwa.gov.tw/api/v1/rest/datastore/E-A0015-001"
                + "?Authorization=" + Uri.EscapeDataString(apiKey)
                + "&format=JSON"
                + "&limit=1";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return EarthquakeReportResult.Empty($"中央氣象署地震查詢失敗：{(int)response.StatusCode} {response.ReasonPhrase}");

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseCwaEarthquakeReport(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return EarthquakeReportResult.Empty("中央氣象署地震查詢失敗：" + ex.Message);
        }
    }

    internal static string ParseCwaEarthquakeReportForTest(string json)
    {
        return ParseCwaEarthquakeReport(json).Summary;
    }

    internal static EarthquakeReportResult ParseCwaEarthquakeReport(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (GetString(root, "success").Equals("false", StringComparison.OrdinalIgnoreCase))
            return EarthquakeReportResult.Empty("中央氣象署地震查詢失敗：API 回傳失敗。");

        if (!root.TryGetProperty("records", out var records)
            || !TryGetArray(records, "Earthquake", out var earthquakes))
        {
            return EarthquakeReportResult.Empty("中央氣象署 API 有回應，但找不到地震資料。");
        }

        var earthquake = earthquakes.EnumerateArray().FirstOrDefault();
        if (earthquake.ValueKind == JsonValueKind.Undefined)
            return EarthquakeReportResult.Empty("中央氣象署地震快訊：目前沒有新的顯著有感地震。");

        var id = GetString(earthquake, "EarthquakeNo");
        var originTime = GetString(earthquake, "OriginTime");
        var location = GetString(earthquake, "Location");
        var reportContent = GetString(earthquake, "ReportContent");
        var focalDepth = GetString(earthquake, "FocalDepth");
        var magnitude = GetMagnitude(earthquake);
        var maxIntensity = GetMaxIntensityText(earthquake);

        var summary = "中央氣象署地震快訊：";
        if (!string.IsNullOrWhiteSpace(originTime))
            summary += $"{FormatTime(originTime)}，";
        if (!string.IsNullOrWhiteSpace(location))
            summary += $"{location}，";
        if (!string.IsNullOrWhiteSpace(magnitude))
            summary += $"規模 {magnitude}，";
        if (!string.IsNullOrWhiteSpace(focalDepth))
            summary += $"深度 {focalDepth} 公里，";
        if (!string.IsNullOrWhiteSpace(maxIntensity))
            summary += $"最大震度 {maxIntensity}，";

        summary = summary.TrimEnd('，');
        if (!string.IsNullOrWhiteSpace(reportContent))
            summary += $"。{reportContent}";
        else
            summary += "。";

        return new EarthquakeReportResult(id, summary, originTime, magnitude, maxIntensity, false);
    }

    private static bool TryGetArray(JsonElement element, string name, out JsonElement array)
    {
        if (element.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            return true;

        var match = element.EnumerateObject()
            .FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array);
        array = match.Value;
        return array.ValueKind == JsonValueKind.Array;
    }

    private static string GetMagnitude(JsonElement earthquake)
    {
        if (earthquake.TryGetProperty("EarthquakeInfo", out var info)
            && info.TryGetProperty("EarthquakeMagnitude", out var magnitude))
        {
            var value = GetString(magnitude, "MagnitudeValue");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return GetString(earthquake, "MagnitudeValue");
    }

    private static string GetMaxIntensityText(JsonElement earthquake)
    {
        if (!earthquake.TryGetProperty("Intensity", out var intensity)
            || !TryGetArray(intensity, "ShakingArea", out var areas))
        {
            return "";
        }

        var strongest = areas.EnumerateArray()
            .Select(area => new
            {
                County = GetString(area, "CountyName"),
                Intensity = GetString(area, "AreaIntensity")
            })
            .Where(area => !string.IsNullOrWhiteSpace(area.Intensity))
            .OrderByDescending(area => ParseIntensity(area.Intensity))
            .FirstOrDefault();

        if (strongest == null)
            return "";

        return string.IsNullOrWhiteSpace(strongest.County)
            ? strongest.Intensity
            : $"{strongest.County} {strongest.Intensity}";
    }

    private static int ParseIntensity(string text)
    {
        var digits = new string((text ?? "").Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : -1;
    }

    private static string FormatTime(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var time)
            ? time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : value;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return "";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }
}

internal sealed record EarthquakeReportResult(
    string Id,
    string Summary,
    string OriginTime,
    string Magnitude,
    string MaxIntensity,
    bool IsEmpty)
{
    public static EarthquakeReportResult Empty(string summary) => new("", summary, "", "", "", true);
}
