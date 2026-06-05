using System.Linq;
using VPet_Simulator.Windows.AiAgent;
using VPet_Simulator.Windows.AiAgent.Chat;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentIntentReasoningSkillTests
{
    [Fact]
    public void ComplaintAboutBrokenCodeInfersCodingHelpAndSupport()
    {
        var context = AiConversationContext.ForTest("我又把程式搞壞了，好煩");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);

        Assert.Equal(AiEmotionType.Angry, emotion.Type);
        Assert.Equal(AiPrimaryIntent.CodingHelp, intent.PrimaryIntent);
        Assert.Equal(AiPrimaryIntent.EmotionalSupport, intent.SecondaryIntent);
        Assert.Contains(AiHiddenNeed.NeedsDebugging, intent.HiddenNeeds);
        Assert.Contains(AiHiddenNeed.NeedsEncouragement, intent.HiddenNeeds);
        Assert.False(intent.ShouldUseTool);
    }

    [Fact]
    public void CalendarQuestionPlansCalendarTool()
    {
        var context = AiConversationContext.ForTest("幫我看今天有沒有行程");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);
        var plan = new ToolSkill(new FakeToolExecutor()).Plan(context, intent);

        Assert.Equal(AiPrimaryIntent.Calendar, intent.PrimaryIntent);
        Assert.True(intent.ShouldUseTool);
        Assert.Contains("calendar_list_today", intent.CandidateTools);
        Assert.True(plan.ShouldExecute);
        Assert.Equal("calendar_list_today", plan.ToolCall.SkillName);
    }

    [Fact]
    public void CasualLowEnergyChatDoesNotUseTool()
    {
        var context = AiConversationContext.ForTest("今天有點懶");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);
        var plan = new ToolSkill(new FakeToolExecutor()).Plan(context, intent);

        Assert.Equal(AiEmotionType.Tired, emotion.Type);
        Assert.Equal(AiPrimaryIntent.CasualChat, intent.PrimaryIntent);
        Assert.Contains(AiHiddenNeed.NeedsCompanionship, intent.HiddenNeeds);
        Assert.False(plan.ShouldExecute);
    }

    private sealed class FakeToolExecutor : IAiAgentToolExecutor
    {
        public System.Threading.Tasks.Task<string> ExecuteAsync(OllamaSkillCall skillCall, string userText, System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.FromResult("ok");
        }
    }
}
