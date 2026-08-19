using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using FairProgression.Services;

namespace FairProgression.Load;

/// <summary>
/// Main orchestrator. Runs in the PostLoad phase — the very last named phase in 4.1 —
/// to ensure ALL mods have finished adding their quests.
/// 4.1 phase order: Preload (100k) → GameCallbacks (200k) → ... → PostLoad (1M).
/// PostLoad + 999 keeps the old "after everyone else" contract: quest-injecting mods
/// run at lower offsets (e.g. TTC injects its quests at PostLoad + 50, MissionControl
/// runs at PostLoad + 100), so their quests are in the DB before we rebalance.
/// A mod could still register beyond PostLoad + 999 (TypePriority is an open int),
/// but the same held for PostSptModLoader + N in 4.0.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 999)]
public sealed class PostDb : IOnLoad
{
    private readonly ISptLogger<PostDb> _logger;
    private readonly TemplateTable _templates;
    private readonly LocaleService _localeService;

    public PostDb(ISptLogger<PostDb> logger, TemplateTable templates, LocaleService localeService)
    {
        _logger = logger;
        _templates = templates;
        _localeService = localeService;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Load config
        var configDir = ConfigLoader.GetConfigDir();
        var configPath = Path.Combine(configDir, "config.jsonc");
        var config = ConfigLoader.Load(configPath);

        var quests = _templates.Quests;

        if (VanillaQuestSnapshot.Ids.Count == 0)
        {
            _logger.Warning("[FairProgression] Vanilla snapshot is empty — was CaptureVanilla loaded? Aborting.");
            return Task.CompletedTask;
        }

        // Get English locale data for resolving quest/trader names
        Dictionary<string, string>? locales = null;
        try
        {
            locales = _localeService.GetLocaleDb("en");
        }
        catch { /* locale resolution is best-effort */ }

        // Resolve SPT root path for disk-based lookups
        var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        var sptRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));

        // Load exp_table from globals.json on disk for parallel chain boost
        List<int>? expTable = null;
        try
        {
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

        // Buff under-rewarded quests BEFORE computing brackets,
        // so the buffed XP is included in the budget calculation
        var buffed = 0;
        if (config.BuffUnderRewarded)
        {
            var vanillaXpPerLevel = QuestAnalyzer.BuildVanillaXpPerLevel(questInfos);
            buffed = XPScaler.ApplyBuff(questInfos, quests, vanillaXpPerLevel, config.BuffGapThreshold, config.DryRun);
        }

        // Resolve level brackets (from trader loyalty levels or manual config)
        var levelBrackets = BracketResolver.Resolve(config.LevelBrackets, sptRoot);

        // Compute brackets and coefficients (now includes buffed XP)
        var brackets = XPBudgetCalculator.ComputeBrackets(questInfos, levelBrackets, config.MinCoefficient);

        // Apply scaling (reduce over-rewarded)
        var modified = XPScaler.Apply(questInfos, brackets, quests, config.Mode, config.DryRun);

        // Dashboard
        if (config.EnableDashboard)
        {
            Dashboard.Print(_logger, config, questInfos, brackets, quests);
        }

        var action = config.DryRun ? "would modify" : "scaled";
        _logger.Info($"[FairProgression] {action} {modified} quest XP rewards, buffed {buffed} under-rewarded quests");

        return Task.CompletedTask;
    }
}
