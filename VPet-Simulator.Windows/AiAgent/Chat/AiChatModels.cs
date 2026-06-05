using System;
using System.Collections.Generic;

namespace VPet_Simulator.Windows.AiAgent.Chat;

internal sealed class AiConversationContext
{
    public string UserInput { get; init; } = "";
    public string PetStatus { get; init; } = "";
    public string CalendarSummary { get; init; } = "";
    public string CurrentLocation { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<AiShortTermMemoryTurn> RecentTurns { get; init; } = Array.Empty<AiShortTermMemoryTurn>();

    public static AiConversationContext ForTest(string userInput)
    {
        return new AiConversationContext
        {
            UserInput = userInput,
            CurrentLocation = "",
            Provider = "ollama",
            Model = "test",
            Now = DateTimeOffset.Now
        };
    }
}

internal sealed class AiShortTermMemoryTurn
{
    public string UserInput { get; init; } = "";
    public string AssistantResponse { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public AiPrimaryIntent PrimaryIntent { get; init; } = AiPrimaryIntent.CasualChat;
}

internal enum AiEmotionType
{
    Normal,
    Confused,
    Angry,
    Tired,
    Excited,
    Sad
}

internal sealed class AiEmotionResult
{
    public AiEmotionResult(AiEmotionType type, double confidence, string reason)
    {
        Type = type;
        Confidence = confidence;
        Reason = reason;
    }

    public AiEmotionType Type { get; }
    public double Confidence { get; }
    public string Reason { get; }
}

internal enum AiPrimaryIntent
{
    CasualChat,
    AskQuestion,
    StudyHelp,
    CodingHelp,
    TaskCommand,
    EmotionalSupport,
    Planning,
    Reminder,
    Calendar,
    DesktopAction,
    ProjectHelp
}

internal enum AiHiddenNeed
{
    NeedsEncouragement,
    NeedsExplanation,
    NeedsStepByStep,
    NeedsDirectAnswer,
    NeedsDebugging,
    NeedsSummary,
    NeedsDecisionHelp,
    NeedsCompanionship
}

internal sealed class AiIntentResult
{
    public AiPrimaryIntent PrimaryIntent { get; init; } = AiPrimaryIntent.CasualChat;
    public AiPrimaryIntent? SecondaryIntent { get; init; }
    public List<AiHiddenNeed> HiddenNeeds { get; init; } = new();
    public string HiddenNeed => string.Join(", ", HiddenNeeds);
    public string ResponseGoal { get; init; } = "自然回覆使用者";
    public bool ShouldUseTool { get; init; }
    public List<string> CandidateTools { get; init; } = new();
    public double Confidence { get; init; } = 0.5;
    public bool ShouldUpdateProjectMemory { get; init; }
}

internal sealed class AiToolPlan
{
    public AiToolPlan(bool shouldExecute, OllamaSkillCall toolCall, string reason)
    {
        ShouldExecute = shouldExecute;
        ToolCall = toolCall;
        Reason = reason;
    }

    public static AiToolPlan None { get; } = new(false, OllamaSkillCall.None, "");

    public bool ShouldExecute { get; }
    public OllamaSkillCall ToolCall { get; }
    public string Reason { get; }
}

internal sealed class AiToolExecutionResult
{
    public AiToolExecutionResult(bool executed, string toolName, string result)
    {
        Executed = executed;
        ToolName = toolName;
        Result = result;
    }

    public static AiToolExecutionResult None { get; } = new(false, "", "");

    public bool Executed { get; }
    public string ToolName { get; }
    public string Result { get; }
}

internal enum AiStyleMode
{
    Normal,
    Cute,
    Teacher,
    Coder,
    Comfort,
    Tsukkomi
}

internal sealed class AiStyleResult
{
    public AiStyleResult(AiStyleMode mode, string instruction)
    {
        Mode = mode;
        Instruction = instruction;
    }

    public AiStyleMode Mode { get; }
    public string Instruction { get; }
}

internal enum AiResponseLength
{
    Short,
    Medium,
    Long
}

internal sealed class AiResponsePlan
{
    public string ReplyStrategy { get; init; } = "";
    public string Tone { get; init; } = "";
    public AiResponseLength Length { get; init; } = AiResponseLength.Short;
    public bool ShouldAskFollowUp { get; init; }
    public bool ShouldBePlayful { get; init; }
    public bool MustMentionToolResult { get; init; }
    public string FinalInstruction { get; init; } = "";
}

internal sealed class AiPersonalityProfile
{
    public string Name { get; init; } = "AI 桌寵";
    public string Instruction { get; init; } = "";
}

internal sealed class AiReplyGenerationRequest
{
    public string SystemPrompt { get; init; } = "";
    public string ContextPrompt { get; init; } = "";
    public string UserInput { get; init; } = "";
}

internal sealed class ChatPipelineResult
{
    public string FinalResponse { get; init; } = "";
    public AiConversationContext Context { get; init; } = new();
    public AiEmotionResult Emotion { get; init; } = new(AiEmotionType.Normal, 0, "");
    public AiIntentResult Intent { get; init; } = new();
    public AiToolPlan ToolPlan { get; init; } = AiToolPlan.None;
    public AiToolExecutionResult ToolResult { get; init; } = AiToolExecutionResult.None;
    public AiResponsePlan ResponsePlan { get; init; } = new();
    public AiMemoryUpdateResult MemoryUpdate { get; init; } = new(false, "");
}

internal sealed class AiMemoryItem
{
    public string Type { get; init; } = "";
    public string Text { get; init; } = "";
}

internal sealed class AiMemoryUpdateResult
{
    public AiMemoryUpdateResult(bool updated, string reason)
    {
        Updated = updated;
        Reason = reason;
    }

    public bool Updated { get; }
    public string Reason { get; }
}
