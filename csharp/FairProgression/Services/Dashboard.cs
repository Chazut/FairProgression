using FairProgression.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Common.Models.Logging;

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
        List<BracketSummary> brackets,
        Dictionary<MongoId, Quest>? dbQuests = null)
    {
        var vanillaCount = quests.Count(q => q.IsVanilla);
        var moddedCount = quests.Count(q => !q.IsVanilla);
        var vanillaOriginalXp = quests.Where(q => q.IsVanilla).Sum(q => (long)q.OriginalXp);
        var vanillaScaledXp = quests.Where(q => q.IsVanilla).Sum(q => (long)q.ScaledXp);
        var moddedOriginalXp = quests.Where(q => !q.IsVanilla).Sum(q => (long)q.OriginalXp);
        var moddedScaledXp = quests.Where(q => !q.IsVanilla).Sum(q => (long)q.ScaledXp);
        var totalOriginalXp = vanillaOriginalXp + moddedOriginalXp;
        var totalScaledXp = vanillaScaledXp + moddedScaledXp;

        logger.Info($"{Tag} {"",1}");
        logger.Info($"{Tag} ================================================================");
        logger.Info($"{Tag}  XP Rebalancing Dashboard{(config.DryRun ? "  [DRY RUN]" : "")}");
        logger.Info($"{Tag}  Mode: {config.Mode}");
        logger.Info($"{Tag} ================================================================");
        logger.Info($"{Tag}  Quests: {quests.Count} total ({vanillaCount} vanilla, {moddedCount} modded)");
        logger.Info($"{Tag}  Vanilla quest XP: {FormatXp(vanillaOriginalXp)} -> {FormatXp(vanillaScaledXp)} ({FormatPct(vanillaScaledXp, vanillaOriginalXp)})");
        logger.Info($"{Tag}  Modded  quest XP: {FormatXp(moddedOriginalXp)} -> {FormatXp(moddedScaledXp)} ({FormatPct(moddedScaledXp, moddedOriginalXp)})");
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

        // Buffed quests summary
        var buffedQuests = quests.Where(q => !q.IsVanilla && q.PreBuffXp > 0).ToList();
        if (buffedQuests.Count > 0)
        {
            var buffedXpAdded = buffedQuests.Sum(q => (long)(q.OriginalXp - q.PreBuffXp));
            logger.Info($"{Tag} {"",1}");
            logger.Info($"{Tag}  Buffed {buffedQuests.Count} under-rewarded quests (+{FormatXp(buffedXpAdded)} XP)");
        }

        logger.Info($"{Tag} {"",1}");
        logger.Info($"{Tag}  Total quest XP: {FormatXp(totalOriginalXp)} -> {FormatXp(totalScaledXp)} ({FormatPct(totalScaledXp, totalOriginalXp)})");
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

        if (!config.Verbose) return;

        // Under-rewarded quests: difficulty suggests a much higher level than XP reward
        var underRewarded = quests
            .Where(q => !q.IsVanilla && q.LevelFromDifficulty > q.LevelFromXp + 5 && q.OriginalXp > 0)
            .OrderByDescending(q => q.LevelFromDifficulty - q.LevelFromXp)
            .ToList();

        if (underRewarded.Count > 0)
        {
            logger.Info($"{Tag} {"",1}");
            logger.Info($"{Tag} ================================================================");
            logger.Info($"{Tag}  Under-rewarded quests ({underRewarded.Count}):");
            logger.Info($"{Tag}  Quests where difficulty suggests a higher level than XP reward.");
            logger.Info($"{Tag}  {"Quest",-40} {"Trader",-12} {"Buff",12} {"XpLvl",6} {"DiffLvl",8} {"Gap",4}");
            logger.Info($"{Tag}  {"-----",-40} {"------",-12} {"----",12} {"-----",6} {"-------",8} {"---",4}");

            foreach (var q in underRewarded)
            {
                var name = q.Name.Length > 40 ? q.Name[..37] + "..." : q.Name;
                var trader = q.TraderName.Length > 12 ? q.TraderName[..9] + "..." : q.TraderName;
                var gap = q.LevelFromDifficulty - q.LevelFromXp;
                var buffedStr = q.PreBuffXp > 0 ? $"{q.PreBuffXp,4}->{q.OriginalXp}" : $"{"---",7}";
                logger.Info(
                    $"{Tag}  {name,-40} {trader,-12} {buffedStr,12} {q.LevelFromXp,6} " +
                    $"{q.LevelFromDifficulty,8} {$"+{gap}",4}");

                // Show objectives if DB quests are available
                if (dbQuests != null && dbQuests.TryGetValue(new MongoId(q.Id), out var dbQuest))
                {
                    foreach (var desc in DescribeObjectives(dbQuest))
                        logger.Info($"{Tag}    -> {desc}");
                }
            }
        }
    }

    /// <summary>
    /// Build human-readable descriptions of a quest's AvailableForFinish conditions.
    /// </summary>
    private static List<string> DescribeObjectives(Quest quest)
    {
        var result = new List<string>();
        if (quest.Conditions?.AvailableForFinish == null) return result;

        foreach (var cond in quest.Conditions.AvailableForFinish)
        {
            var desc = DescribeCondition(cond);
            if (!string.IsNullOrEmpty(desc))
                result.Add(desc);
        }
        return result;
    }

    private static string DescribeCondition(QuestCondition cond)
    {
        var val = (int)(cond.Value ?? 0);
        var type = cond.ConditionType ?? "";

        if (type == "HandoverItem")
        {
            var fir = cond.OnlyFoundInRaid == true ? " (FIR)" : "";
            return $"Handover {val} item(s){fir}";
        }

        if (type == "CounterCreator")
        {
            var subType = cond.Type ?? "?";
            var details = new List<string>();

            if (cond.Counter?.Conditions != null)
            {
                foreach (var cc in cond.Counter.Conditions)
                {
                    var ccType = cc.ConditionType ?? "";
                    if (ccType == "Kills")
                    {
                        var target = cc.Target?.List?.FirstOrDefault() ?? "Any";
                        details.Add($"target={target}");
                        if (cc.SavageRole is { Count: > 0 })
                            details.Add($"role={string.Join(",", cc.SavageRole)}");
                        if (cc.BodyPart is { Count: > 0 })
                            details.Add($"bodyPart={string.Join(",", cc.BodyPart)}");
                        if (cc.Distance?.Value > 0)
                            details.Add($"dist>={cc.Distance.Value}m");
                        if (cc.Weapon is { Count: > 0 })
                            details.Add($"weapons={cc.Weapon.Count}");
                        if (cc.WeaponCaliber is { Count: > 0 })
                            details.Add($"caliber={string.Join(",", cc.WeaponCaliber)}");
                    }
                    else if (ccType == "Location")
                    {
                        var locs = cc.Target?.List;
                        if (locs is { Count: > 0 })
                            details.Add($"map={string.Join(",", locs.Take(3))}{(locs.Count > 3 ? "..." : "")}");
                    }
                    else if (ccType == "InZone")
                        details.Add("zone");
                    else if (ccType == "HealthEffect")
                        details.Add("healthEffect");
                    else if (ccType == "ExitStatus")
                        details.Add("extract");
                    else if (ccType == "Equipment")
                        details.Add("equipment");
                }
            }

            var detailStr = details.Count > 0 ? $" [{string.Join(", ", details)}]" : "";
            var session = cond.OneSessionOnly == true ? " (1 raid)" : "";
            return $"{subType} x{val}{session}{detailStr}";
        }

        if (type == "HideoutArea")
            return $"Hideout area lvl {val}";

        if (type == "TraderLoyalty")
            return $"Trader loyalty lvl {val}";

        if (type == "Skill")
            return $"Skill lvl {val}";

        if (type == "FindItem")
            return $"Find {val} item(s)";

        if (type is "LeaveItemAtLocation" or "PlaceBeacon")
            return $"Place {val} item(s)";

        return $"{type} x{val}";
    }

    private static string FormatXp(long xp)
    {
        return xp >= 1_000_000 ? $"{xp / 1_000_000.0:F1}M"
             : xp >= 1_000 ? $"{xp / 1_000.0:F1}k"
             : xp.ToString();
    }

    private static string FormatPct(long after, long before)
    {
        if (before <= 0) return "---";
        var pct = 100.0 * after / before;
        if (pct > 100) return $"+{pct - 100:F1}%";
        if (pct < 100) return $"-{100 - pct:F1}%";
        return "0%";
    }
}
