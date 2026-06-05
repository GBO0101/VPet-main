using System.Linq;
using VPet_Simulator.Windows.AiAgent.Chat;
using Xunit;

namespace VPet_Simulator.Windows.Tests;

public sealed class AiAgentMemorySkillTests
{
    [Fact]
    public void ProjectMentionUpdatesProjectsOrNotes()
    {
        var store = new InMemoryStructuredMemoryStore();
        var skill = new MemorySkill(store);
        var context = AiConversationContext.ForTest("我最近都在做 AI 桌寵");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);

        var update = skill.Update(context, intent, "聽起來你最近都在忙 AI 桌寵。");
        var memory = store.Load();

        Assert.True(update.Updated);
        Assert.True(memory.Projects.Any(project => project.Name.Contains("AI 桌寵"))
            || memory.ConversationNotes.Any(note => note.Text.Contains("AI 桌寵")));
    }

    [Fact]
    public void CasualChatDoesNotRetrieveUnrelatedMemory()
    {
        var store = new InMemoryStructuredMemoryStore();
        var memory = store.Load();
        memory.Projects.Add(new AiMemoryProject { Name = "AI 桌寵", Description = "使用者最近在做的專案" });
        memory.ConversationNotes.Add(new AiMemoryNote { Text = "使用者喜歡短回答" });
        store.Save(memory);

        var skill = new MemorySkill(store);
        var context = AiConversationContext.ForTest("今天有點懶");
        var emotion = new EmotionSkill().Analyze(context);
        var intent = new IntentReasoningSkill().Reason(context, emotion);

        var memories = skill.Retrieve(context, intent);

        Assert.Empty(memories);
    }
}
