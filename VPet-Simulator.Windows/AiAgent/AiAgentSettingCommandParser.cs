using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VPet_Simulator.Windows.AiAgent;

internal enum AiAgentSettingCommandType
{
    None,
    PomodoroFocusMinutes,
    PomodoroBreakMinutes,
    PomodoroDurations,
    PetName,
    OwnerName
}

internal readonly struct AiAgentSettingCommand
{
    public AiAgentSettingCommand(AiAgentSettingCommandType type, int focusMinutes = 0, int breakMinutes = 0, string name = "")
    {
        Type = type;
        FocusMinutes = focusMinutes;
        BreakMinutes = breakMinutes;
        Name = name ?? "";
    }

    public AiAgentSettingCommandType Type { get; }
    public int FocusMinutes { get; }
    public int BreakMinutes { get; }
    public string Name { get; }
}

internal static class AiAgentSettingCommandParser
{
    private static readonly Regex PairDurationRegex = new(@"(?<focus>\d{1,3})\s*(?:/|：|:|,|，)\s*(?<break>\d{1,3})", RegexOptions.Compiled);
    private static readonly Regex DurationRegex = new(@"(?<value>\d{1,3})\s*(?<unit>小時|小时|hours?|分鐘|分钟|mins?|minutes?)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string text, out AiAgentSettingCommand command)
    {
        command = default;
        var input = Normalize(text);
        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (TryParseOwnerName(input, out command)
            || TryParsePetName(input, out command)
            || TryParsePomodoro(input, out command))
            return true;

        return false;
    }

    private static bool TryParsePomodoro(string text, out AiAgentSettingCommand command)
    {
        command = default;
        if (!ContainsAny(text, "番茄鐘", "番茄钟", "pomodoro"))
            return false;

        var pair = PairDurationRegex.Match(text);
        if (pair.Success
            && int.TryParse(pair.Groups["focus"].Value, out var pairFocus)
            && int.TryParse(pair.Groups["break"].Value, out var pairBreak)
            && (HasSettingIntent(text) || text.Contains("番茄鐘") || text.Contains("番茄钟")))
        {
            command = new AiAgentSettingCommand(AiAgentSettingCommandType.PomodoroDurations, pairFocus, pairBreak);
            return true;
        }

        if (!HasSettingIntent(text))
            return false;

        var durations = ExtractDurations(text);
        if (durations.Count == 0)
            return false;

        var hasFocus = ContainsAny(text, "專注", "专注", "工作", "focus");
        var hasBreak = ContainsAny(text, "休息", "break");
        if (hasBreak && durations.Count >= 2)
        {
            command = new AiAgentSettingCommand(AiAgentSettingCommandType.PomodoroDurations, durations[0], durations[1]);
            return true;
        }

        if (hasBreak)
        {
            command = new AiAgentSettingCommand(AiAgentSettingCommandType.PomodoroBreakMinutes, breakMinutes: durations[0]);
            return true;
        }

        command = new AiAgentSettingCommand(AiAgentSettingCommandType.PomodoroFocusMinutes, focusMinutes: durations[0]);
        return true;
    }

    private static bool TryParsePetName(string text, out AiAgentSettingCommand command)
    {
        return TryParseName(text, new[]
        {
            @"(?:把)?(?:桌寵|桌宠|寵物|宠物)(?:的)?(?:名稱|名称|名字|名子|name)\s*(?:改成|改為|改为|設定成|设置成|設成|设成|設為|设为|叫做|叫|是|=|：|:)\s*(?<name>.+)$",
            @"(?:把)?你(?:的)?(?:名稱|名称|名字|名子|name)\s*(?:改成|改為|改为|設定成|设置成|設成|设成|設為|设为|叫做|叫|是|=|：|:)\s*(?<name>.+)$",
            @"(?:以後|以后)?\s*叫你\s*(?<name>.+)$"
        }, AiAgentSettingCommandType.PetName, out command);
    }

    private static bool TryParseOwnerName(string text, out AiAgentSettingCommand command)
    {
        return TryParseName(text, new[]
        {
            @"(?:以後|以后)?\s*叫我\s*(?<name>.+)$",
            @"(?:把)?(?:主人|我的|我)(?:的)?(?:名稱|名称|名字|名子|稱呼|称呼|name)?\s*(?:改成|改為|改为|設定成|设置成|設成|设成|設為|设为|叫做|叫|是|=|：|:)\s*(?<name>.+)$"
        }, AiAgentSettingCommandType.OwnerName, out command);
    }

    private static bool TryParseName(string text, IEnumerable<string> patterns, AiAgentSettingCommandType type, out AiAgentSettingCommand command)
    {
        command = default;
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            var name = CleanName(match.Groups["name"].Value);
            if (!IsUsableName(name))
                continue;

            command = new AiAgentSettingCommand(type, name: name);
            return true;
        }

        return false;
    }

    private static List<int> ExtractDurations(string text)
    {
        return DurationRegex.Matches(text)
            .Cast<Match>()
            .Where(match => match.Success && int.TryParse(match.Groups["value"].Value, out _))
            .Select(match =>
            {
                var value = int.Parse(match.Groups["value"].Value);
                var unit = match.Groups["unit"].Value;
                return unit is "小時" or "小时" || unit.StartsWith("hour", StringComparison.OrdinalIgnoreCase)
                    ? value * 60
                    : value;
            })
            .ToList();
    }

    private static bool HasSettingIntent(string text)
    {
        return ContainsAny(text,
            "設定", "设置", "設成", "设成", "設為", "设为",
            "改成", "改為", "改为", "更改", "調成", "调成", "調整", "调整",
            "變成", "变成", "set", "change");
    }

    private static string CleanName(string name)
    {
        var cleaned = (name ?? "").Trim()
            .Trim('「', '」', '『', '』', '"', '\'', '`', ' ');
        return cleaned.TrimEnd('。', '.', '！', '!', '？', '?', '，', ',', '；', ';');
    }

    private static bool IsUsableName(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.Length <= 30
            && !ContainsAny(name, "什麼", "什么", "誰", "谁", "嗎", "吗", "多久", "多少");
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        return (value ?? "").Replace('　', ' ').Trim();
    }
}
