using System;
using System.Text;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentReminderSpeech
{
    public static string BuildLocalReminderText(string title, string note)
    {
        title = string.IsNullOrWhiteSpace(title) ? "有件小事" : title.Trim();
        note = note?.Trim() ?? "";

        var builder = new StringBuilder();
        builder.Append("主人，時間到啦！").Append(title).Append("。");
        if (!string.IsNullOrWhiteSpace(note))
            builder.AppendLine().Append(note);
        builder.AppendLine().Append("我有乖乖幫你記著喔。");
        return builder.ToString().TrimEnd();
    }

    public static string BuildCalendarReminderText(string summary, double minutes)
    {
        summary = string.IsNullOrWhiteSpace(summary) ? "你的行程" : summary.Trim();
        if (minutes < 1)
            return $"主人，{summary} 現在開始啦。快去吧，我會乖乖等你。";

        return $"主人，{summary} 大約 {Math.Ceiling(minutes)} 分鐘後開始喔。先準備一下，別被我黏住啦。";
    }
}
