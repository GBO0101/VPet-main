using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using VPet_Simulator.Windows.Interface;
using Timer = System.Timers.Timer;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class CalendarReminderService : IDisposable
{
    private readonly IMainWindow mw;
    private readonly GoogleCalendarClient calendarClient;
    private readonly Timer timer;
    private readonly HashSet<string> remindedEvents = [];
    private bool isChecking;

    public CalendarReminderService(IMainWindow mw, GoogleCalendarClient calendarClient)
    {
        this.mw = mw;
        this.calendarClient = calendarClient;
        timer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds)
        {
            AutoReset = true
        };
        timer.Elapsed += Timer_Elapsed;
    }

    public GoogleCalendarClient CalendarClient => calendarClient;

    public void Start()
    {
        timer.Start();
        _ = CheckAsync();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Elapsed -= Timer_Elapsed;
        timer.Dispose();
    }

    public async Task<string> BuildCalendarSummaryAsync(CancellationToken cancellationToken)
    {
        if (!calendarClient.IsConfigured)
            return "";

        var events = await calendarClient.GetUpcomingEventsAsync(TimeSpan.FromDays(1), cancellationToken);
        if (events.Count == 0)
            return "\u672a\u4f86 24 \u5c0f\u6642\u6c92\u6709\u884c\u7a0b\u3002";

        var builder = new StringBuilder();
        foreach (var calendarEvent in events.Take(10))
        {
            var timeText = calendarEvent.IsAllDay
                ? calendarEvent.Start.ToString("MM/dd")
                : calendarEvent.Start.ToString("MM/dd HH:mm");
            builder.AppendLine($"{timeText} {calendarEvent.Summary}");
        }
        return builder.ToString();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (isChecking || !calendarClient.IsConfigured)
            return;

        isChecking = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var events = await calendarClient.GetUpcomingEventsAsync(TimeSpan.FromHours(12), cts.Token);
            var now = DateTime.Now;
            foreach (var calendarEvent in events)
            {
                if (calendarEvent.IsAllDay)
                    continue;

                var minutes = (calendarEvent.Start - now).TotalMinutes;
                if (minutes is < 0 or > 10)
                    continue;

                var key = $"{calendarEvent.Id}:{calendarEvent.Start:o}";
                if (!remindedEvents.Add(key))
                    continue;

                var text = AiAgentReminderSpeech.BuildCalendarReminderText(calendarEvent.Summary, minutes);

                mw.Dispatcher.Invoke(() => mw.Main.SayRnd(text, true, "Google Calendar"));
            }
        }
        catch
        {
            // Calendar reminders must not interrupt the pet main flow.
        }
        finally
        {
            isChecking = false;
        }
    }
}
