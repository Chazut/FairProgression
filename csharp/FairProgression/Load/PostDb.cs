using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using FairProgression.Services;

namespace FairProgression.Load;

/// <summary>
/// Main orchestrator. Runs in the PostSptModLoader phase — the very last phase —
/// to ensure ALL mods have finished adding their quests.
/// Phase order: Database (200k) → PostDBModLoader (400k) → ... → PostSptModLoader (1.1M)
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 50)]
public sealed class PostDb : IOnLoad
{
    private readonly ISptLogger<PostDb> _logger;
    private readonly DatabaseService _db;

    public PostDb(ISptLogger<PostDb> logger, DatabaseService db)
    {
        _logger = logger;
        _db = db;
    }

    public Task OnLoad()
    {
        // Load config
        var configDir = ConfigLoader.GetConfigDir();
        var configPath = Path.Combine(configDir, "config.jsonc");
        var config = ConfigLoader.Load(configPath);

        var quests = _db.GetTables().Templates?.Quests;
        if (quests == null)
        {
            _logger.Warning("[FairProgression] Could not access quest database — aborting");
            return Task.CompletedTask;
        }

        if (VanillaQuestSnapshot.Ids.Count == 0)
        {
            _logger.Warning("[FairProgression] Vanilla snapshot is empty — was CaptureVanilla loaded? Aborting.");
            return Task.CompletedTask;
        }

        // Get English locale data for resolving quest/trader names
        Dictionary<string, string>? locales = null;
        try
        {
            var serverLocales = _db.GetLocales();
            if (serverLocales?.Global?.TryGetValue("en", out var lazyEn) == true)
            {
                locales = lazyEn.Value;
            }
        }
        catch { /* locale resolution is best-effort */ }

        // Load exp_table from globals.json on disk for parallel chain boost
        List<int>? expTable = null;
        try
        {
            var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
            var sptRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
            var globalsPath = Path.Combine(sptRoot, "SPT_Data", "database", "globals.json");
            expTable = ExpTableLoader.LoadFromFile(globalsPath);
        }
        catch { /* exp_table is optional — parallel chain boost will be skipped */ }

        // Analyze all quests
        var questInfos = QuestAnalyzer.Analyze(quests, locales, expTable);

        var moddedCount = questInfos.Count(q => !q.IsVanilla);
        if (moddedCount == 0)
        {
            _logger.Info("[FairProgression] No modded quests detected — nothing to rebalance");
            return Task.CompletedTask;
        }

        // Compute brackets and coefficients
        var brackets = XPBudgetCalculator.ComputeBrackets(questInfos, config.LevelBrackets, config.MinCoefficient);

        // Apply scaling
        var modified = XPScaler.Apply(questInfos, brackets, quests, config.Mode, config.DryRun);

        // Dashboard
        if (config.EnableDashboard)
        {
            Dashboard.Print(_logger, config, questInfos, brackets);
        }

        var action = config.DryRun ? "would modify" : "scaled";
        _logger.Info($"[FairProgression] {action} {modified} quest XP rewards across {brackets.Count} brackets");

        return Task.CompletedTask;
    }
}
