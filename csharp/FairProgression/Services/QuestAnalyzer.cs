using FairProgression.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace FairProgression.Services;

/// <summary>
/// Analyzes all quests in the database and classifies them as vanilla or modded,
/// computes prerequisite depth, and estimates completion level.
/// </summary>
public static class QuestAnalyzer
{
    /// <summary>
    /// Build a list of QuestInfo from the current quest database.
    /// Uses locale data to resolve human-readable quest names and trader names.
    /// expTable: cumulative XP per level from globals (index 0 = level 1, etc.)
    /// </summary>
    public static List<QuestInfo> Analyze(
        Dictionary<MongoId, Quest> quests,
        Dictionary<string, string>? locales,
        List<int>? expTable = null)
    {
        var result = new List<QuestInfo>(quests.Count);
        var depthCache = new Dictionary<string, int>();

        // Build trader name lookup from locale data
        var traderNames = ResolveTraderNames(locales);

        foreach (var (id, quest) in quests)
        {
            var idStr = id.ToString();
            var xpReward = GetXpReward(quest);
            var minLevel = GetMinLevel(quest);
            var prereqIds = GetPrerequisiteQuestIds(quest);

            // Resolve quest name: try locale "{id} name", then QuestName, then id
            var name = ResolveQuestName(idStr, quest.QuestName, locales);
            var traderId = quest.TraderId.ToString();

            var difficulty = DifficultyEstimator.EstimateDifficulty(quest);

            result.Add(new QuestInfo
            {
                Id = idStr,
                Name = name,
                TraderId = traderId,
                TraderName = traderNames.TryGetValue(traderId, out var tn) ? tn : traderId,
                IsVanilla = VanillaQuestSnapshot.IsVanilla(idStr),
                MinLevel = minLevel,
                DifficultyScore = difficulty,
                OriginalXp = xpReward,
                ScaledXp = xpReward,
                PrerequisiteQuestIds = prereqIds
            });
        }

        // Compute prerequisite depth for each quest
        var lookup = result.ToDictionary(q => q.Id);
        foreach (var q in result)
        {
            q.PrerequisiteDepth = ComputeDepth(q.Id, lookup, depthCache, []);
        }

        // Build XP→level mapping from vanilla quests
        var vanillaXpPerLevel = BuildVanillaXpPerLevel(result);

        // Estimate completion level for each quest (initial pass based on XP)
        foreach (var q in result)
        {
            q.EstimatedCompletionLevel = EstimateCompletionLevel(q, lookup, vanillaXpPerLevel);
        }

        // Propagation pass: ensure each quest's level >= max(prereqs' levels) + 1
        // This correctly handles chained quests (e.g., quest #10 in a 10-quest chain
        // can't be completed at level 5 even if its XP reward matches level 5).
        PropagatePrerequisiteLevels(result, lookup);

        // Parallel chain boost: when multiple quest chains run in parallel (e.g., 20 TTC themes),
        // the player accumulates XP from ALL chains before reaching deeper quests in any single chain.
        // Group modded quests by depth, estimate cumulative XP from shallower quests across all chains,
        // and convert that into a level floor.
        ApplyParallelChainBoost(result, expTable);

        return result;
    }

    /// <summary>
    /// Resolve quest name from locale data. Tries "{questId} name" locale key first,
    /// then falls back to QuestName property, then to the raw ID.
    /// </summary>
    private static string ResolveQuestName(string questId, string? questName, Dictionary<string, string>? locales)
    {
        // Try locale key "{id} name"
        if (locales != null && locales.TryGetValue($"{questId} name", out var localeName) && !string.IsNullOrWhiteSpace(localeName))
            return localeName;

        // Fall back to QuestName if it's not just the ID repeated
        if (!string.IsNullOrWhiteSpace(questName) && questName != questId)
            return questName;

        return questId;
    }

    /// <summary>
    /// Build a trader ID → display name mapping from locale data.
    /// Trader names are stored as "{traderId} Nickname" in the locale.
    /// </summary>
    private static Dictionary<string, string> ResolveTraderNames(Dictionary<string, string>? locales)
    {
        var result = new Dictionary<string, string>();
        if (locales == null) return result;

        foreach (var (key, value) in locales)
        {
            if (key.EndsWith(" Nickname") && !string.IsNullOrWhiteSpace(value))
            {
                var traderId = key[..^" Nickname".Length];
                result[traderId] = value;
            }
        }

        return result;
    }

    private static int GetXpReward(Quest quest)
    {
        if (quest.Rewards?.TryGetValue("Success", out var rewards) != true)
            return 0;

        return rewards?
            .Where(r => r.Type == RewardType.Experience)
            .Sum(r => (int)(r.Value ?? 0)) ?? 0;
    }

    private static int GetMinLevel(Quest quest)
    {
        if (quest.Conditions?.AvailableForStart == null)
            return 0;

        foreach (var cond in quest.Conditions.AvailableForStart)
        {
            if (cond.ConditionType == "Level" && cond.Value.HasValue)
                return (int)cond.Value.Value;
        }

        return 0;
    }

    private static List<string> GetPrerequisiteQuestIds(Quest quest)
    {
        if (quest.Conditions?.AvailableForStart == null)
            return [];

        var ids = new List<string>();
        foreach (var cond in quest.Conditions.AvailableForStart)
        {
            if (cond.ConditionType == "Quest" && cond.Target?.List != null)
            {
                ids.AddRange(cond.Target.List);
            }
        }
        return ids;
    }

    /// <summary>
    /// Boost estimated levels for modded quests in parallel chains.
    /// Scoped PER TRADER: quests from the same trader (= same mod) are assumed to be played
    /// in parallel, but quests from different traders are independent.
    /// For each trader, group quests by prerequisite depth, then for each depth D compute
    /// the cumulative XP from depths 0..D-1 within that trader's quests.
    /// </summary>
    private static void ApplyParallelChainBoost(List<QuestInfo> quests, List<int>? expTable)
    {
        if (expTable == null || expTable.Count == 0) return;

        // Group modded quests by trader
        var byTrader = quests.Where(q => !q.IsVanilla)
            .GroupBy(q => q.TraderId ?? "")
            .ToList();

        foreach (var traderGroup in byTrader)
        {
            var traderQuests = traderGroup.ToList();
            var withDepth = traderQuests.Where(q => q.PrerequisiteDepth > 0).ToList();
            if (withDepth.Count == 0) continue;

            // Group this trader's quests by depth
            var xpByDepth = new Dictionary<int, long>();
            foreach (var q in traderQuests)
            {
                var d = q.PrerequisiteDepth;
                xpByDepth.TryAdd(d, 0);
                xpByDepth[d] += q.OriginalXp;
            }

            // Cumulative XP at each depth from shallower quests within this trader
            var cumulXpBeforeDepth = new Dictionary<int, long>();
            long runningXp = 0;
            foreach (var d in xpByDepth.Keys.OrderBy(d => d))
            {
                cumulXpBeforeDepth[d] = runningXp;
                runningXp += xpByDepth[d];
            }

            // Apply level floor
            foreach (var q in withDepth)
            {
                if (!cumulXpBeforeDepth.TryGetValue(q.PrerequisiteDepth, out var cumulXp) || cumulXp <= 0)
                    continue;

                var levelFromCumul = XpToLevel(cumulXp, expTable);
                if (levelFromCumul > q.EstimatedCompletionLevel)
                    q.EstimatedCompletionLevel = levelFromCumul;
            }
        }
    }

    /// <summary>
    /// Convert a cumulative XP amount to a player level using the exp_table.
    /// </summary>
    private static int XpToLevel(long xp, List<int> expTable)
    {
        long cumul = 0;
        for (var i = 0; i < expTable.Count; i++)
        {
            cumul += expTable[i];
            if (cumul > xp)
                return i + 1; // levels are 1-based
        }
        return expTable.Count;
    }

    /// <summary>
    /// Propagate prerequisite levels: each quest's estimated level must be at least
    /// max(prereqs' estimated levels) + 1. This ensures chained quests (e.g., quest #10
    /// in a chain) are placed in higher brackets, reflecting the real player level
    /// when they actually complete the quest.
    /// </summary>
    private static void PropagatePrerequisiteLevels(List<QuestInfo> quests, Dictionary<string, QuestInfo> lookup)
    {
        var propagated = new Dictionary<string, int>();

        foreach (var q in quests)
        {
            q.EstimatedCompletionLevel = PropagateLevel(q.Id, lookup, propagated, []);
        }
    }

    private static int PropagateLevel(string questId, Dictionary<string, QuestInfo> lookup,
        Dictionary<string, int> cache, HashSet<string> visiting)
    {
        if (cache.TryGetValue(questId, out var cached))
            return cached;

        if (!lookup.TryGetValue(questId, out var quest))
            return 1;

        // Cycle detection
        if (!visiting.Add(questId))
        {
            cache[questId] = quest.EstimatedCompletionLevel;
            return quest.EstimatedCompletionLevel;
        }

        var level = quest.EstimatedCompletionLevel;

        foreach (var prereqId in quest.PrerequisiteQuestIds)
        {
            var prereqLevel = PropagateLevel(prereqId, lookup, cache, visiting);
            // This quest must be at least 1 level above its highest prereq
            level = Math.Max(level, prereqLevel + 1);
        }

        visiting.Remove(questId);
        cache[questId] = level;
        quest.EstimatedCompletionLevel = level;
        return level;
    }

    private static int ComputeDepth(string questId, Dictionary<string, QuestInfo> lookup,
        Dictionary<string, int> cache, HashSet<string> visiting)
    {
        if (cache.TryGetValue(questId, out var cached))
            return cached;

        if (!lookup.TryGetValue(questId, out var quest) || quest.PrerequisiteQuestIds.Count == 0)
        {
            cache[questId] = 0;
            return 0;
        }

        if (!visiting.Add(questId))
        {
            cache[questId] = 0;
            return 0;
        }

        var maxDepth = 0;
        foreach (var prereqId in quest.PrerequisiteQuestIds)
        {
            var d = ComputeDepth(prereqId, lookup, cache, visiting);
            maxDepth = Math.Max(maxDepth, d + 1);
        }

        visiting.Remove(questId);
        cache[questId] = maxDepth;
        return maxDepth;
    }

    /// <summary>
    /// Build a sorted list of (medianXP, level) from vanilla quests to map XP → level.
    /// Groups vanilla quests by their minLevel and computes the median XP for each level.
    /// </summary>
    private static List<(int medianXp, int level)> BuildVanillaXpPerLevel(List<QuestInfo> quests)
    {
        var byLevel = new Dictionary<int, List<int>>();
        foreach (var q in quests.Where(q => q.IsVanilla && q.OriginalXp > 0 && q.MinLevel > 0))
        {
            if (!byLevel.ContainsKey(q.MinLevel))
                byLevel[q.MinLevel] = [];
            byLevel[q.MinLevel].Add(q.OriginalXp);
        }

        var mapping = new List<(int medianXp, int level)>();
        foreach (var (level, xps) in byLevel.OrderBy(kv => kv.Key))
        {
            xps.Sort();
            var median = xps[xps.Count / 2];
            mapping.Add((median, level));
        }

        return mapping;
    }

    /// <summary>
    /// Estimate at which level a quest will realistically be completed.
    /// For vanilla quests: use the explicit minLevel.
    /// For modded quests: if minLevel > 1, use it. Otherwise, estimate from the XP reward amount
    /// by finding the closest match in the vanilla XP→level distribution.
    /// </summary>
    private static int EstimateCompletionLevel(QuestInfo quest, Dictionary<string, QuestInfo> lookup,
        List<(int medianXp, int level)> vanillaXpPerLevel)
    {
        // Vanilla quests: trust their minLevel
        if (quest.IsVanilla)
            return Math.Max(1, quest.MinLevel);

        // Modded quests with an explicit minLevel > 1: use it
        if (quest.MinLevel > 1)
            return quest.MinLevel;

        // Modded quests with minLevel <= 1: combine XP-based and difficulty-based estimates
        var levelFromXp = quest.OriginalXp > 0 && vanillaXpPerLevel.Count > 0
            ? EstimateLevelFromXp(quest.OriginalXp, vanillaXpPerLevel)
            : 1;

        var levelFromDifficulty = quest.DifficultyScore > 0
            ? DifficultyEstimator.DifficultyToLevel(quest.DifficultyScore)
            : 1;

        // Take the max of both estimates
        var level = Math.Max(levelFromXp, levelFromDifficulty);

        // Also check prereqs as a floor
        foreach (var prereqId in quest.PrerequisiteQuestIds)
        {
            if (lookup.TryGetValue(prereqId, out var prereq))
                level = Math.Max(level, prereq.EstimatedCompletionLevel + 1);
        }

        return Math.Max(1, level);
    }

    /// <summary>
    /// Given an XP reward amount, find the vanilla level whose median XP is closest.
    /// </summary>
    private static int EstimateLevelFromXp(int xp, List<(int medianXp, int level)> vanillaXpPerLevel)
    {
        var bestLevel = vanillaXpPerLevel[0].level;
        var bestDiff = int.MaxValue;

        foreach (var (medianXp, level) in vanillaXpPerLevel)
        {
            var diff = Math.Abs(xp - medianXp);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestLevel = level;
            }
        }

        return bestLevel;
    }
}
