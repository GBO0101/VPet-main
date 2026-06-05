using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class WeatherSkillClient
{
    private readonly HttpClient httpClient;

    public WeatherSkillClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
    {
    }

    internal WeatherSkillClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    private static readonly TaiwanLocation[] TaiwanLocations =
    {
        new("\u81fa\u5317", "Taipei City", 25.0330, 121.5654, "\u53f0\u5317", "\u81fa\u5317\u5e02", "\u53f0\u5317\u5e02", "taipei"),
        new("\u65b0\u5317", "New Taipei City", 25.0169, 121.4628, "\u65b0\u5317\u5e02", "new taipei"),
        new("\u6843\u5712", "Taoyuan City", 24.9936, 121.3010, "\u6843\u5712\u5e02", "\u6843\u56ed", "\u6843\u56ed\u5e02", "taoyuan"),
        new("\u81fa\u4e2d", "Taichung City", 24.1477, 120.6736, "\u53f0\u4e2d", "\u81fa\u4e2d\u5e02", "\u53f0\u4e2d\u5e02", "taichung"),
        new("\u81fa\u5357", "Tainan City", 22.9999, 120.2270, "\u53f0\u5357", "\u81fa\u5357\u5e02", "\u53f0\u5357\u5e02", "tainan"),
        new("\u9ad8\u96c4", "Kaohsiung City", 22.6273, 120.3014, "\u9ad8\u96c4\u5e02", "kaohsiung"),
        new("\u57fa\u9686", "Keelung City", 25.1276, 121.7392, "\u57fa\u9686\u5e02", "keelung"),
        new("\u65b0\u7af9", "Hsinchu City", 24.8138, 120.9675, "\u65b0\u7af9\u5e02", "hsinchu"),
        new("\u5609\u7fa9\u5e02", "Chiayi City", 23.4801, 120.4491, "chiayi city"),
        new("\u65b0\u7af9\u7e23", "Hsinchu County", 24.8387, 121.0177, "hsinchu county"),
        new("\u82d7\u6817", "Miaoli County", 24.5602, 120.8214, "\u82d7\u6817\u7e23", "miaoli"),
        new("\u5f70\u5316", "Changhua County", 24.0518, 120.5161, "\u5f70\u5316\u7e23", "changhua"),
        new("\u5357\u6295", "Nantou County", 23.9609, 120.9719, "\u5357\u6295\u7e23", "nantou"),
        new("\u96f2\u6797", "Yunlin County", 23.7092, 120.4313, "\u96f2\u6797\u7e23", "\u4e91\u6797", "\u4e91\u6797\u7e23", "yunlin"),
        new("\u5609\u7fa9", "Chiayi County", 23.4518, 120.2555, "\u5609\u7fa9\u7e23", "chiayi"),
        new("\u5c4f\u6771", "Pingtung County", 22.5519, 120.5487, "\u5c4f\u6771\u7e23", "pingtung"),
        new("\u5b9c\u862d", "Yilan County", 24.7021, 121.7378, "\u5b9c\u862d\u7e23", "\u5b9c\u5170", "\u5b9c\u5170\u7e23", "yilan"),
        new("\u82b1\u84ee", "Hualien County", 23.9872, 121.6015, "\u82b1\u84ee\u7e23", "\u82b1\u83b2", "\u82b1\u83b2\u7e23", "hualien"),
        new("\u81fa\u6771", "Taitung County", 22.7554, 121.1500, "\u53f0\u6771", "\u81fa\u6771\u7e23", "\u53f0\u6771\u7e23", "taitung"),
        new("\u6f8e\u6e56", "Penghu County", 23.5711, 119.5793, "\u6f8e\u6e56\u7e23", "penghu"),
        new("\u91d1\u9580", "Kinmen County", 24.4321, 118.3171, "\u91d1\u9580\u7e23", "\u91d1\u95e8", "\u91d1\u95e8\u7e23", "kinmen"),
        new("\u9023\u6c5f", "Lienchiang County", 26.1602, 119.9517, "\u9023\u6c5f\u7e23", "\u8fde\u6c5f", "\u8fde\u6c5f\u7e23", "\u99ac\u7956", "\u9a6c\u7956", "lienchiang", "matsu")
    };

    public async Task<string> GetWeatherAsync(string location, CancellationToken cancellationToken)
    {
        return await GetWeatherAsync(location, "", cancellationToken);
    }

    public async Task<string> GetWeatherAsync(string location, string requestText, CancellationToken cancellationToken)
    {
        var normalizedLocation = NormalizeLocation(location);
        try
        {
            var resolvedLocation = TryResolveTaiwanLocation(location, normalizedLocation)
                ?? await ResolveOpenMeteoLocationAsync(normalizedLocation, cancellationToken);
            if (resolvedLocation == null)
                return "\u627e\u4e0d\u5230\u9019\u500b\u5730\u9ede\u7684\u5929\u6c23\u3002";

            var cwaApiKey = AiAgentEnvironment.Get(AiAgentEnvironment.CwaApiKey);
            if (!string.IsNullOrWhiteSpace(cwaApiKey) && resolvedLocation.IsTaiwanLocation)
                return await GetCwaForecastAsync(resolvedLocation.CwaLocationName, cwaApiKey, requestText, cancellationToken);

            var weatherUrl = "https://api.open-meteo.com/v1/forecast"
                + "?latitude=" + resolvedLocation.Latitude.ToString(CultureInfo.InvariantCulture)
                + "&longitude=" + resolvedLocation.Longitude.ToString(CultureInfo.InvariantCulture)
                + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m"
                + "&forecast_days=1&timezone=auto";
            using var weatherResponse = await httpClient.GetAsync(weatherUrl, cancellationToken);
            if (!weatherResponse.IsSuccessStatusCode)
                return $"\u5929\u6c23\u67e5\u8a62\u5931\u6557\uff1a{(int)weatherResponse.StatusCode} {weatherResponse.ReasonPhrase}";

            using var weatherStream = await weatherResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var weatherDocument = await JsonDocument.ParseAsync(weatherStream, cancellationToken: cancellationToken);
            if (!weatherDocument.RootElement.TryGetProperty("current", out var current))
                return "\u5929\u6c23 API \u6709\u56de\u61c9\uff0c\u4f46\u627e\u4e0d\u5230\u5373\u6642\u5929\u6c23\u5167\u5bb9\u3002";

            var temperature = GetDouble(current, "temperature_2m");
            var apparent = GetDouble(current, "apparent_temperature");
            var humidity = GetDouble(current, "relative_humidity_2m");
            var precipitation = GetDouble(current, "precipitation");
            var wind = GetDouble(current, "wind_speed_10m");
            var code = (int)GetDouble(current, "weather_code");

            return $"\u76ee\u524d {resolvedLocation.DisplayName} \u5929\u6c23\uff1a{DescribeWeatherCode(code)}\uff0c\u6c23\u6eab {temperature:0.#}\u00b0C\uff0c\u9ad4\u611f {apparent:0.#}\u00b0C\uff0c\u6fd5\u5ea6 {humidity:0.#}%\uff0c\u964d\u96e8 {precipitation:0.#} mm\uff0c\u98a8\u901f {wind:0.#} km/h\u3002";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return "\u5929\u6c23\u67e5\u8a62\u5931\u6557\uff1a" + ex.Message;
        }
    }

    private static TaiwanLocation? TryResolveTaiwanLocation(string rawLocation, string normalizedLocation)
    {
        var candidates = new[]
        {
            NormalizeForMatch(rawLocation),
            NormalizeForMatch(normalizedLocation)
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

        foreach (var candidate in candidates)
        {
            var exact = TaiwanLocations.FirstOrDefault(location => location.Aliases.Any(alias => candidate.Equals(NormalizeForMatch(alias), StringComparison.OrdinalIgnoreCase)));
            if (exact != null)
                return exact;
        }

        foreach (var candidate in candidates)
        {
            var match = TaiwanLocations.FirstOrDefault(location => location.Aliases.Any(alias => candidate.Contains(NormalizeForMatch(alias), StringComparison.OrdinalIgnoreCase)));
            if (match != null)
                return match;
        }

        return null;
    }

    private static async Task<TaiwanLocation?> ResolveOpenMeteoLocationAsync(string location, CancellationToken cancellationToken)
    {
        location = string.IsNullOrWhiteSpace(location) ? "Taipei" : location;
        var geoUrl = "https://geocoding-api.open-meteo.com/v1/search?name="
            + Uri.EscapeDataString(location)
            + "&count=1&language=zh&format=json";
        using var geoResponse = await new HttpClient { Timeout = TimeSpan.FromSeconds(20) }.GetAsync(geoUrl, cancellationToken);
        if (!geoResponse.IsSuccessStatusCode)
            return null;

        using var geoStream = await geoResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var geoDocument = await JsonDocument.ParseAsync(geoStream, cancellationToken: cancellationToken);
        if (!geoDocument.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;

        var place = results.EnumerateArray().First();
        var latitude = place.GetProperty("latitude").GetDouble();
        var longitude = place.GetProperty("longitude").GetDouble();
        var placeName = GetString(place, "name");
        var country = GetString(place, "country");
        var admin = GetString(place, "admin1");
        var displayName = string.Join(" ", new[] { country, admin, placeName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new TaiwanLocation(displayName, displayName, latitude, longitude);
    }

    private async Task<string> GetCwaForecastAsync(string locationName, string apiKey, string requestText, CancellationToken cancellationToken)
    {
        var url = "https://opendata.cwa.gov.tw/api/v1/rest/datastore/F-C0032-001"
            + "?Authorization=" + Uri.EscapeDataString(apiKey)
            + "&locationName=" + Uri.EscapeDataString(locationName)
            + "&format=JSON";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"中央氣象署天氣查詢失敗：{(int)response.StatusCode} {response.ReasonPhrase}";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseCwaForecast(json, locationName, requestText);
    }

    internal static string ParseCwaForecastForTest(string json, string locationName)
    {
        return ParseCwaForecast(json, locationName, "");
    }

    private static string ParseCwaForecast(string json, string locationName, string requestText)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (GetString(root, "success").Equals("false", StringComparison.OrdinalIgnoreCase))
            return "中央氣象署天氣查詢失敗：API 回傳失敗。";

        if (!root.TryGetProperty("records", out var records)
            || !records.TryGetProperty("location", out var locations)
            || locations.ValueKind != JsonValueKind.Array)
        {
            return "中央氣象署 API 有回應，但找不到天氣預報內容。";
        }

        var location = locations.EnumerateArray()
            .FirstOrDefault(item => GetString(item, "locationName").Equals(locationName, StringComparison.OrdinalIgnoreCase));
        if (location.ValueKind == JsonValueKind.Undefined)
            location = locations.EnumerateArray().FirstOrDefault();
        if (location.ValueKind == JsonValueKind.Undefined)
            return "中央氣象署 API 有回應，但沒有這個地點的預報。";

        var displayName = GetString(location, "locationName");
        var wx = GetCwaElement(location, "Wx", requestText);
        var pop = GetCwaElement(location, "PoP", requestText);
        var minT = GetCwaElement(location, "MinT", requestText);
        var maxT = GetCwaElement(location, "MaxT", requestText);
        var ci = GetCwaElement(location, "CI", requestText);
        var start = GetCwaElementStart(location, "Wx", requestText);
        var end = GetCwaElementEnd(location, "Wx", requestText);
        var timeText = FormatCwaForecastTime(start, end);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(wx))
            parts.Add(wx);
        if (!string.IsNullOrWhiteSpace(minT) || !string.IsNullOrWhiteSpace(maxT))
            parts.Add($"氣溫 {minT}-{maxT}°C");
        if (!string.IsNullOrWhiteSpace(pop))
            parts.Add($"降雨機率 {pop}%");
        if (!string.IsNullOrWhiteSpace(ci))
            parts.Add(ci);

        return $"中央氣象署 {displayName} {timeText}預報：{string.Join("，", parts)}。";
    }

    private static string GetCwaElement(JsonElement location, string elementName, string requestText)
    {
        var time = GetCwaElementTime(location, elementName, requestText);
        return time.ValueKind == JsonValueKind.Undefined
            ? ""
            : GetString(time.GetProperty("parameter"), "parameterName");
    }

    private static string GetCwaElementStart(JsonElement location, string elementName, string requestText)
    {
        var time = GetCwaElementTime(location, elementName, requestText);
        return time.ValueKind == JsonValueKind.Undefined ? "" : GetString(time, "startTime");
    }

    private static string GetCwaElementEnd(JsonElement location, string elementName, string requestText)
    {
        var time = GetCwaElementTime(location, elementName, requestText);
        return time.ValueKind == JsonValueKind.Undefined ? "" : GetString(time, "endTime");
    }

    private static JsonElement GetCwaElementTime(JsonElement location, string elementName, string requestText)
    {
        if (!location.TryGetProperty("weatherElement", out var elements) || elements.ValueKind != JsonValueKind.Array)
            return default;

        var element = elements.EnumerateArray()
            .FirstOrDefault(item => GetString(item, "elementName").Equals(elementName, StringComparison.OrdinalIgnoreCase));
        if (element.ValueKind == JsonValueKind.Undefined
            || !element.TryGetProperty("time", out var times)
            || times.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        if (ShouldUseTomorrowForecast(requestText))
        {
            var tomorrow = DateTime.Today.AddDays(1);
            var tomorrowTime = times.EnumerateArray()
                .FirstOrDefault(time => DateTime.TryParse(GetString(time, "startTime"), out var startTime)
                    && startTime.Date == tomorrow);
            if (tomorrowTime.ValueKind != JsonValueKind.Undefined)
                return tomorrowTime;
        }

        return times.EnumerateArray().FirstOrDefault();
    }

    private static bool ShouldUseTomorrowForecast(string requestText)
    {
        return (requestText ?? "").Contains("明天", StringComparison.OrdinalIgnoreCase)
            || (requestText ?? "").Contains("tomorrow", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCwaForecastTime(string start, string end)
    {
        if (!DateTime.TryParse(start, out var startTime) || !DateTime.TryParse(end, out var endTime))
            return "";

        return $"{startTime:MM/dd HH:mm}-{endTime:HH:mm} ";
    }

    private static string NormalizeLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return "Taipei";

        var text = location.Trim()
            .Replace("\u7684\u5929\u6c23", "", StringComparison.Ordinal)
            .Replace("\u5929\u6c23", "", StringComparison.Ordinal)
            .Replace("\u6c23\u6eab", "", StringComparison.Ordinal)
            .Replace("\u4eca\u5929", "", StringComparison.Ordinal)
            .Replace("\u73fe\u5728", "", StringComparison.Ordinal)
            .Replace("\u600e\u9ebc\u6a23", "", StringComparison.Ordinal)
            .Replace("\u5982\u4f55", "", StringComparison.Ordinal)
            .Replace("\u6703\u4e0d\u6703\u4e0b\u96e8", "", StringComparison.Ordinal)
            .Replace("\u6703\u4e0b\u96e8\u55ce", "", StringComparison.Ordinal)
            .Replace("\u4e0b\u96e8", "", StringComparison.Ordinal)
            .Replace("\u5e7e\u5ea6", "", StringComparison.Ordinal)
            .Replace("\u51b7\u4e0d\u51b7", "", StringComparison.Ordinal)
            .Replace("\u71b1\u4e0d\u71b1", "", StringComparison.Ordinal)
            .Trim();

        return string.IsNullOrWhiteSpace(text) ? "Taipei" : text;
    }

    private static string NormalizeForMatch(string value)
    {
        return (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("\u81fa", "\u53f0", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : 0;
    }

    private static string DescribeWeatherCode(int code)
    {
        return code switch
        {
            0 => "\u6674\u6717",
            1 or 2 or 3 => "\u591a\u96f2",
            45 or 48 => "\u6709\u9727",
            51 or 53 or 55 => "\u6bdb\u6bdb\u96e8",
            56 or 57 => "\u51cd\u96e8",
            61 or 63 or 65 => "\u4e0b\u96e8",
            66 or 67 => "\u51cd\u96e8",
            71 or 73 or 75 => "\u4e0b\u96ea",
            77 => "\u96ea\u7c92",
            80 or 81 or 82 => "\u9663\u96e8",
            85 or 86 => "\u9663\u96ea",
            95 => "\u96f7\u96e8",
            96 or 99 => "\u96f7\u96e8\u593e\u51b0\u96f9",
            _ => "\u5929\u6c23\u72c0\u614b\u78bc " + code
        };
    }

    private sealed class TaiwanLocation
    {
        public TaiwanLocation(string displayName, string englishName, double latitude, double longitude, params string[] aliases)
        {
            DisplayName = displayName;
            EnglishName = englishName;
            Latitude = latitude;
            Longitude = longitude;
            Aliases = new[] { displayName, englishName }.Concat(aliases).ToArray();
            CwaLocationName = Aliases.FirstOrDefault(alias => alias.EndsWith("市", StringComparison.Ordinal) || alias.EndsWith("縣", StringComparison.Ordinal)) ?? displayName;
            IsTaiwanLocation = aliases.Length > 0;
        }

        public string DisplayName { get; }
        public string EnglishName { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public IReadOnlyList<string> Aliases { get; }
        public string CwaLocationName { get; }
        public bool IsTaiwanLocation { get; }
    }
}
