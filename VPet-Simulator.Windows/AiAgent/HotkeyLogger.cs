using System.IO;

namespace VPet_Simulator.Windows.AiAgent;

internal static class HotkeyLogger
{
    private static readonly string LogPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".", "hotkey_debug.log");
    private static readonly object Lock = new();

    static HotkeyLogger()
    {
        try { File.AppendAllText(LogPath, $"[HotkeyLogger] === 啟動 ===\n"); }
        catch { }
    }

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"[{System.DateTime.Now:HH:mm:ss}] {message}\n");
            }
        }
        catch { }
    }
}
