using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace FairProgression.Services;

/// <summary>
/// Estimates quest difficulty by analyzing the AvailableForFinish conditions.
/// Returns a score that can be mapped to an estimated player level.
/// </summary>
public static class DifficultyEstimator
{
    private const string RoubleTpl = "5449016a4bdc2d6f028b456f";
    private const string DollarTpl = "5696686a4bdc2da3298b456a";
    private const string EuroTpl = "569668774bdc2da2298b4568";

    // QuestsExtended uses dist>=5555 as a placeholder for impossible server-side conditions.
    // These are tracked by the BepInEx plugin, not by the server.
    private const double QePlaceholderDistance = 5555;

    public static int EstimateDifficulty(Quest quest)
    {
        if (quest.Conditions?.AvailableForFinish == null)
            return 0;

        var scores = new List<double>();
        var hasHandover = false;
        var hasFind = false;

        foreach (var cond in quest.Conditions.AvailableForFinish)
        {
            var type = cond.ConditionType ?? "";
            if (type == "HandoverItem") hasHandover = true;
            if (type == "FindItem") hasFind = true;
            scores.Add(ScoreCondition(cond));
        }

        if (scores.Count == 0) return 0;

        // If quest has both FindItem and HandoverItem, they likely describe the same objective
        // (find item then hand it over). Halve the FindItem contributions.
        if (hasFind && hasHandover)
        {
            var conditions = quest.Conditions.AvailableForFinish;
            for (var i = 0; i < conditions.Count; i++)
            {
                if ((conditions[i].ConditionType ?? "") == "FindItem")
                    scores[i] *= 0.0; // Don't double-count: find+handover = 1 objective
            }
        }

        // Remove zeros and sort descending
        scores = scores.Where(s => s > 0).OrderByDescending(s => s).ToList();
        if (scores.Count == 0) return 0;

        // Highest single condition + diminishing bonus for additional conditions
        // Cap additional contributions to prevent quests with 20 conditions from inflating
        var total = scores[0];
        var additionalCap = scores[0] * 0.5; // additional conditions add at most 50% of the hardest
        var additional = 0.0;
        for (var i = 1; i < scores.Count; i++)
        {
            additional += scores[i] * 0.15;
        }
        total += Math.Min(additional, additionalCap);

        return (int)Math.Round(total);
    }

    private static double ScoreCondition(QuestCondition cond)
    {
        return cond.ConditionType switch
        {
            "HandoverItem" => ScoreHandover(cond),
            "CounterCreator" => ScoreCounter(cond),
            "HideoutArea" => ScoreHideout(cond),
            "TraderLoyalty" => ScoreTraderLoyalty(cond),
            "FindItem" => ScoreFindItem(cond),
            "LeaveItemAtLocation" or "PlaceBeacon" => 8.0,
            "WeaponAssembly" => 5.0,
            "Skill" => ScoreSkill(cond),
            "SellItemToTrader" => 3.0,
            _ => 1.0
        };
    }

    private static double ScoreHandover(QuestCondition cond)
    {
        var count = (int)(cond.Value ?? 1);

        var targets = cond.Target?.List;
        if (targets != null && targets.Any(t => t == RoubleTpl || t == DollarTpl || t == EuroTpl))
            return 2.0;

        var effectiveCount = Math.Min(count, 20);
        var baseScore = 3.0 + effectiveCount * 1.5;

        if (cond.OnlyFoundInRaid == true)
            baseScore *= 1.4;

        return Math.Min(baseScore, 25);
    }

    private static double ScoreCounter(QuestCondition cond)
    {
        // Detect QuestsExtended placeholder conditions (dist>=5555m)
        if (IsQePlaceholder(cond))
            return 1.0;

        var count = (int)(cond.Value ?? 1);
        var type = cond.Type ?? "";

        if (type is "Elimination" or "Kills")
            return ScoreKills(cond, count);

        // For non-kill counters, check for map/zone difficulty bonus
        var mapBonus = GetCounterMapBonus(cond);
        if (type == "Exploration")
        {
            var score = (5.0 + Math.Min(count, 10) * 1.5) * (1 + mapBonus);
            if (cond.OneSessionOnly == true) score *= 1 + SessionBonus(score);
            return score;
        }

        if (type == "Discover")
            return 3.0;

        // Generic completion (survive, extract, health effects, etc.)
        var effectiveCount = count > 30 ? 5.0 + Math.Log2(count / 30.0) * 1.5 : (double)count;
        var genScore = (3.0 + Math.Min(effectiveCount, 15) * 1.0) * (1 + mapBonus);
        if (cond.OneSessionOnly == true) genScore *= 1 + SessionBonus(genScore);
        return genScore;
    }

    /// <summary>
    /// Extract map difficulty bonus from a counter condition's Location sub-conditions.
    /// </summary>
    private static double GetCounterMapBonus(QuestCondition cond)
    {
        if (cond.Counter?.Conditions == null) return 0;
        var bonus = 0.0;
        foreach (var cc in cond.Counter.Conditions)
        {
            if (cc.ConditionType == "Location" && cc.Target?.List is { Count: > 0 })
                bonus = Math.Max(bonus, cc.Target.List.Max(m => GetMapDifficulty(m)));
        }
        return bonus;
    }

    /// <summary>
    /// Detect QuestsExtended placeholder conditions.
    /// These use an impossible distance (5555m) on a Kills counter condition
    /// because the real objective is tracked client-side by the BepInEx plugin.
    /// </summary>
    private static bool IsQePlaceholder(QuestCondition cond)
    {
        if (cond.Counter?.Conditions == null) return false;
        foreach (var cc in cond.Counter.Conditions)
        {
            if (cc.ConditionType == "Kills" && cc.Distance?.Value >= QePlaceholderDistance)
                return true;
        }
        return false;
    }

    private static double ScoreKills(QuestCondition cond, int count)
    {
        if (count > 1_000_000) return 1.0;

        var baseScore = 0.0;
        var multiplier = 1.0;

        var targetIsPmc = false;
        var targetIsBoss = false;
        var hasBodyPartRestriction = false;
        var hasDistanceRestriction = false;
        var hasWeaponRestriction = false;
        var hasTimeRestriction = false;
        var hasEquipmentRestriction = false;
        var mapDifficulty = 0.0;
        var hasZoneRestriction = false;
        var mapCount = 0;

        if (cond.Counter?.Conditions != null)
        {
            foreach (var cc in cond.Counter.Conditions)
            {
                if (cc.ConditionType == "Kills")
                {
                    if (cc.Distance?.Value >= QePlaceholderDistance)
                        return 1.0;

                    var target = cc.Target?.List?.FirstOrDefault() ?? "";

                    if (target is "AnyPmc" or "Bear" or "Usec")
                        targetIsPmc = true;

                    if (cc.SavageRole is { Count: > 0 })
                        targetIsBoss = true;

                    if (cc.BodyPart is { Count: > 0 })
                        hasBodyPartRestriction = true;

                    if (cc.Distance?.Value > 0)
                        hasDistanceRestriction = true;

                    if (cc.Weapon is { Count: > 0 } || cc.WeaponCaliber is { Count: > 0 }
                        || (cc.WeaponModsInclusive != null && cc.WeaponModsInclusive.Any()))
                        hasWeaponRestriction = true;

                    if (cc.Daytime != null)
                        hasTimeRestriction = true;
                }
                else if (cc.ConditionType == "Location")
                {
                    var maps = cc.Target?.List;
                    if (maps is { Count: > 0 })
                    {
                        mapCount = maps.Count;
                        mapDifficulty = maps.Max(m => GetMapDifficulty(m));
                    }
                }
                else if (cc.ConditionType == "InZone")
                {
                    hasZoneRestriction = true;
                }
                else if (cc.ConditionType == "Equipment")
                {
                    hasEquipmentRestriction = true;
                }
            }
        }

        if (targetIsBoss)
            baseScore = 18.0;
        else if (targetIsPmc)
            baseScore = 7.0;
        else
            baseScore = 3.0;

        var effectiveCount = Math.Min((double)count, 50);
        baseScore += Math.Sqrt(effectiveCount) * 2;

        if (hasBodyPartRestriction) multiplier += 0.25;
        if (hasDistanceRestriction) multiplier += 0.25;
        if (hasWeaponRestriction) multiplier += 0.15;
        if (hasTimeRestriction) multiplier += 0.10;
        if (hasEquipmentRestriction) multiplier += 0.15;

        // Map difficulty: hard maps add a significant multiplier
        multiplier += mapDifficulty;

        // Zone restriction: specific zones are harder (may require keys)
        if (hasZoneRestriction) multiplier += 0.20;

        // Multiple maps: need to visit several maps to complete
        if (mapCount > 1) multiplier += 0.05 * (mapCount - 1);

        var killScore = baseScore * multiplier;

        // OneSessionOnly: bonus scales with how hard the condition already is
        if (cond.OneSessionOnly == true)
            killScore *= 1 + SessionBonus(killScore);

        return Math.Min(killScore, 50);
    }

    /// <summary>
    /// Map difficulty ratings. Returns a multiplier bonus (added to the kill multiplier).
    /// Labs requires a keycard to enter and is the hardest PvE content.
    /// Lighthouse has rogues. Streets is dangerous. Reserve has raiders.
    /// </summary>
    /// <summary>
    /// OneSessionOnly bonus based on the base score of the condition (before session multiplier).
    /// The harder the condition already is, the more OneSessionOnly amplifies it.
    /// A "kill 2 scavs in 1 raid" (base ~6) gets a small bonus,
    /// while "20 PMC headshots in 1 raid" (base ~18) gets a huge bonus.
    /// </summary>
    private static double SessionBonus(double baseScore)
    {
        return baseScore switch
        {
            <= 5 => 0.15,   // trivial in one raid (visit zones, kill 1-2 scavs)
            <= 10 => 0.30,  // moderate (kill a few targets with restrictions)
            <= 20 => 0.50,  // hard (PMC kills, boss kills, many targets)
            <= 30 => 0.75,  // very hard
            _ => 1.00       // extreme
        };
    }

    private static double GetMapDifficulty(string locationId)
    {
        return locationId switch
        {
            "laboratory" or "5b0fc42d86f7744a585f9105" => 0.50, // Labs — requires keycard, hardest map
            "Lighthouse" => 0.30, // Rogues, hard AI
            "Labyrinth" => 0.30, // Hard PvE content
            _ => 0.0
        };
    }

    private static double ScoreHideout(QuestCondition cond)
    {
        var level = (int)(cond.Value ?? 1);
        return level switch
        {
            1 => 8,
            2 => 18,
            >= 3 => 28,
            _ => 5
        };
    }

    private static double ScoreTraderLoyalty(QuestCondition cond)
    {
        var level = (int)(cond.Value ?? 1);
        return level switch
        {
            1 => 5,
            2 => 12,
            3 => 25,
            >= 4 => 38,
            _ => 5
        };
    }

    private static double ScoreFindItem(QuestCondition cond)
    {
        var count = (int)(cond.Value ?? 1);
        return 4.0 + Math.Min(count, 10) * 1.0;
    }

    private static double ScoreSkill(QuestCondition cond)
    {
        var level = (int)(cond.Value ?? 1);
        return level switch
        {
            <= 3 => 5,
            <= 6 => 12,
            <= 8 => 22,
            _ => 30
        };
    }

    public static int DifficultyToLevel(int difficulty)
    {
        return difficulty switch
        {
            <= 0 => 1,
            <= 5 => 1 + difficulty,
            <= 15 => 5 + (difficulty - 5),
            <= 30 => 15 + (difficulty - 15),
            <= 50 => 30 + (int)((difficulty - 30) * 0.75),
            _ => 45 + (difficulty - 50) / 2
        };
    }
}
