using System.Text.Json;

namespace FairProgression.Services;

/// <summary>
/// Loads the experience table (XP per level) from the SPT globals.json file.
/// Path: config.exp.level.exp_table[] where each entry is {"exp": N}.
/// </summary>
public static class ExpTableLoader
{
    public static List<int>? LoadFromFile(string globalsPath)
    {
        if (!File.Exists(globalsPath))
            return null;

        using var stream = File.OpenRead(globalsPath);
        using var doc = JsonDocument.Parse(stream);

        var expTable = doc.RootElement
            .GetProperty("config")
            .GetProperty("exp")
            .GetProperty("level")
            .GetProperty("exp_table");

        var result = new List<int>();
        foreach (var entry in expTable.EnumerateArray())
        {
            result.Add(entry.GetProperty("exp").GetInt32());
        }

        return result;
    }
}
