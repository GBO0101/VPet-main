using System;
using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class PomodoroSessionTests
{
    [Fact]
    public void UsesDefaultDurationsWhenConfiguredValuesAreInvalid()
    {
        var session = new PomodoroSession(0, -1);

        Assert.Equal(25, session.FocusMinutes);
        Assert.Equal(5, session.BreakMinutes);
    }

    [Fact]
    public void AdvancesFromFocusToBreakThenStartsNextFocus()
    {
        var now = new DateTime(2026, 6, 1, 9, 0, 0);
        var session = new PomodoroSession(25, 5);

        session.Start(now);
        var focusResult = session.Advance(now.AddMinutes(25));
        var breakResult = session.Advance(now.AddMinutes(30));

        Assert.Equal(PomodoroPhaseAdvance.FocusFinished, focusResult);
        Assert.Equal(PomodoroPhaseAdvance.BreakFinished, breakResult);
        Assert.Equal(PomodoroPhase.Focus, session.Phase);
        Assert.Equal(TimeSpan.Zero, session.FocusElapsed);
        Assert.Equal(now.AddMinutes(55), session.EndsAt);
    }

    [Fact]
    public void PauseAndResumeKeepElapsedFocusTime()
    {
        var now = new DateTime(2026, 6, 1, 9, 0, 0);
        var session = new PomodoroSession(25, 5);

        session.Start(now);
        var paused = session.Pause(now.AddMinutes(11));
        var resumed = session.Resume(now.AddMinutes(20));

        Assert.True(paused);
        Assert.True(resumed);
        Assert.Equal(TimeSpan.FromMinutes(11), session.FocusElapsed);
        Assert.Equal(now.AddMinutes(34), session.EndsAt);
    }
}
