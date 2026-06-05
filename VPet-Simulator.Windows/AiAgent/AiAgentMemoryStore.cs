using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VPet_Simulator.Windows.AiAgent.Chat;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentMemoryStore : IStructuredMemoryStore
{
    private static string MemoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VPet",
        "AiAgentMemory.json");

    public string Remember(string fact)
    {
        fact = (fact ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fact))
            return "\u6c92\u6709\u53ef\u4ee5\u8a18\u4f4f\u7684\u5167\u5bb9\u3002";

        var memory = Load();
        if (!memory.ConversationNotes.Any(note => note.Text.Equals(fact, StringComparison.OrdinalIgnoreCase)))
            memory.ConversationNotes.Add(new AiMemoryNote { Text = fact, CreatedAt = DateTimeOffset.Now });

        Save(memory);
        return "\u6211\u8a18\u4f4f\u4e86\uff1a" + fact;
    }

    public string Recall()
    {
        var memory = Load();
        var notes = memory.ConversationNotes.Take(20).ToList();
        var projects = memory.Projects.Take(10).ToList();
        if (notes.Count == 0 && projects.Count == 0)
            return "\u76ee\u524d\u9084\u6c92\u6709\u8a18\u61b6\u3002";

        var builder = new StringBuilder();
        builder.AppendLine("\u5df2\u8a18\u4f4f\u7684\u4e8b\uff1a");
        foreach (var project in projects)
            builder.AppendLine("- 專案：" + project.Name);
        foreach (var note in notes)
            builder.AppendLine("- " + note.Text);
        return builder.ToString();
    }

    public AiStructuredMemory Load()
    {
        try
        {
            if (!File.Exists(MemoryPath))
                return AiStructuredMemory.CreateDefault();

            var json = File.ReadAllText(MemoryPath);
            if (IsLegacyStringList(json))
                return ConvertLegacyList(json);

            return JsonSerializer.Deserialize<AiStructuredMemory>(json) ?? AiStructuredMemory.CreateDefault();
        }
        catch
        {
            return AiStructuredMemory.CreateDefault();
        }
    }

    public void Save(AiStructuredMemory memory)
    {
        var directory = Path.GetDirectoryName(MemoryPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(memory, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(MemoryPath, json);
    }

    private static bool IsLegacyStringList(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AiStructuredMemory ConvertLegacyList(string json)
    {
        var memory = AiStructuredMemory.CreateDefault();
        var items = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
            memory.ConversationNotes.Add(new AiMemoryNote { Text = item, CreatedAt = DateTimeOffset.Now });
        return memory;
    }
}
