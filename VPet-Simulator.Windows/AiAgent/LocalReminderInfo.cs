using System;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class LocalReminderInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime Time { get; set; }
    public bool Triggered { get; set; }
}
