using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FairProgression.Models;

namespace FairProgression.Services;

/// <summary>
/// Loads the FairProgression config.jsonc from the mod's config directory.
/// Supports JSONC (strips // and /* */ comments before parsing).
/// </summary>
public static partial class ConfigLoader
{
    public static FairProgressionConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            return new FairProgressionConfig();

        var raw = File.ReadAllText(configPath);
        var json = StripComments(raw);
        return JsonSerializer.Deserialize<FairProgressionConfig>(json) ?? new FairProgressionConfig();
    }

    /// <summary>
    /// Resolve the config directory based on the assembly location (deployed mod path).
    /// </summary>
    public static string GetConfigDir()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var configDir = Path.Combine(asmDir, "config");
        return configDir;
    }

    private static string StripComments(string input)
    {
        // Remove single-line comments
        var result = SingleLineComment().Replace(input, "");
        // Remove multi-line comments
        result = MultiLineComment().Replace(result, "");
        return result;
    }

    [GeneratedRegex(@"//.*?$", RegexOptions.Multiline)]
    private static partial Regex SingleLineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex MultiLineComment();
}
