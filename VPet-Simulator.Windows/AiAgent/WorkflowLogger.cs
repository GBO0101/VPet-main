using System;
using System.IO;

namespace VPet_Simulator.Windows.AiAgent;

internal static class WorkflowLogger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "workflow_debug.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogAction(string workflowName, int index, string actionDetail)
    {
        Log($"[{workflowName}] 動作#{index}: {actionDetail}");
    }

    public static void LogTrigger(string workflowName, string triggerInfo, bool matched)
    {
        Log($"[Trigger] {(matched ? "✓" : "✗")} {workflowName} ← {triggerInfo}");
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}");
    }
}
