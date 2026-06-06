using System;
using System.Collections.Generic;

namespace VPet_Simulator.Windows.AiAgent;

public enum WorkflowTriggerType
{
    Screen,
    Schedule,
    Input
}

public enum WorkflowActionType
{
    LaunchProgram,
    StartPomodoro,
    SendMessage,
    Wait,
    ShowNotification
}

public class WorkflowDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public WorkflowTrigger Trigger { get; set; } = new();
    public List<WorkflowAction> Actions { get; set; } = new();
}

public class WorkflowTrigger
{
    public WorkflowTriggerType Type { get; set; }

    public string ScreenKeyword { get; set; } = "";

    public string ScheduleCron { get; set; } = "";

    public string InputKeyword { get; set; } = "";
}

public class WorkflowAction
{
    public WorkflowActionType Type { get; set; }

    public string ProgramName { get; set; } = "";

    public int PomodoroMinutes { get; set; } = 25;

    public string Message { get; set; } = "";

    public int DelaySeconds { get; set; }

    public string NotificationTitle { get; set; } = "";
    public string NotificationBody { get; set; } = "";
}

internal class WorkflowDisplayItem
{
    public string Name { get; set; } = "";
    public string TriggerTypeDisplay { get; set; } = "";
    public string TriggerSummary { get; set; } = "";
    public bool Enabled { get; set; }
    public int ActionCount { get; set; }
    public WorkflowDefinition? Source { get; set; }
}
