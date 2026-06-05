using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class LocationSkillClient
{
    private readonly HttpClient httpClient;

    public LocationSkillClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
    {
    }

    internal LocationSkillClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken)
    {
        var configured = AiAgentEnvironment.Get(AiAgentEnvironment.DefaultLocation);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        try
        {
            using var response = await httpClient.GetAsync("https://ipwho.is/", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return "";

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseIpApiLocation(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return "";
        }
    }

    internal static string ParseIpApiLocationForTest(string json) => ParseIpApiLocation(json);

    private static string ParseIpApiLocation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var status = GetString(root, "status");
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("success", StringComparison.OrdinalIgnoreCase))
            return "";
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            return "";
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
            return "";

        var city = NormalizeTaiwanCity(GetString(root, "city"));
        var countryCode = FirstNonEmpty(GetString(root, "countryCode"), GetString(root, "country_code"));
        if (countryCode.Equals("TW", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(city))
            return city;

        var region = FirstNonEmpty(GetString(root, "regionName"), GetString(root, "region"));
        var country = FirstNonEmpty(GetString(root, "country"), GetString(root, "country_name"));
        if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
            return $"{city}, {country}";
        if (!string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(country))
            return $"{region}, {country}";
        return city;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string NormalizeTaiwanCity(string city)
    {
        return city.Trim() switch
        {
            "台北" or "台北市" or "臺北" or "Taipei" or "Taipei City" => "臺北市",
            "新北" or "新北市" or "New Taipei" or "New Taipei City" => "新北市",
            "桃園" or "桃園市" or "Taoyuan" or "Taoyuan City" => "桃園市",
            "台中" or "台中市" or "臺中" or "Taichung" or "Taichung City" => "臺中市",
            "台南" or "台南市" or "臺南" or "Tainan" or "Tainan City" => "臺南市",
            "高雄" or "高雄市" or "Kaohsiung" or "Kaohsiung City" => "高雄市",
            "基隆" or "基隆市" or "Keelung" or "Keelung City" => "基隆市",
            "新竹" or "新竹市" or "Hsinchu" or "Hsinchu City" => "新竹市",
            "嘉義市" or "Chiayi City" => "嘉義市",
            "新竹縣" or "Hsinchu County" => "新竹縣",
            "苗栗" or "苗栗縣" or "Miaoli" or "Miaoli County" => "苗栗縣",
            "彰化" or "彰化縣" or "Changhua" or "Changhua County" => "彰化縣",
            "南投" or "南投縣" or "Nantou" or "Nantou County" => "南投縣",
            "雲林" or "雲林縣" or "Yunlin" or "Yunlin County" => "雲林縣",
            "嘉義" or "嘉義縣" or "Chiayi" or "Chiayi County" => "嘉義縣",
            "屏東" or "屏東縣" or "Pingtung" or "Pingtung County" => "屏東縣",
            "宜蘭" or "宜蘭縣" or "Yilan" or "Yilan County" => "宜蘭縣",
            "花蓮" or "花蓮縣" or "Hualien" or "Hualien County" => "花蓮縣",
            "台東" or "台東縣" or "臺東" or "Taitung" or "Taitung County" => "臺東縣",
            "澎湖" or "澎湖縣" or "Penghu" or "Penghu County" => "澎湖縣",
            "金門" or "金門縣" or "Kinmen" or "Kinmen County" => "金門縣",
            "連江" or "連江縣" or "Lienchiang" or "Lienchiang County" or "Matsu" => "連江縣",
            _ => city.Trim()
        };
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
