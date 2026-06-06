using System;
using System.Collections.Generic;

namespace VPet_Simulator.Windows.AiAgent;

internal enum WorkflowTriggerType
{
    Screen,
    Schedule,
    Voice
}

internal enum WorkflowActionType
{
    LaunchProgram,
    StartPomodoro,
    SendMessage,
    Wait
}

internal class WorkflowDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public WorkflowTrigger Trigger { get; set; } = new();
    public List<WorkflowAction> Actions { get; set; } = new();
}

internal class WorkflowTrigger
{
    public WorkflowTriggerType Type { get; set; }

    public string ScreenKeyword { get; set; } = "";

    public string ScheduleCron { get; set; } = "";

    public string VoiceCommand { get; set; } = "";
}

internal class WorkflowAction
{
    public WorkflowActionType Type { get; set; }

    public string ProgramName { get; set; } = "";

    public int PomodoroMinutes { get; set; } = 25;

    public string Message { get; set; } = "";

    public int DelaySeconds { get; set; }
}
