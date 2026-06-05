using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class WeatherSkillClientTests
{
    [Fact]
    public void ParseCwaForecastBuildsReadableSummary()
    {
        const string json = """
        {
          "success": "true",
          "records": {
            "location": [
              {
                "locationName": "臺中市",
                "weatherElement": [
                  {
                    "elementName": "Wx",
                    "time": [
                      {
                        "startTime": "2026-06-05 18:00:00",
                        "endTime": "2026-06-06 06:00:00",
                        "parameter": { "parameterName": "多雲短暫陣雨" }
                      }
                    ]
                  },
                  {
                    "elementName": "PoP",
                    "time": [
                      {
                        "startTime": "2026-06-05 18:00:00",
                        "endTime": "2026-06-06 06:00:00",
                        "parameter": { "parameterName": "40" }
                      }
                    ]
                  },
                  {
                    "elementName": "MinT",
                    "time": [
                      {
                        "startTime": "2026-06-05 18:00:00",
                        "endTime": "2026-06-06 06:00:00",
                        "parameter": { "parameterName": "25" }
                      }
                    ]
                  },
                  {
                    "elementName": "MaxT",
                    "time": [
                      {
                        "startTime": "2026-06-05 18:00:00",
                        "endTime": "2026-06-06 06:00:00",
                        "parameter": { "parameterName": "31" }
                      }
                    ]
                  },
                  {
                    "elementName": "CI",
                    "time": [
                      {
                        "startTime": "2026-06-05 18:00:00",
                        "endTime": "2026-06-06 06:00:00",
                        "parameter": { "parameterName": "舒適至悶熱" }
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        var summary = WeatherSkillClient.ParseCwaForecastForTest(json, "臺中市");

        Assert.Contains("臺中市", summary);
        Assert.Contains("多雲短暫陣雨", summary);
        Assert.Contains("25-31°C", summary);
        Assert.Contains("降雨機率 40%", summary);
        Assert.Contains("舒適至悶熱", summary);
    }
}
