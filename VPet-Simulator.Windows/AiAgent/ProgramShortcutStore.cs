using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class ProgramShortcutStore
{
    private const string FileName = "AiAgentProgramShortcuts.json";
    private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".lnk",
        ".bat",
        ".cmd",
        ".ps1"
    };

    public List<ProgramShortcutInfo> Load()
    {
        return AiAgentJsonStore.LoadList<ProgramShortcutInfo>(FileName)
            .Where(shortcut => !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Path))
            .OrderBy(shortcut => shortcut.Name)
            .ToList();
    }

    public void Save(List<ProgramShortcutInfo> shortcuts)
    {
        AiAgentJsonStore.SaveList(FileName, shortcuts
            .Where(shortcut => !string.IsNullOrWhiteSpace(shortcut.Name) && !string.IsNullOrWhiteSpace(shortcut.Path))
            .OrderBy(shortcut => shortcut.Name)
            .ToList());
    }

    public string AddOrUpdate(string name, string path)
    {
        name = (name ?? "").Trim();
        path = (path ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return "\u8acb\u8f38\u5165\u7a0b\u5f0f\u540d\u7a31\u548c\u8def\u5f91\u3002";

        var shortcuts = Load();
        var existing = shortcuts.FirstOrDefault(shortcut => shortcut.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            shortcuts.Add(new ProgramShortcutInfo { Name = name, Path = path });
        }
        else
        {
            existing.Path = path;
        }
        Save(shortcuts);
        return "\u5df2\u5132\u5b58\u7a0b\u5f0f\u6377\u5f91\uff1a" + name;
    }

    public static List<ProgramShortcutInfo> FindLaunchableFiles(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return new List<ProgramShortcutInfo>();

        return EnumerateFiles(folderPath)
            .Where(path => LaunchableExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new ProgramShortcutInfo
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path
            })
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateFiles(string folderPath)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folderPath);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(folderPath);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            foreach (var file in EnumerateFiles(directory))
                yield return file;
        }
    }

    public void Delete(string name)
    {
        var shortcuts = Load();
        shortcuts.RemoveAll(shortcut => shortcut.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Save(shortcuts);
    }

    public string Open(string target)
    {
        target = (target ?? "").Trim();
        if (string.IsNullOrWhiteSpace(target))
            return "\u8acb\u544a\u8a34\u6211\u8981\u6253\u958b\u54ea\u500b\u767d\u540d\u55ae\u7a0b\u5f0f\u3002";

        var shortcuts = Load();
        var shortcut = shortcuts.FirstOrDefault(item => item.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
            ?? shortcuts.FirstOrDefault(item => item.Name.Contains(target, StringComparison.OrdinalIgnoreCase) || target.Contains(item.Name, StringComparison.OrdinalIgnoreCase));
        if (shortcut == null)
            return "\u627e\u4e0d\u5230\u767d\u540d\u55ae\u7a0b\u5f0f\uff1a" + target;

        if (!File.Exists(shortcut.Path) && !Directory.Exists(shortcut.Path))
            return "\u767d\u540d\u55ae\u8def\u5f91\u4e0d\u5b58\u5728\uff1a" + shortcut.Path;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = shortcut.Path,
                UseShellExecute = true
            });
            return "\u5df2\u958b\u555f\uff1a" + shortcut.Name;
        }
        catch (Exception ex)
        {
            return "\u958b\u555f\u5931\u6557\uff1a" + ex.Message;
        }
    }

    public string ListText()
    {
        var shortcuts = Load();
        if (shortcuts.Count == 0)
            return "\u5c1a\u672a\u8a2d\u5b9a\u7a0b\u5f0f\u767d\u540d\u55ae\u3002";

        var builder = new StringBuilder();
        builder.AppendLine("\u7a0b\u5f0f\u767d\u540d\u55ae\uff1a");
        foreach (var shortcut in shortcuts)
            builder.AppendLine("- " + shortcut.Name + " -> " + shortcut.Path);
        return builder.ToString();
    }
}
