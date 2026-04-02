using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace FairProgression.Services;

/// <summary>
/// Estimates quest difficulty by analyzing the AvailableForFinish conditions.
/// Returns a score that can be mapped to an estimated player level.
/// </summary>
public static class DifficultyEstimator
{
    // Rouble template ID — HandoverItem with this target is a money payment, not an item collection
    private const string RoubleTpl = "5449016a4bdc2d6f028b456f";
    private const string DollarTpl = "5696686a4bdc2da3298b456a";
    private const string EuroTpl = "569668774bdc2da2298b4568";

    public static int EstimateDifficulty(Quest quest)
    {
        if (quest.Conditions?.AvailableForFinish == null)
            return 0;

        var scores = new List<double>();
        foreach (var cond in quest.Conditions.AvailableForFinish)
        {
            scores.Add(ScoreCondition(cond));
        }

        if (scores.Count == 0) return 0;

        // Use: highest single condition + small bonus for each additional condition
        // This prevents 10x "handover 1 card" from scoring 10x too high
        scores.Sort((a, b) => b.CompareTo(a));
        var total = scores[0];
        for (var i = 1; i < scores.Count; i++)
            total += scores[i] * 0.2; // diminishing contribution from additional conditions

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
            "LeaveItemAtLocation" or "PlaceBeacon" => 8.0, // plant/place = moderate
            "WeaponAssembly" => 5.0, // gunsmith-style = easy-moderate
            "Skill" => ScoreSkill(cond),
            "SellItemToTrader" => 3.0, // trivial
            _ => 1.0
        };
    }

    private static double ScoreHandover(QuestCondition cond)
    {
        var count = (int)(cond.Value ?? 1);

        // Check if target is money (roubles/dollars/euros) — trivial regardless of amount
        var targets = cond.Target?.List;
        if (targets != null && targets.Any(t => t == RoubleTpl || t == DollarTpl || t == EuroTpl))
            return 2.0; // paying money is trivial

        // Item handover: high counts (>20) are likely currency or stackable items
        var effectiveCount = Math.Min(count, 20);
        var baseScore = 3.0 + effectiveCount * 1.5;

        if (cond.OnlyFoundInRaid == true)
            baseScore *= 1.4;

        return Math.Min(baseScore, 25);
    }

    private static double ScoreCounter(QuestCondition cond)
    {
        var count = (int)(cond.Value ?? 1);
        var type = cond.Type ?? "";

        if (type is "Elimination" or "Kills")
            return ScoreKills(cond, count);

        if (type == "Exploration")
            return 5.0 + Math.Min(count, 10) * 1.5;

        if (type == "Discover")
            return 3.0; // discover items = trivial

        // Generic completion (survive, extract, health effects, etc.)
        // Cap effective count — high values are damage/distance/time, not discrete counts
        var effectiveCount = count > 30 ? 5.0 + Math.Log2(count / 30.0) * 1.5 : (double)count;
        return 3.0 + Math.Min(effectiveCount, 15) * 1.0;
    }

    private static double ScoreKills(QuestCondition cond, int count)
    {
        // Impossible/placeholder quests (value > 1M)
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

        // Base score by target type
        if (targetIsBoss)
            baseScore = 18.0;
        else if (targetIsPmc)
            baseScore = 7.0;
        else
            baseScore = 3.0;

        // Kill count scaling (diminishing returns, cap at 50 real kills)
        var effectiveCount = Math.Min((double)count, 50);
        baseScore += Math.Sqrt(effectiveCount) * 2;

        // Restriction multipliers
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
            2 => 15,
            3 => 28,
            >= 4 => 40,
            _ => 5
        };
    }

    private static double ScoreFindItem(QuestCondition cond)
    {
        var count = (int)(cond.Value ?? 1);
        // Find items in raid: moderate, scales mildly with count
        return 4.0 + Math.Min(count, 10) * 1.0;
    }

    private static double ScoreSkill(QuestCondition cond)
    {
        var level = (int)(cond.Value ?? 1);
        // Skill levels: low=easy, high=very grindy late-game
        return level switch
        {
            <= 3 => 5,
            <= 6 => 12,
            <= 8 => 22,
            _ => 30 // level 9-10+ = very late game
        };
    }

    /// <summary>
    /// Map a difficulty score to an estimated player level.
    /// </summary>
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
