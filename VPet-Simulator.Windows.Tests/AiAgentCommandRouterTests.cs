using VPet_Simulator.Windows.AiAgent;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentCommandRouterTests
{
    [Theory]
    [InlineData("還要休息多久")]
    [InlineData("現在還剩多久要專注")]
    public void RoutesPomodoroRemainingQuestionWithoutPomodoroKeyword(string text)
    {
        var handled = AiAgentCommandRouter.TryHandle(null!, text, out var response);

        Assert.True(handled);
        Assert.Equal("番茄鐘服務尚未啟動。", response);
    }
}
