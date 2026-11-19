using FairProgression.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace FairProgression.Services;

/// <summary>
/// Applies computed scaling coefficients to quest XP rewards in the database.
/// </summary>
public static class XPScaler
{
    public static void CalculateRewards(List<QuestInfo> quests, List<BracketSummary> brackets, FairProgressionConfig config)
    {
        foreach (var bracket in brackets)
            bracket.TotalXpAfter = 0;

        foreach (var quest in quests)
        {
            quest.ScaledXp = quest.BaseXp;
            if (quest.BracketIndex < 0)
                continue;

            var bracket = brackets[quest.BracketIndex];
            bool eligible = quest.OriginalXp > 0 && !(quest.IsVanilla && config.Mode == "scale_modded_only");
            // Earlier builds applied buffs only when this bracket was actually scaled.
            if (eligible && (bracket.Coefficient < 1 || config.ExtraXpPercent > 0))
                quest.ScaledXp = Math.Max(1, (int)Math.Round(quest.OriginalXp * bracket.Coefficient));
            bracket.TotalXpAfter += quest.ScaledXp;
        }
    }

    public static void ApplyRewards(List<QuestInfo> quests, Dictionary<MongoId, Quest> database)
    {
        foreach (var planned in quests)
        {
            if (planned.ScaledXp == planned.BaseXp || !database.TryGetValue(new MongoId(planned.Id), out var quest))
                continue;

            var rewards = quest.Rewards?.GetValueOrDefault("Success");
            if (rewards == null) continue;
            // Allocate the planned total across every XP entry, including quests from custom traders.
            long currentTotal = rewards.Where(r => r.Type == RewardType.Experience).Sum(r => (long)(r.Value ?? 0));
            if (currentTotal <= 0 || currentTotal == planned.ScaledXp) continue;
            long originalSum = 0, assigned = 0;
            foreach (var reward in rewards.Where(r => r.Type == RewardType.Experience))
            {
                originalSum += (int)(reward.Value ?? 0);
                var next = (long)Math.Round((double)originalSum * planned.ScaledXp / currentTotal);
                reward.Value = (int)(next - assigned);
                assigned = next;
            }
        }
    }

    /// <summary>
    /// Buff under-rewarded modded quests: when difficulty suggests a much higher level
    /// than the XP reward, increase the XP to match the vanilla median for the difficulty level.
    /// Returns the number of quests buffed.
    /// </summary>
    public static void ApplyBuff(
        List<QuestInfo> quests,
        List<(int medianXp, int level)> vanillaXpPerLevel,
        int gapThreshold)
    {

        foreach (var q in quests)
        {
            if (q.IsVanilla || q.OriginalXp <= 0)
                continue;

            var gap = q.LevelFromDifficulty - q.LevelFromXp;
            if (gap < gapThreshold)
                continue;

            // Use midpoint between XP level and difficulty level as target
            // (not the full difficulty level â€” avoids over-buffing)
            var midLevel = (q.LevelFromXp + q.LevelFromDifficulty) / 2;
            var targetXp = LevelToXp(midLevel, vanillaXpPerLevel);

            // Cap: never more than 2x the original XP
            targetXp = Math.Min(targetXp, (int)Math.Min(int.MaxValue, (long)q.OriginalXp * 2));

            if (targetXp <= q.OriginalXp)
                continue;

            // Buff runs before scaling â€” update OriginalXp so brackets account for it.
            // The bracket budget then determines the final reward.
            q.PreBuffXp = q.OriginalXp;
            q.OriginalXp = targetXp;
            q.ScaledXp = targetXp;
        }
    }

    /// <summary>
    /// Find the vanilla median XP for a given level.
    /// Uses the closest matching level from the vanilla XP mapping.
    /// </summary>
    private static int LevelToXp(int level, List<(int medianXp, int level)> vanillaXpPerLevel)
    {
        if (vanillaXpPerLevel.Count == 0) return 0;

        var bestXp = vanillaXpPerLevel[0].medianXp;
        var bestDiff = int.MaxValue;

        foreach (var (medianXp, lvl) in vanillaXpPerLevel)
        {
            var diff = Math.Abs(level - lvl);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestXp = medianXp;
            }
        }

        return bestXp;
    }

}
