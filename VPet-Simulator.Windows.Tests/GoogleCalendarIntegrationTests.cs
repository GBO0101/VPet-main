using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class GoogleCalendarIntegrationTests
{
    [Fact]
    [Trait("Category", "Calendar")]
    public async Task CalendarSkillRoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var client = new GoogleCalendarClient();

        await client.ConnectAsync(cts.Token);

        var today = await client.ListTodayEventsAsync(cts.Token);
        Assert.False(string.IsNullOrWhiteSpace(today));

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var start = now.AddMinutes(10);
        var end = now.AddMinutes(40);
        var title = "AI Pet 測試事件 " + Guid.NewGuid().ToString("N")[..8];
        var eventIds = new List<string>();

        try
        {
            for (var i = 0; i < 2; i++)
            {
                var addResult = await client.AddCalendarEventAsync(
                    title,
                    FormatIso(start.AddMinutes(i)),
                    FormatIso(end.AddMinutes(i)),
                    "AI Pet Calendar skill integration test",
                    cts.Token);

                Assert.Contains(title, addResult);
                Assert.DoesNotContain("http", addResult, StringComparison.OrdinalIgnoreCase);
                var eventId = ExtractEventId(addResult);
                Assert.False(string.IsNullOrWhiteSpace(eventId));
                eventIds.Add(eventId);
            }

            var searchResult = await client.SearchEventsAsync(title, 1, cts.Token);
            Assert.Contains(title, searchResult);
            foreach (var eventId in eventIds)
                Assert.Contains(eventId, searchResult);

            var deleteResult = await client.DeleteCalendarEventsByKeywordAsync(title, 1, cts.Token);
            Assert.Contains("已刪除", deleteResult);
            eventIds.Clear();
        }
        finally
        {
            foreach (var eventId in eventIds)
                await client.DeleteCalendarEventAsync(eventId, CancellationToken.None);
        }
    }

    private static string FormatIso(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string ExtractEventId(string text)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            const string prefix = "event_id：";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return "";
    }
}
