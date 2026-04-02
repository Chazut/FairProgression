using System.Text.Json;

namespace FairProgression.Services;

/// <summary>
/// Holds the set of vanilla quest IDs, loaded directly from the SPT quests.json file on disk.
/// Reading from disk (instead of the in-memory DB) guarantees we get the unmodified vanilla set,
/// regardless of mod load ordering.
/// </summary>
public static class VanillaQuestSnapshot
{
    public static HashSet<string> Ids { get; } = [];

    /// <summary>
    /// Load vanilla quest IDs by reading the quests.json file from disk.
    /// The file is a JSON object where each key is a quest ID.
    /// </summary>
    public static void LoadFromFile(string questsJsonPath)
    {
        Ids.Clear();

        if (!File.Exists(questsJsonPath))
            return;

        using var stream = File.OpenRead(questsJsonPath);
        using var doc = JsonDocument.Parse(stream);

        foreach (var prop in doc.RootElement.EnumerateObject())
            Ids.Add(prop.Name);
    }

    public static bool IsVanilla(string questId) => Ids.Contains(questId);
}
