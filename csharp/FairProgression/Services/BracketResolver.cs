using System.Text.Json;
using FairProgression.Models;

namespace FairProgression.Services;

/// <summary>
/// Resolves level brackets from config. Supports:
/// - "trader" — auto-compute from vanilla trader loyalty level requirements
/// - JSON array — manual brackets, e.g. "[5, 10, 15, 20, 30, 40, 50]"
/// </summary>
public static class BracketResolver
{
    private static readonly List<int> Fallback = [13, 20, 30, 38, 44];

    /// <summary>
    /// Resolve level brackets from the config string.
    /// </summary>
    public static List<int> Resolve(string bracketsConfig, string? sptRoot)
    {
        var trimmed = bracketsConfig.Trim();

        if (trimmed.Equals("trader", StringComparison.OrdinalIgnoreCase))
            return ResolveFromTraders(sptRoot) ?? Fallback;

        // Try parse as JSON array
        try
        {
            var parsed = JsonSerializer.Deserialize<List<int>>(trimmed);
            if (parsed is { Count: > 0 })
                return parsed;
        }
        catch { }

        return Fallback;
    }

    /// <summary>
    /// Read trader base.json files and extract loyalty level minLevel values.
    /// Groups by LL tier, takes the min/max to create meaningful brackets:
    ///   bracket 1: 1 → min(LL2 levels) - 1      (early game, before any LL2)
    ///   bracket 2: min(LL2) → max(LL2)           (unlocking LL2s)
    ///   bracket 3: max(LL2)+1 → max(LL3)         (unlocking LL3s)
    ///   bracket 4: max(LL3)+1 → max(LL4)         (unlocking LL4s)
    /// </summary>
    private static List<int>? ResolveFromTraders(string? sptRoot)
    {
        if (string.IsNullOrEmpty(sptRoot)) return null;

        var tradersDir = Path.Combine(sptRoot, "SPT_Data", "database", "traders");
        if (!Directory.Exists(tradersDir)) return null;

        // Vanilla trader IDs (skip Fence, BTR, Arena, Storyteller, Caretaker)
        var vanillaTraderIds = new HashSet<string>
        {
            "54cb50c76803fa8b248b4571", // Prapor
            "54cb57776803fa99248b456e", // Therapist
            "58330581ace78e27b8b10cee", // Skier
            "5935c25fb3acc3127c3d8cd9", // Peacekeeper
            "5a7c2eca46aef81a7ca2145d", // Mechanic
            "5ac3b934156ae10c4430e83c", // Ragman
            "5c0647fdd443bc2504c2d371", // Jaeger
        };

        var ll2Levels = new List<int>();
        var ll3Levels = new List<int>();
        var ll4Levels = new List<int>();

        foreach (var traderId in vanillaTraderIds)
        {
            var basePath = Path.Combine(tradersDir, traderId, "base.json");
            if (!File.Exists(basePath)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(basePath));
                var loyaltyLevels = doc.RootElement.GetProperty("loyaltyLevels");
                var llArray = loyaltyLevels.EnumerateArray().ToList();

                // LL index: 0=LL1, 1=LL2, 2=LL3, 3=LL4
                if (llArray.Count > 1) ll2Levels.Add(llArray[1].GetProperty("minLevel").GetInt32());
                if (llArray.Count > 2) ll3Levels.Add(llArray[2].GetProperty("minLevel").GetInt32());
                if (llArray.Count > 3) ll4Levels.Add(llArray[3].GetProperty("minLevel").GetInt32());
            }
            catch { }
        }

        if (ll2Levels.Count == 0) return null;

        var brackets = new List<int>();

        // Early game: before first LL2
        var firstLl2 = ll2Levels.Min() - 1;
        if (firstLl2 >= 2) brackets.Add(firstLl2);

        // LL2 phase: unlocking all LL2s
        var lastLl2 = ll2Levels.Max();
        brackets.Add(lastLl2);

        // LL3 phase
        if (ll3Levels.Count > 0)
        {
            var lastLl3 = ll3Levels.Max();
            brackets.Add(lastLl3);
        }

        // LL4 phase
        if (ll4Levels.Count > 0)
        {
            var lastLl4 = ll4Levels.Max();
            brackets.Add(lastLl4);
        }

        return brackets;
    }
}
