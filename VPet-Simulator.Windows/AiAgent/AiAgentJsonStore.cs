using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentJsonStore
{
    public static string GetDataPath(string fileName)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VPet", fileName);
    }

    public static List<T> LoadList<T>(string fileName)
    {
        try
        {
            var path = GetDataPath(fileName);
            if (!File.Exists(path))
                return new List<T>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    public static void SaveList<T>(string fileName, List<T> items)
    {
        var path = GetDataPath(fileName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
