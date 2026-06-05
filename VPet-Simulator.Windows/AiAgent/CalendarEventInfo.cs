using System;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class CalendarEventInfo
{
    public string Id { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool IsAllDay { get; init; }
}
