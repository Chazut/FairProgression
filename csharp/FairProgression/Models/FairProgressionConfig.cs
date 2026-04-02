using System.Text.Json.Serialization;

namespace FairProgression.Models;

public sealed class FairProgressionConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "scale_all";

    [JsonPropertyName("level_brackets")]
    public List<int> LevelBrackets { get; set; } = [5, 10, 15, 20, 25, 30, 40, 50];

    [JsonPropertyName("min_coefficient")]
    public double MinCoefficient { get; set; } = 0.25;

    [JsonPropertyName("enable_dashboard")]
    public bool EnableDashboard { get; set; } = true;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; }

    [JsonPropertyName("buff_under_rewarded")]
    public bool BuffUnderRewarded { get; set; } = true;

    [JsonPropertyName("buff_gap_threshold")]
    public int BuffGapThreshold { get; set; } = 5;

    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; }
}
