using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.AiAgent.Chat;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentSkillExecutor : IAiAgentToolExecutor
{
    private readonly IMainWindow mw;
    private readonly CalendarReminderService reminderService;
    private readonly PomodoroService pomodoroService;
    private readonly GoogleCalendarClient calendarClient;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly WeatherSkillClient weatherClient = new();
    private readonly EarthquakeSkillClient earthquakeClient = new();
    private readonly EarthquakeAlertService earthquakeAlertService;
    private readonly AiAgentMemoryStore memoryStore = new();
    private readonly LocalReminderStore localReminderStore = new();
    private readonly ProgramShortcutStore programShortcutStore = new();
    private readonly FileSearchSkill fileSearchSkill = new();

    public AiAgentSkillExecutor(IMainWindow mw, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder, PomodoroService pomodoroService)
    {
        this.mw = mw;
        this.reminderService = reminderService;
        this.pomodoroService = pomodoroService;
        calendarClient = reminderService.CalendarClient;
        this.petStatusBuilder = petStatusBuilder;
        earthquakeAlertService = new EarthquakeAlertService(mw);
    }

    public async Task<string> ExecuteAsync(OllamaSkillCall skillCall, string userText, CancellationToken cancellationToken)
    {
        if (!skillCall.HasSkill)
            return "";

        var result = skillCall.SkillName.ToLowerInvariant() switch
        {
            "get_pet_status" => petStatusBuilder.BuildStatusSummary(),
            "get_calendar_events" => await reminderService.BuildCalendarSummaryAsync(cancellationToken),
            "calendar_list_today" => await calendarClient.ListTodayEventsAsync(cancellationToken),
            "calendar_list_by_date" => await calendarClient.ListEventsByDateAsync(skillCall.Date, cancellationToken),
            "calendar_add_event" => await calendarClient.AddCalendarEventAsync(
                string.IsNullOrWhiteSpace(skillCall.Title) ? userText : skillCall.Title,
                skillCall.StartDatetime,
                skillCall.EndDatetime,
                string.IsNullOrWhiteSpace(skillCall.Description) ? skillCall.Note : skillCall.Description,
                cancellationToken),
            "calendar_search_events" => await calendarClient.SearchEventsAsync(GetCalendarKeyword(skillCall, userText), skillCall.DaysAhead, cancellationToken),
            "calendar_delete_event" => await DeleteCalendarEventAsync(skillCall, cancellationToken),
            "get_weather" => await weatherClient.GetWeatherAsync(
                string.IsNullOrWhiteSpace(skillCall.Location) ? userText : skillCall.Location,
                string.IsNullOrWhiteSpace(skillCall.Query) ? userText : skillCall.Query,
                cancellationToken),
            "get_earthquake_report" => await GetEarthquakeReportAsync(cancellationToken),
            "remember_user_fact" => memoryStore.Remember(string.IsNullOrWhiteSpace(skillCall.Fact) ? userText : skillCall.Fact),
            "recall_memory" => memoryStore.Recall(),
            "create_reminder" => localReminderStore.Create(
                string.IsNullOrWhiteSpace(skillCall.Title) ? userText : skillCall.Title,
                skillCall.Time,
                skillCall.Note),
            "list_reminders" => localReminderStore.ListText(),
            "open_program" => programShortcutStore.Open(string.IsNullOrWhiteSpace(skillCall.Target) ? userText : skillCall.Target),
            "search_files" => await fileSearchSkill.SearchAsync(string.IsNullOrWhiteSpace(skillCall.Query) ? userText : skillCall.Query, cancellationToken),
            "start_pomodoro" => pomodoroService.Start(),
            "get_pomodoro_status" => pomodoroService.BuildStatusText(),
            "pause_pomodoro" => pomodoroService.Pause(),
            "resume_pomodoro" => pomodoroService.Resume(),
            "stop_pomodoro" => pomodoroService.Stop(),
            "sleep" => RunPetCommand("\u53bb\u7761\u89ba"),
            "wake_up" => RunPetCommand("\u9192\u4f86"),
            "stop_activity" => RunPetCommand("\u505c\u6b62\u5de5\u4f5c"),
            "touch_head" => RunPetCommand("\u6478\u982d"),
            "touch_body" => RunPetCommand("\u6478\u8eab\u9ad4"),
            "start_work" => RunPetCommand("\u958b\u59cb\u5de5\u4f5c"),
            "start_study" => RunPetCommand("\u958b\u59cb\u5b78\u7fd2"),
            "start_play" => RunPetCommand("\u53bb\u73a9"),
            "open_food_shop" => RunPetCommand("\u8cb7\u98df\u7269"),
            "open_drink_shop" => RunPetCommand("\u8cb7\u98f2\u6599"),
            _ => "\u672a\u77e5\u7684 skill\uff1a" + skillCall.SkillName
        };

        return string.IsNullOrWhiteSpace(result)
            ? ""
            : $"Skill `{skillCall.SkillName}` \u57f7\u884c\u7d50\u679c\uff1a\n{result}";
    }

    private async Task<string> GetEarthquakeReportAsync(CancellationToken cancellationToken)
    {
        var report = await earthquakeClient.GetLatestReportAsync(cancellationToken);
        earthquakeAlertService.NotifyIfNew(report);

        return report.Summary;
    }

    private async Task<string> DeleteCalendarEventAsync(OllamaSkillCall skillCall, CancellationToken cancellationToken)
    {
        if (skillCall.DeleteAll)
        {
            if (!string.IsNullOrWhiteSpace(skillCall.Date))
                return await calendarClient.DeleteCalendarEventsByDateAsync(skillCall.Date, cancellationToken);

            var deleteKeyword = GetCalendarKeyword(skillCall, "");
            return string.IsNullOrWhiteSpace(deleteKeyword)
                ? "請告訴我要刪除哪一天，或要刪除哪個關鍵字的所有行程。"
                : await calendarClient.DeleteCalendarEventsByKeywordAsync(deleteKeyword, skillCall.DaysAhead, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(skillCall.EventId))
            return await calendarClient.DeleteCalendarEventAsync(skillCall.EventId, cancellationToken);

        var keyword = GetCalendarKeyword(skillCall, "");
        if (string.IsNullOrWhiteSpace(keyword))
            return "請先提供 event_id，或提供要刪除事件的明確關鍵字。";

        var matches = await calendarClient.SearchEventItemsAsync(keyword, skillCall.DaysAhead, cancellationToken);
        if (matches.Count == 0)
            return $"找不到包含「{keyword}」的未來行程，沒有刪除任何事件。";
        if (matches.Count > 1)
        {
            var builder = new StringBuilder();
            builder.AppendLine("找到多個候選事件，請指定要刪除的 event_id：");
            foreach (var calendarEvent in matches)
                builder.AppendLine($"- event_id: {calendarEvent.Id} | {FormatEventTime(calendarEvent)} | {calendarEvent.Summary}");
            return builder.ToString().TrimEnd();
        }

        return await calendarClient.DeleteCalendarEventAsync(matches[0].Id, cancellationToken);
    }

    private static string GetCalendarKeyword(OllamaSkillCall skillCall, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(skillCall.Keyword))
            return skillCall.Keyword;
        if (!string.IsNullOrWhiteSpace(skillCall.Query))
            return skillCall.Query;
        if (!string.IsNullOrWhiteSpace(skillCall.Title))
            return skillCall.Title;
        return fallback;
    }

    private static string FormatEventTime(CalendarEventInfo calendarEvent)
    {
        if (calendarEvent.IsAllDay)
            return "全天";

        return calendarEvent.Start.Date == calendarEvent.End.Date
            ? $"{calendarEvent.Start:MM/dd HH:mm}-{calendarEvent.End:HH:mm}"
            : $"{calendarEvent.Start:MM/dd HH:mm}-{calendarEvent.End:MM/dd HH:mm}";
    }

    private string RunPetCommand(string command)
    {
        return AiAgentCommandRouter.TryHandle(mw, command, out var response, pomodoroService)
            ? response
            : "\u9019\u500b skill \u76ee\u524d\u7121\u6cd5\u57f7\u884c\u3002";
    }
}
