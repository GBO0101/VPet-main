using System;
using System.Linq;
using System.Timers;
using VPet_Simulator.Core;
using VPet_Simulator.Windows;
using VPet_Simulator.Windows.Interface;
using Timer = System.Timers.Timer;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class PomodoroService : IDisposable
{
    public const string DynamicResourceKey = "ai_agent_pomodoro_service";
    private readonly IMainWindow mw;
    private readonly Timer timer;
    private PomodoroSession? session;
    private bool isChecking;

    public PomodoroService(IMainWindow mw)
    {
        this.mw = mw;
        timer = new Timer(TimeSpan.FromSeconds(1).TotalMilliseconds)
        {
            AutoReset = true
        };
        timer.Elapsed += Timer_Elapsed;
    }

    public bool IsRunning => session?.Phase is PomodoroPhase.Focus or PomodoroPhase.Break;
    public bool IsPaused => session?.Phase == PomodoroPhase.PausedFocus;

    public string Start()
    {
        if (IsRunning)
            return "番茄鐘已經在進行中了。";
        if (IsPaused)
            return "番茄鐘目前已暫停，請使用「繼續番茄鐘」。";

        session = new PomodoroSession(mw.Set.PomodoroFocusMinutes, mw.Set.PomodoroBreakMinutes);
        session.Start(DateTime.Now);
        timer.Start();

        if (!mw.Dispatcher.Invoke(StartWritingWork))
        {
            timer.Stop();
            session.Stop();
            return "現在還不能開始寫作工作。";
        }

        return $"番茄鐘開始：專注 {session.FocusMinutes} 分鐘，休息 {session.BreakMinutes} 分鐘。";
    }

    public string Stop()
    {
        if (!IsRunning && !IsPaused)
            return "目前沒有正在進行的番茄鐘。";

        timer.Stop();
        session?.Stop();
        mw.Dispatcher.Invoke(StopWritingWork);
        return "番茄鐘已停止。";
    }

    public string Pause()
    {
        if (session == null || !session.Pause(DateTime.Now))
            return "目前沒有可暫停的專注時間。";

        timer.Stop();
        mw.Dispatcher.Invoke(StopWritingWork);
        return $"番茄鐘已暫停：已專注 {FormatMinutes(session.FocusElapsed)} 分鐘。";
    }

    public string Resume()
    {
        if (session == null || !IsPaused)
            return "目前沒有暫停中的番茄鐘。";

        if (!mw.Dispatcher.Invoke(StartWritingWork))
            return "現在還不能繼續寫作工作。";

        session.Resume(DateTime.Now);
        timer.Start();
        return $"番茄鐘繼續：已專注 {FormatMinutes(session.FocusElapsed)} 分鐘，剩餘 {FormatMinutes(session.GetRemaining(DateTime.Now))} 分鐘。";
    }

    public string BuildStatusText()
    {
        if (session == null || session.Phase == PomodoroPhase.Stopped)
            return $"未開始。預設 {mw.Set.PomodoroFocusMinutes}/{mw.Set.PomodoroBreakMinutes} 分鐘。";

        var now = DateTime.Now;
        return session.Phase switch
        {
            PomodoroPhase.Focus => $"專注中：已專注 {FormatMinutes(session.GetFocusElapsed(now))}/{session.FocusMinutes} 分鐘，剩餘 {FormatMinutes(session.GetRemaining(now))} 分鐘。",
            PomodoroPhase.PausedFocus => $"已暫停：已專注 {FormatMinutes(session.FocusElapsed)}/{session.FocusMinutes} 分鐘，剩餘 {FormatMinutes(session.GetRemaining(now))} 分鐘。",
            PomodoroPhase.Break => $"休息中：剩餘 {FormatMinutes(session.GetRemaining(now))}/{session.BreakMinutes} 分鐘。",
            _ => "未開始。"
        };
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Elapsed -= Timer_Elapsed;
        timer.Dispose();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (isChecking || session == null)
            return;

        isChecking = true;
        try
        {
            switch (session.Advance(DateTime.Now))
            {
                case PomodoroPhaseAdvance.FocusFinished:
                    HandleFocusFinished();
                    break;
                case PomodoroPhaseAdvance.BreakFinished:
                    if (!HandleBreakFinished())
                    {
                        timer.Stop();
                        session?.Stop();
                    }
                    break;
            }
        }
        finally
        {
            isChecking = false;
        }
    }

    private bool StartWritingWork()
    {
        if (mw.Main.State == Main.WorkingState.Work && mw.Main.NowWork != null)
            return true;

        mw.Main.WorkList(out var works, out _, out _);
        var writingWork = works.FirstOrDefault(work => work.Name.Equals("文案", StringComparison.OrdinalIgnoreCase))
            ?? works.FirstOrDefault();

        if (writingWork == null)
            return false;

        return mw.Main.StartWork(writingWork);
    }

    private void StopWritingWork()
    {
        if (mw.Main.State == Main.WorkingState.Work && mw.Main.NowWork != null)
            mw.Main.WorkTimer.Stop();
    }

    private void HandleFocusFinished()
    {
        mw.Dispatcher.Invoke(() =>
        {
            ShowWindowsNotification("番茄鐘", "專注時間到了，休息一下吧。");
            if (mw.Main.State == Main.WorkingState.Work && mw.Main.NowWork != null)
                mw.Main.WorkTimer.Stop(() => mw.Main.SayRnd("專注時間到了，休息一下吧。", true, "番茄鐘"));
            else
                mw.Main.SayRnd("專注時間到了，休息一下吧。", true, "番茄鐘");
        });
    }

    private bool HandleBreakFinished()
    {
        return mw.Dispatcher.Invoke(() =>
        {
            ShowWindowsNotification("番茄鐘", "休息時間到了，可以回來專注了。");
            if (StartWritingWork())
            {
                mw.Main.SayRnd("休息時間到了，可以回來專注了。", true, "番茄鐘");
                return true;
            }

            mw.Main.SayRnd("休息時間到了，但現在還不能開始寫作工作。番茄鐘已停止。", true, "番茄鐘");
            return false;
        });
    }

    private void ShowWindowsNotification(string title, string text)
    {
        if (mw is not MainWindow mainWindow || mainWindow.notifyIcon == null)
            return;

        mainWindow.notifyIcon.BalloonTipTitle = title;
        mainWindow.notifyIcon.BalloonTipText = text;
        mainWindow.notifyIcon.ShowBalloonTip(5000);
    }

    private static string FormatMinutes(TimeSpan time)
    {
        return Math.Max(0, time.TotalMinutes).ToString("0.#");
    }
}
