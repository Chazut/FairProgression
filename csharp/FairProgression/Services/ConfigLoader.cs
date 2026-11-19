using System.Reflection;
using System.Text.Json;
using FairProgression.Models;

namespace FairProgression.Services;

public static class ConfigLoader
{
    private static readonly object Gate = new();
    public static string GetConfigDir() =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "config");
    public static string ConfigPath => Path.Combine(GetConfigDir(), "config.jsonc");
    public static FairProgressionConfig Load(string path) => ConfigDocument.Open(path).Settings;

    public static void Validate(FairProgressionConfig cfg)
    {
        if (cfg.ExtraXpPercent is < 0 or > 100 || !double.IsFinite(cfg.MinCoefficient) || cfg.MinCoefficient is < 0 or > 1)
            throw new ArgumentException("XP percentages must be between 0 and 100.");
        if (cfg.Mode is not ("scale_all" or "scale_modded_only"))
            throw new ArgumentException("Select a supported scaling mode.");
        if (cfg.BuffGapThreshold < 0)
            throw new ArgumentException("The difficulty gap must be zero or greater.");
        if (string.Equals(cfg.LevelBrackets?.Trim(), "trader", StringComparison.OrdinalIgnoreCase)) return;
        if (!TryReadLimits(cfg.LevelBrackets, out _))
            throw new ArgumentException("Enter increasing positive level limits, for example 12, 20, 32, 42.");
    }

    public static bool TryReadLimits(string? json, out int[] limits)
    {
        limits = [];
        try { limits = JsonSerializer.Deserialize<int[]>(json ?? "") ?? []; }
        catch (JsonException) { return false; }
        return limits.Length > 0 && limits[0] >= 1 && limits.SequenceEqual(limits.Distinct().Order());
    }

    public static ConfigDocument Save(string path, ConfigDocument previous, FairProgressionConfig edited)
    {
        Validate(edited);
        lock (Gate)
        {
            var current = ConfigDocument.Open(path);
            if (current.Source != previous.Source)
                throw new InvalidOperationException("Settings changed since you opened this page. Reload saved settings to continue.");
            var updated = current.Apply(edited);
            if (updated == current.Source) return current;
            var saved = ConfigDocument.Parse(updated);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, updated);
                File.Move(temp, path, true);
            }
            finally { File.Delete(temp); }
            return saved;
        }
    }
}
