namespace FairProgression.Models;

/// <summary>
/// Lightweight representation of a quest for XP analysis.
/// </summary>
public sealed class QuestInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? TraderId { get; set; }
    public string TraderName { get; set; } = "Unknown";
    public bool IsVanilla { get; set; }
    public int MinLevel { get; set; }
    public int OriginalXp { get; set; }
    public int ScaledXp { get; set; }
    public int DifficultyScore { get; set; }
    public int LevelFromXp { get; set; }
    public int LevelFromDifficulty { get; set; }
    public int EstimatedCompletionLevel { get; set; }
    public int PrerequisiteDepth { get; set; }
    public int BracketIndex { get; set; }
    public List<string> PrerequisiteQuestIds { get; set; } = [];
}
