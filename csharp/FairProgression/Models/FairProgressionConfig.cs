using System.Text.Json.Serialization;

namespace FairProgression.Models;

public sealed record FairProgressionConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "scale_all";

    // Missing in older files. Preserve their protected-vanilla formula until a budget is selected.
    [JsonPropertyName("extra_xp_percent")]
    public int? ExtraXpPercent { get; set; }

    [JsonPropertyName("level_brackets")]
    [JsonConverter(typeof(LevelBracketsConverter))]
    public string LevelBrackets { get; set; } = "trader";

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
