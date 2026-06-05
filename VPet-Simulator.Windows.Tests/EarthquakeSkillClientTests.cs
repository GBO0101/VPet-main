using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class EarthquakeSkillClientTests
{
    [Fact]
    public void ParseCwaEarthquakeReportBuildsReadableSummary()
    {
        const string json = """
        {
          "success": "true",
          "records": {
            "Earthquake": [
              {
                "EarthquakeNo": 2026001,
                "ReportContent": "06/05-21:10花蓮縣近海發生規模4.8有感地震，最大震度花蓮縣3級。",
                "OriginTime": "2026-06-05 21:10:00",
                "FocalDepth": 12.3,
                "Location": "花蓮縣政府東方 20.0 公里 (位於花蓮縣近海)",
                "EarthquakeInfo": {
                  "EarthquakeMagnitude": {
                    "MagnitudeType": "芮氏規模",
                    "MagnitudeValue": 4.8
                  }
                },
                "Intensity": {
                  "ShakingArea": [
                    {
                      "CountyName": "花蓮縣",
                      "AreaIntensity": "3級"
                    }
                  ]
                }
              }
            ]
          }
        }
        """;

        var summary = EarthquakeSkillClient.ParseCwaEarthquakeReportForTest(json);

        Assert.Contains("中央氣象署地震快訊", summary);
        Assert.Contains("2026-06-05 21:10", summary);
        Assert.Contains("規模 4.8", summary);
        Assert.Contains("深度 12.3 公里", summary);
        Assert.Contains("最大震度 花蓮縣 3級", summary);
    }
}
