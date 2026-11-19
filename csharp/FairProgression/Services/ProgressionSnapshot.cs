using FairProgression.Models;

namespace FairProgression.Services;

public sealed record RewardPlan(List<QuestInfo> Quests, List<BracketSummary> Brackets);

public sealed class ProgressionSnapshot(List<QuestInfo> original, List<int> traderLimits)
{
    private static volatile ProgressionSnapshot? current;
    public static ProgressionSnapshot? Current => current;
    internal static void Publish(ProgressionSnapshot snapshot) => current = snapshot;

    private readonly List<(int medianXp, int level)> medians = QuestAnalyzer.BuildVanillaXpPerLevel(original);

    public RewardPlan Preview(FairProgressionConfig config)
    {
        // Each editor circuit and the startup pass work on independent copies of the original rewards.
        var quests = original.Select(q => q with { }).ToList();
        if (config.BuffUnderRewarded)
            XPScaler.ApplyBuff(quests, medians, config.BuffGapThreshold);
        var limits = config.LevelBrackets.Trim().Equals("trader", StringComparison.OrdinalIgnoreCase)
            ? traderLimits : BracketResolver.Resolve(config.LevelBrackets, null);
        var brackets = XPBudgetCalculator.ComputeBrackets(quests, limits, config.MinCoefficient, config.ExtraXpPercent, config.Mode);
        XPScaler.CalculateRewards(quests, brackets, config);
        return new(quests, brackets);
    }
}
