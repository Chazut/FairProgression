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

        if (type == "Exploration")
            return 5.0 + Math.Min(count, 10) * 1.5;

        if (type == "Discover")
            return 3.0;

        // Generic completion (survive, extract, health effects, etc.)
        var effectiveCount = count > 30 ? 5.0 + Math.Log2(count / 30.0) * 1.5 : (double)count;
        return 3.0 + Math.Min(effectiveCount, 15) * 1.0;
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
        var hasLocationRestriction = false;
        var hasBodyPartRestriction = false;
        var hasDistanceRestriction = false;
        var hasWeaponRestriction = false;
        var hasTimeRestriction = false;

        if (cond.Counter?.Conditions != null)
        {
            foreach (var cc in cond.Counter.Conditions)
            {
                if (cc.ConditionType == "Kills")
                {
                    // Skip QE placeholder distance
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
                else if (cc.ConditionType is "Location" or "InZone")
                {
                    hasLocationRestriction = true;
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
        if (hasLocationRestriction) multiplier += 0.10;
        if (hasTimeRestriction) multiplier += 0.10;
        if (cond.OneSessionOnly == true) multiplier += 0.4;

        return Math.Min(baseScore * multiplier, 50);
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
