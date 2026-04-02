using FairProgression.Models;
using SPTarkov.Server.Core.Models.Utils;

namespace FairProgression.Services;

/// <summary>
/// Prints a detailed dashboard of XP rebalancing results to the server log.
/// </summary>
public static class Dashboard
{
    private const string Tag = "[FairProgression]";

    public static void Print<T>(
        ISptLogger<T> logger,
        FairProgressionConfig config,
        List<QuestInfo> quests,
        List<BracketSummary> brackets)
    {
        var vanillaCount = quests.Count(q => q.IsVanilla);
        var moddedCount = quests.Count(q => !q.IsVanilla);
        var totalOriginalXp = quests.Sum(q => (long)q.OriginalXp);
        var totalScaledXp = quests.Sum(q => (long)q.ScaledXp);
        var moddedOriginalXp = quests.Where(q => !q.IsVanilla).Sum(q => (long)q.OriginalXp);

        logger.Info($"{Tag} {"",1}");
        logger.Info($"{Tag} ================================================================");
        logger.Info($"{Tag}  XP Rebalancing Dashboard{(config.DryRun ? "  [DRY RUN]" : "")}");
        logger.Info($"{Tag}  Mode: {config.Mode}");
        logger.Info($"{Tag} ================================================================");
        logger.Info($"{Tag}  Quests: {quests.Count} total ({vanillaCount} vanilla, {moddedCount} modded)");
        logger.Info($"{Tag}  Modded quest XP: {FormatXp(moddedOriginalXp)} ({(totalOriginalXp > 0 ? (100.0 * moddedOriginalXp / totalOriginalXp) : 0):F1}% of total)");
        logger.Info($"{Tag} {"",1}");

        // Bracket table
        logger.Info($"{Tag}  {"Bracket",-10} {"Van.Q",6} {"Van.XP",10} {"Mod.Q",6} {"Mod.XP",10} {"Total",10} {"Coeff",7} {"After",10}");
        logger.Info($"{Tag}  {"--------",-10} {"-----",6} {"------",10} {"-----",6} {"------",10} {"-----",10} {"-----",7} {"-----",10}");

        foreach (var b in brackets)
        {
            logger.Info(
                $"{Tag}  {b.Label,-10} {b.VanillaQuestCount,6} {FormatXp(b.VanillaXp),10} " +
                $"{b.ModdedQuestCount,6} {FormatXp(b.ModdedXp),10} " +
                $"{FormatXp(b.TotalXpBefore),10} {b.Coefficient,7:F3} {FormatXp(b.TotalXpAfter),10}");
        }

        // Show unscaled quests (above all brackets)
        var unscaledQuests = quests.Where(q => q.BracketIndex < 0).ToList();
        if (unscaledQuests.Count > 0)
        {
            var unscaledXp = unscaledQuests.Sum(q => (long)q.OriginalXp);
            var lastBracket = brackets[^1].UpperBound;
            logger.Info(
                $"{Tag}  {$"{lastBracket + 1}+",-10} {unscaledQuests.Count(q => q.IsVanilla),6} " +
                $"{FormatXp(unscaledQuests.Where(q => q.IsVanilla).Sum(q => (long)q.OriginalXp)),10} " +
                $"{unscaledQuests.Count(q => !q.IsVanilla),6} " +
                $"{FormatXp(unscaledQuests.Where(q => !q.IsVanilla).Sum(q => (long)q.OriginalXp)),10} " +
                $"{FormatXp(unscaledXp),10} {"---",7} {FormatXp(unscaledXp),10}");
        }

        logger.Info($"{Tag} {"",1}");
        logger.Info($"{Tag}  Total quest XP: {FormatXp(totalOriginalXp)} -> {FormatXp(totalScaledXp)} ({(totalOriginalXp > 0 ? (100.0 * totalScaledXp / totalOriginalXp) : 100):F1}%)");
        logger.Info($"{Tag} ================================================================");

        // Verbose: all modded quests grouped by trader
        if (config.Verbose)
        {
            var moddedByTrader = quests.Where(q => !q.IsVanilla)
                .GroupBy(q => q.TraderName)
                .OrderBy(g => g.Key)
                .ToList();

            logger.Info($"{Tag} {"",1}");
            logger.Info($"{Tag}  All modded quests ({quests.Count(q => !q.IsVanilla)}) by trader:");

            foreach (var group in moddedByTrader)
            {
                var groupXp = group.Sum(q => (long)q.OriginalXp);
                var groupScaled = group.Sum(q => (long)q.ScaledXp);
                logger.Info($"{Tag} {"",1}");
                logger.Info($"{Tag}  --- {group.Key} ({group.Count()} quests, {FormatXp(groupXp)} XP -> {FormatXp(groupScaled)}) ---");
                logger.Info($"{Tag}  {"Quest",-50} {"Bracket",8} {"Lvl",4} {"Diff",5} {"Before",8} {"After",8} {"Coeff",7}");

                foreach (var q in group.OrderBy(q => q.EstimatedCompletionLevel).ThenBy(q => q.Name))
                {
                    var name = q.Name.Length > 50 ? q.Name[..47] + "..." : q.Name;
                    var bracketLabel = q.BracketIndex >= 0 ? brackets[q.BracketIndex].Label : "---";
                    var coeff = q.BracketIndex >= 0 ? $"{brackets[q.BracketIndex].Coefficient,7:F3}" : "  ---  ";
                    logger.Info(
                        $"{Tag}  {name,-50} {bracketLabel,8} {q.EstimatedCompletionLevel,4} " +
                        $"{q.DifficultyScore,5} {q.OriginalXp,8} {q.ScaledXp,8} {coeff}");
                }
            }
        }
    }

    private static string FormatXp(long xp)
    {
        return xp >= 1_000_000 ? $"{xp / 1_000_000.0:F1}M"
             : xp >= 1_000 ? $"{xp / 1_000.0:F1}k"
             : xp.ToString();
    }
}
