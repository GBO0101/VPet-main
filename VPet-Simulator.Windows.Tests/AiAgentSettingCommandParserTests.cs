using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentSettingCommandParserTests
{
    [Fact]
    public void ParsesPomodoroFocusMinutes()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("把番茄鐘專注時間改成 45 分鐘", out var command);

        Assert.True(parsed);
        Assert.Equal(AiAgentSettingCommandType.PomodoroFocusMinutes, command.Type);
        Assert.Equal(45, command.FocusMinutes);
    }

    [Fact]
    public void ParsesPomodoroDurations()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("設定番茄鐘 25/5", out var command);

        Assert.True(parsed);
        Assert.Equal(AiAgentSettingCommandType.PomodoroDurations, command.Type);
        Assert.Equal(25, command.FocusMinutes);
        Assert.Equal(5, command.BreakMinutes);
    }

    [Fact]
    public void ParsesPomodoroDurationsWithImplicitFocusAndBreak()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("番茄鐘設定成 25 分鐘，休息 5 分鐘", out var command);

        Assert.True(parsed);
        Assert.Equal(AiAgentSettingCommandType.PomodoroDurations, command.Type);
        Assert.Equal(25, command.FocusMinutes);
        Assert.Equal(5, command.BreakMinutes);
    }

    [Fact]
    public void ParsesPetName()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("把桌寵名字改成 小雪", out var command);

        Assert.True(parsed);
        Assert.Equal(AiAgentSettingCommandType.PetName, command.Type);
        Assert.Equal("小雪", command.Name);
    }

    [Fact]
    public void ParsesOwnerName()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("以後叫我 阿光", out var command);

        Assert.True(parsed);
        Assert.Equal(AiAgentSettingCommandType.OwnerName, command.Type);
        Assert.Equal("阿光", command.Name);
    }

    [Fact]
    public void DoesNotParsePomodoroStatusQuestionAsSetting()
    {
        var parsed = AiAgentSettingCommandParser.TryParse("番茄鐘還剩多久", out _);

        Assert.False(parsed);
    }
}
