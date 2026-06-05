namespace VPet_Simulator.Windows.AiAgent;

internal sealed class OllamaSkillCall
{
    public static readonly OllamaSkillCall None = new("none");

    public OllamaSkillCall(
        string skillName,
        string location = "",
        string fact = "",
        string title = "",
        string time = "",
        string note = "",
        string target = "",
        string query = "",
        string date = "",
        string startDatetime = "",
        string endDatetime = "",
        string description = "",
        string keyword = "",
        string eventId = "",
        int daysAhead = 30,
        bool deleteAll = false)
    {
        SkillName = string.IsNullOrWhiteSpace(skillName) ? "none" : skillName.Trim();
        Location = location?.Trim() ?? "";
        Fact = fact?.Trim() ?? "";
        Title = title?.Trim() ?? "";
        Time = time?.Trim() ?? "";
        Note = note?.Trim() ?? "";
        Target = target?.Trim() ?? "";
        Query = query?.Trim() ?? "";
        Date = date?.Trim() ?? "";
        StartDatetime = startDatetime?.Trim() ?? "";
        EndDatetime = endDatetime?.Trim() ?? "";
        Description = description?.Trim() ?? "";
        Keyword = keyword?.Trim() ?? "";
        EventId = eventId?.Trim() ?? "";
        DaysAhead = daysAhead <= 0 ? 30 : daysAhead;
        DeleteAll = deleteAll;
    }

    public string SkillName { get; }
    public string Location { get; }
    public string Fact { get; }
    public string Title { get; }
    public string Time { get; }
    public string Note { get; }
    public string Target { get; }
    public string Query { get; }
    public string Date { get; }
    public string StartDatetime { get; }
    public string EndDatetime { get; }
    public string Description { get; }
    public string Keyword { get; }
    public string EventId { get; }
    public int DaysAhead { get; }
    public bool DeleteAll { get; }

    public bool HasSkill => !SkillName.Equals("none", System.StringComparison.OrdinalIgnoreCase);
}
