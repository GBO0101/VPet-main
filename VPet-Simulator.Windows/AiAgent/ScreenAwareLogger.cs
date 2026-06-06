using System;
using System.IO;

namespace VPet_Simulator.Windows.AiAgent;

internal static class ScreenAwareLogger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "screen_aware.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogError(string context, Exception ex)
    {
        Log($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}");
    }
}
