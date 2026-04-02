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
    /// <summary>
    /// Scale quest XP rewards based on bracket coefficients.
    /// Returns the number of quests modified.
    /// </summary>
    public static int Apply(
        List<QuestInfo> quests,
        List<BracketSummary> brackets,
        Dictionary<MongoId, Quest> dbQuests,
        string mode,
        bool dryRun)
    {
        var modified = 0;

        foreach (var questInfo in quests)
        {
            if (questInfo.OriginalXp <= 0)
                continue;

            // Quest above all brackets (e.g., lvl 50+) — no scaling
            if (questInfo.BracketIndex < 0)
                continue;

            var bracket = brackets[questInfo.BracketIndex];
            if (bracket.Coefficient >= 1.0)
                continue;

            // In "scale_modded_only" mode, skip vanilla quests
            if (mode == "scale_modded_only" && questInfo.IsVanilla)
                continue;

            var scaledXp = (int)Math.Round(questInfo.OriginalXp * bracket.Coefficient);
            scaledXp = Math.Max(1, scaledXp); // Never reduce to 0
            questInfo.ScaledXp = scaledXp;

            if (!dryRun && dbQuests.TryGetValue(new MongoId(questInfo.Id), out var dbQuest))
            {
                SetXpReward(dbQuest, scaledXp);
            }

            modified++;
        }

        // For "scale_modded_only" mode, recalculate bracket coefficients to reflect
        // that only modded quests were scaled
        if (mode == "scale_modded_only")
        {
            RecalcBracketTotals(quests, brackets);
        }

        return modified;
    }

    private static void SetXpReward(Quest quest, int newXp)
    {
        if (quest.Rewards?.TryGetValue("Success", out var rewards) != true)
            return;

        if (rewards == null) return;

        foreach (var reward in rewards)
        {
            if (reward.Type == RewardType.Experience)
            {
                reward.Value = newXp;
            }
        }
    }

    /// <summary>
    /// Recalculate bracket after totals based on actual scaled values.
    /// Used for "scale_modded_only" mode where coefficients are not applied uniformly.
    /// </summary>
    private static void RecalcBracketTotals(List<QuestInfo> quests, List<BracketSummary> brackets)
    {
        foreach (var bracket in brackets)
            bracket.TotalXpAfter = 0;

        foreach (var q in quests.Where(q => q.BracketIndex >= 0))
        {
            brackets[q.BracketIndex].TotalXpAfter += q.ScaledXp;
        }
    }
}
