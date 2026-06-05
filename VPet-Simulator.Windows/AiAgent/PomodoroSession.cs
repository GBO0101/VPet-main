using System;

namespace VPet_Simulator.Windows.AiAgent;

internal enum PomodoroPhase
{
    Stopped,
    Focus,
    PausedFocus,
    Break
}

internal enum PomodoroPhaseAdvance
{
    None,
    FocusFinished,
    BreakFinished
}

internal sealed class PomodoroSession
{
    public PomodoroSession(int focusMinutes, int breakMinutes)
    {
        FocusMinutes = focusMinutes > 0 ? focusMinutes : 25;
        BreakMinutes = breakMinutes > 0 ? breakMinutes : 5;
    }

    public int FocusMinutes { get; }
    public int BreakMinutes { get; }
    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Stopped;
    public DateTime EndsAt { get; private set; }
    public TimeSpan FocusElapsed { get; private set; }
    private DateTime focusStartedAt;

    public void Start(DateTime now)
    {
        Phase = PomodoroPhase.Focus;
        FocusElapsed = TimeSpan.Zero;
        focusStartedAt = now;
        EndsAt = now.AddMinutes(FocusMinutes);
    }

    public bool Pause(DateTime now)
    {
        if (Phase != PomodoroPhase.Focus)
            return false;

        FocusElapsed += now - focusStartedAt;
        if (FocusElapsed < TimeSpan.Zero)
            FocusElapsed = TimeSpan.Zero;
        var focusDuration = TimeSpan.FromMinutes(FocusMinutes);
        if (FocusElapsed > focusDuration)
            FocusElapsed = focusDuration;

        Phase = PomodoroPhase.PausedFocus;
        return true;
    }

    public bool Resume(DateTime now)
    {
        if (Phase != PomodoroPhase.PausedFocus)
            return false;

        Phase = PomodoroPhase.Focus;
        focusStartedAt = now;
        EndsAt = now + (TimeSpan.FromMinutes(FocusMinutes) - FocusElapsed);
        return true;
    }

    public TimeSpan GetFocusElapsed(DateTime now)
    {
        var elapsed = Phase == PomodoroPhase.Focus
            ? FocusElapsed + (now - focusStartedAt)
            : FocusElapsed;
        if (elapsed < TimeSpan.Zero)
            return TimeSpan.Zero;

        var focusDuration = TimeSpan.FromMinutes(FocusMinutes);
        return elapsed > focusDuration ? focusDuration : elapsed;
    }

    public TimeSpan GetRemaining(DateTime now)
    {
        if (Phase == PomodoroPhase.Focus || Phase == PomodoroPhase.Break)
            return EndsAt > now ? EndsAt - now : TimeSpan.Zero;
        if (Phase == PomodoroPhase.PausedFocus)
            return TimeSpan.FromMinutes(FocusMinutes) - FocusElapsed;
        return TimeSpan.Zero;
    }

    public PomodoroPhaseAdvance Advance(DateTime now)
    {
        if (Phase == PomodoroPhase.Stopped || Phase == PomodoroPhase.PausedFocus || now < EndsAt)
            return PomodoroPhaseAdvance.None;

        if (Phase == PomodoroPhase.Focus)
        {
            FocusElapsed = TimeSpan.FromMinutes(FocusMinutes);
            Phase = PomodoroPhase.Break;
            EndsAt = now.AddMinutes(BreakMinutes);
            return PomodoroPhaseAdvance.FocusFinished;
        }

        FocusElapsed = TimeSpan.Zero;
        focusStartedAt = now;
        Phase = PomodoroPhase.Focus;
        EndsAt = now.AddMinutes(FocusMinutes);
        return PomodoroPhaseAdvance.BreakFinished;
    }

    public void Stop()
    {
        Phase = PomodoroPhase.Stopped;
        FocusElapsed = TimeSpan.Zero;
    }
}
