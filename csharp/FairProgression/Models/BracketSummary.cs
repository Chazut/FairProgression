namespace FairProgression.Models;

/// <summary>
/// XP budget summary for a single level bracket.
/// </summary>
public sealed class BracketSummary
{
    public required string Label { get; init; }
    public int LowerBound { get; init; }
    public int UpperBound { get; init; }
    public int VanillaQuestCount { get; set; }
    public long VanillaXp { get; set; }
    public int ModdedQuestCount { get; set; }
    public long ModdedXp { get; set; }
    public long TotalXpBefore { get; set; }
    public long TotalXpAfter { get; set; }
    public double Coefficient { get; set; } = 1.0;
}
