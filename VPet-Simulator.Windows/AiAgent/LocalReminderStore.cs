using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class LocalReminderStore
{
    private const string FileName = "AiAgentReminders.json";

    public List<LocalReminderInfo> Load()
    {
        return AiAgentJsonStore.LoadList<LocalReminderInfo>(FileName)
            .OrderBy(reminder => reminder.Time)
            .ToList();
    }

    public void Save(List<LocalReminderInfo> reminders)
    {
        AiAgentJsonStore.SaveList(FileName, reminders.OrderBy(reminder => reminder.Time).ToList());
    }

    public string Create(string title, string timeText, string note)
    {
        if (!TryParseReminderTime(timeText, out var time))
            return "\u7121\u6cd5\u8fa8\u8b58\u63d0\u9192\u6642\u9593\uff0c\u8acb\u7528 yyyy-MM-dd HH:mm \u6216\u300c5\u5206\u9418\u5f8c\u300d\u3002";

        title = string.IsNullOrWhiteSpace(title) ? "\u63d0\u9192" : title.Trim();
        note = note?.Trim() ?? "";

        var reminders = Load();
        reminders.Add(new LocalReminderInfo
        {
            Title = title,
            Note = note,
            Time = time,
            Triggered = false
        });
        Save(reminders);
        return $"\u5df2\u65b0\u589e\u63d0\u9192\uff1a{time:yyyy-MM-dd HH:mm} {title}";
    }

    public string ListText(bool includeTriggered = false)
    {
        var reminders = Load()
            .Where(reminder => includeTriggered || !reminder.Triggered)
            .Take(20)
            .ToList();
        if (reminders.Count == 0)
            return "\u76ee\u524d\u6c92\u6709\u672a\u89f8\u767c\u7684\u672c\u6a5f\u63d0\u9192\u3002";

        var builder = new StringBuilder();
        builder.AppendLine("\u672c\u6a5f\u63d0\u9192\uff1a");
        foreach (var reminder in reminders)
        {
            var status = reminder.Triggered ? "\u5df2\u89f8\u767c" : "\u672a\u89f8\u767c";
            builder.AppendLine($"- {reminder.Time:yyyy-MM-dd HH:mm} {reminder.Title} ({status})");
            if (!string.IsNullOrWhiteSpace(reminder.Note))
                builder.AppendLine("  " + reminder.Note);
        }
        return builder.ToString();
    }

    public void Delete(string id)
    {
        var reminders = Load();
        reminders.RemoveAll(reminder => reminder.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        Save(reminders);
    }

    public List<LocalReminderInfo> MarkDueRemindersTriggered(DateTime now)
    {
        var reminders = Load();
        var due = reminders
            .Where(reminder => !reminder.Triggered && reminder.Time <= now)
            .OrderBy(reminder => reminder.Time)
            .ToList();
        if (due.Count == 0)
            return due;

        foreach (var reminder in due)
            reminder.Triggered = true;
        Save(reminders);
        return due;
    }

    private static bool TryParseReminderTime(string text, out DateTime time)
    {
        time = default;
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var now = DateTime.Now;
        var normalized = text
            .Replace("\u5206\u9418", "m", StringComparison.Ordinal)
            .Replace("\u5206\u949f", "m", StringComparison.Ordinal)
            .Replace("\u5c0f\u6642", "h", StringComparison.Ordinal)
            .Replace("\u5c0f\u65f6", "h", StringComparison.Ordinal)
            .Replace("\u5f8c", "", StringComparison.Ordinal)
            .Replace("\u540e", "", StringComparison.Ordinal)
            .Trim();

        if (normalized.EndsWith("m", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(normalized[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var minutes))
        {
            time = now.AddMinutes(minutes);
            return true;
        }

        if (normalized.EndsWith("h", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(normalized[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var hours))
        {
            time = now.AddHours(hours);
            return true;
        }

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm",
            "yyyy/M/d HH:mm",
            "M/d HH:mm",
            "MM/dd HH:mm",
            "HH:mm"
        };
        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                continue;

            if (format is "HH:mm")
                parsed = now.Date.Add(parsed.TimeOfDay);
            else if (!format.StartsWith("yyyy", StringComparison.Ordinal))
                parsed = new DateTime(now.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0);

            if (parsed < now)
                parsed = parsed.AddDays(1);
            time = parsed;
            return true;
        }

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out time);
    }
}
