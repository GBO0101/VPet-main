using System;
using System.Collections.Generic;
using System.Linq;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentDiyLinkMatcher
{
    public static bool TryFindTarget(string text, IEnumerable<(string Name, string Content)> links, out string name, out string content)
    {
        name = "";
        content = "";

        var input = Normalize(text);
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var candidates = links
            .Where(link => !string.IsNullOrWhiteSpace(link.Name) && !string.IsNullOrWhiteSpace(link.Content))
            .OrderByDescending(link => Normalize(link.Name).Length)
            .ToList();

        foreach (var link in candidates)
        {
            var aliases = GetAliases(link.Name, link.Content);
            if (aliases.Any(alias => input.Equals(alias, StringComparison.OrdinalIgnoreCase))
                || HasOpenIntent(input) && aliases.Any(alias => input.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                name = link.Name.Trim();
                content = link.Content.Trim();
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetAliases(string name, string content)
    {
        var aliases = new List<string> { Normalize(name) };
        if (Uri.TryCreate(content, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            aliases.Add(Normalize(host));
            aliases.Add(Normalize(host.Split('.')[0]));
        }

        return aliases.Where(alias => alias.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasOpenIntent(string input)
    {
        return new[]
        {
            "打開",
            "打开",
            "開啟",
            "开启",
            "啟動",
            "启动",
            "開",
            "开",
            "去",
            "看",
            "播放",
            "open",
            "launch",
            "start",
            "goto",
            "watch"
        }.Any(input.Contains);
    }

    private static string Normalize(string value)
    {
        return new string((value ?? "")
            .Where(ch => !char.IsWhiteSpace(ch))
            .ToArray())
            .Trim()
            .ToLowerInvariant();
    }
}
