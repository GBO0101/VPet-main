using System;
using System.Collections.Generic;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class EarthquakeAlertService
{
    private static readonly HashSet<string> AlertedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMainWindow mw;

    public EarthquakeAlertService(IMainWindow mw)
    {
        this.mw = mw;
    }

    public bool NotifyIfNew(EarthquakeReportResult report)
    {
        if (report.IsEmpty || string.IsNullOrWhiteSpace(report.Id))
            return false;

        lock (AlertedIds)
        {
            if (!AlertedIds.Add(report.Id))
                return false;
        }

        mw.Dispatcher.Invoke(() =>
        {
            mw.Main.DisplayTouchBody();
            mw.Main.Say("地震快訊！先注意安全，桌寵也有點緊張了。", force: true);

            if (mw is MainWindow mainWindow && mainWindow.notifyIcon != null)
            {
                mainWindow.notifyIcon.BalloonTipTitle = "中央氣象署地震快訊";
                mainWindow.notifyIcon.BalloonTipText = report.Summary.Length > 180 ? report.Summary[..180] + "..." : report.Summary;
                mainWindow.notifyIcon.ShowBalloonTip(8000);
            }
        });

        return true;
    }

    public void MarkSeen(EarthquakeReportResult report)
    {
        if (report.IsEmpty || string.IsNullOrWhiteSpace(report.Id))
            return;

        lock (AlertedIds)
            AlertedIds.Add(report.Id);
    }
}
