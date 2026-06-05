using VPet_Simulator.Windows.AiAgent.Chat;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentResponseReasoningSkillTests
{
    [Fact]
    public void CodingComplaintUsesEmpathyBeforeFixDirection()
    {
        var context = AiConversationContext.ForTest("我又把程式搞壞了，好煩");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);
        var style = new StyleSkill().Select(context, emotion, intent, AiStructuredMemory.CreateDefault());

        var plan = new ResponseReasoningSkill().Plan(
            context,
            emotion,
            intent,
            System.Array.Empty<AiMemoryItem>(),
            AiToolExecutionResult.None,
            style);

        Assert.Contains("先共感", plan.FinalInstruction);
        Assert.Contains("修正方向", plan.FinalInstruction);
        Assert.Equal(AiResponseLength.Short, plan.Length);
        Assert.False(plan.ShouldBePlayful);
    }

    [Fact]
    public void CalendarToolResultMustBeMentionedNaturally()
    {
        var context = AiConversationContext.ForTest("幫我看今天有沒有行程");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);
        var style = new StyleSkill().Select(context, emotion, intent, AiStructuredMemory.CreateDefault());
        var toolResult = new AiToolExecutionResult(true, "calendar_list_today", "今天 15:00 開會");

        var plan = new ResponseReasoningSkill().Plan(
            context,
            emotion,
            intent,
            System.Array.Empty<AiMemoryItem>(),
            toolResult,
            style);

        Assert.True(plan.MustMentionToolResult);
        Assert.Contains("自然口語", plan.FinalInstruction);
    }
}
