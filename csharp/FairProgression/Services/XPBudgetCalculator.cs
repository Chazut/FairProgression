using FairProgression.Models;

namespace FairProgression.Services;

/// <summary>
/// Computes scaling coefficients per level bracket to keep the total quest XP
/// aligned with the vanilla baseline.
/// </summary>
public static class XPBudgetCalculator
{
    /// <summary>
    /// Build bracket summaries and compute scaling coefficients.
    /// Quests estimated above the last bracket are left untouched (no scaling).
    /// </summary>
    public static List<BracketSummary> ComputeBrackets(List<QuestInfo> quests, List<int> levelBrackets, double minCoefficient, int? extraXpPercent = null, string mode = "scale_all")
    {
        var brackets = BuildBrackets(levelBrackets);

        // Assign each quest to a bracket based on estimated completion level
        foreach (var quest in quests)
        {
            var bracket = FindBracket(brackets, quest.EstimatedCompletionLevel);
            if (bracket == null)
            {
                // Quest is above all brackets — no scaling
                quest.BracketIndex = -1;
                continue;
            }

            quest.BracketIndex = brackets.IndexOf(bracket);

            if (quest.IsVanilla)
            {
                bracket.VanillaQuestCount++;
                bracket.VanillaXp += quest.OriginalXp;
            }
            else
            {
                bracket.ModdedQuestCount++;
                bracket.ModdedXp += quest.OriginalXp;
                bracket.RawModdedXp += quest.BaseXp;
            }

            bracket.TotalXpBefore += quest.OriginalXp;
        }

        // The optional difficulty buff affects allocation, not the amount of extra XP requested.
        double fraction = Math.Clamp(extraXpPercent ?? 0, 0, 100) / 100d;
        foreach (var bracket in brackets)
        {
            double target = bracket.VanillaXp + fraction * bracket.RawModdedXp;
            bracket.TargetXp = (long)Math.Round(target);
            if (bracket.TotalXpBefore <= 0 || bracket.ModdedXp <= 0)
            {
                bracket.Coefficient = 1.0;
                bracket.TotalXpAfter = bracket.TotalXpBefore;
            }
            else
            {
                var protectVanilla = mode == "scale_modded_only" && extraXpPercent.HasValue;
                var rawCoeff = protectVanilla
                    ? (target - bracket.VanillaXp) / bracket.ModdedXp
                    : target / bracket.TotalXpBefore;
                bracket.FloorApplied = rawCoeff < minCoefficient;
                bracket.Coefficient = Math.Max(rawCoeff, minCoefficient);
                bracket.TotalXpAfter = (long)(bracket.TotalXpBefore * bracket.Coefficient);
            }
        }

        return brackets;
    }

    private static List<BracketSummary> BuildBrackets(List<int> levelBrackets)
    {
        var brackets = new List<BracketSummary>();
        var prev = 1;

        foreach (var upper in levelBrackets)
        {
            brackets.Add(new BracketSummary
            {
                Label = $"{prev}-{upper}",
                LowerBound = prev,
                UpperBound = upper
            });
            prev = upper + 1;
        }

        return brackets;
    }

    /// <summary>
    /// Find the bracket for a given level. Returns null if the level is above all brackets
    /// (those quests should not be scaled).
    /// </summary>
    private static BracketSummary? FindBracket(List<BracketSummary> brackets, int level)
    {
        foreach (var bracket in brackets)
        {
            if (level >= bracket.LowerBound && level <= bracket.UpperBound)
                return bracket;
        }

        // Level is above the last bracket — no scaling
        return null;
    }
}
