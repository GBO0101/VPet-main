using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class FileSearchSkill
{
    public Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return Task.Run(() => Search(query, cancellationToken), cancellationToken);
    }

    private static string Search(string query, CancellationToken cancellationToken)
    {
        query = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(query))
            return "\u8acb\u544a\u8a34\u6211\u8981\u641c\u5c0b\u7684\u6a94\u540d\u3002";

        var results = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var searchedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in GetPrioritySearchRoots())
        {
            if (timeoutCts.IsCancellationRequested || results.Count >= 30)
                break;

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            var fullRoot = Path.GetFullPath(root);
            if (!searchedRoots.Add(fullRoot))
                continue;

            SearchDirectory(fullRoot, query, results, timeoutCts.Token, stopwatch);
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (timeoutCts.IsCancellationRequested || results.Count >= 30)
                break;

            try
            {
                if (!drive.IsReady)
                    continue;

                var fullRoot = Path.GetFullPath(drive.RootDirectory.FullName);
                if (searchedRoots.Add(fullRoot))
                    SearchDirectory(fullRoot, query, results, timeoutCts.Token, stopwatch);
            }
            catch
            {
                // Ignore inaccessible drives.
            }
        }

        if (results.Count == 0)
            return "\u627e\u4e0d\u5230\u6a94\u6848\uff1a" + query;

        var builder = new StringBuilder();
        builder.AppendLine($"\u627e\u5230 {results.Count} \u7b46\u6a94\u6848\uff08\u6700\u591a\u986f\u793a 30 \u7b46\uff09\uff1a");
        foreach (var result in results.Take(30))
            builder.AppendLine("- " + result);
        if (stopwatch.Elapsed >= TimeSpan.FromSeconds(15))
            builder.AppendLine("\u641c\u5c0b\u5df2\u9054 15 \u79d2\u4e0a\u9650\uff0c\u5df2\u505c\u6b62\u3002");
        return builder.ToString();
    }

    private static IEnumerable<string> GetPrioritySearchRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(userProfile, "Downloads");
        yield return Path.Combine(userProfile, "OneDrive");
        yield return Path.Combine(userProfile, "OneDrive", "Desktop");
        yield return Path.Combine(userProfile, "OneDrive", "Documents");
    }

    private static void SearchDirectory(string root, string query, List<string> results, CancellationToken cancellationToken, Stopwatch stopwatch)
    {
        if (cancellationToken.IsCancellationRequested || results.Count >= 30 || stopwatch.Elapsed >= TimeSpan.FromSeconds(15))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested || results.Count >= 30 || stopwatch.Elapsed >= TimeSpan.FromSeconds(15))
                return;

            try
            {
                if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(file);
            }
            catch
            {
                // Ignore files that cannot be inspected.
            }
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch
        {
            return;
        }

        foreach (var directory in directories)
        {
            SearchDirectory(directory, query, results, cancellationToken, stopwatch);
            if (cancellationToken.IsCancellationRequested || results.Count >= 30 || stopwatch.Elapsed >= TimeSpan.FromSeconds(15))
                return;
        }
    }

    private static string NormalizeQuery(string query)
    {
        return (query ?? "")
            .Trim()
            .Replace("\u5e6b\u6211\u627e", "", StringComparison.Ordinal)
            .Replace("\u627e\u4e00\u4e0b", "", StringComparison.Ordinal)
            .Replace("\u641c\u5c0b", "", StringComparison.Ordinal)
            .Replace("\u6a94\u6848", "", StringComparison.Ordinal)
            .Trim();
    }
}
