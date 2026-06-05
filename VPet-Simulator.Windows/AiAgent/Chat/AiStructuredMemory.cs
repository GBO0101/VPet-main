using System;
using System.Collections.Generic;

namespace VPet_Simulator.Windows.AiAgent.Chat;

internal sealed class AiStructuredMemory
{
    public int Version { get; set; } = 1;
    public AiMemoryProfile Profile { get; set; } = new();
    public List<AiMemoryPreference> Preferences { get; set; } = new();
    public List<AiMemoryProject> Projects { get; set; } = new();
    public List<AiMemoryNote> ConversationNotes { get; set; } = new();
    public AiRelationshipState RelationshipState { get; set; } = new();
    public List<AiEmotionSnapshot> EmotionHistory { get; set; } = new();
    public AiInteractionStats InteractionStats { get; set; } = new();
    public AiProactiveState ProactiveState { get; set; } = new();

    public static AiStructuredMemory CreateDefault()
    {
        return new AiStructuredMemory();
    }
}

internal sealed class AiMemoryProfile
{
    public string UserName { get; set; } = "";
    public string PreferredName { get; set; } = "";
    public string Language { get; set; } = "zh-TW";
}

internal sealed class AiMemoryPreference
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class AiMemoryProject
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class AiMemoryNote
{
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class AiRelationshipState
{
    public int Familiarity { get; set; }
    public string LastImpression { get; set; } = "";
}

internal sealed class AiEmotionSnapshot
{
    public string Emotion { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

internal sealed class AiInteractionStats
{
    public int TotalInteractions { get; set; }
    public DateTimeOffset? LastInteractionAt { get; set; }
}

internal sealed class AiProactiveState
{
    public DateTimeOffset? LastInteractionAt { get; set; }
    public DateTimeOffset? LastProactiveMessageAt { get; set; }
    public string LastContext { get; set; } = "";
}
