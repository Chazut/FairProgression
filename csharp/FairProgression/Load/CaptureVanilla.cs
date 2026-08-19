using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using FairProgression.Services;

namespace FairProgression.Load;

/// <summary>
/// Loads the vanilla quest ID set from the on-disk quests.json file.
/// This runs early (before other mods' quest injection) but reads from disk rather
/// than the in-memory database, so it doesn't matter if other mods already added quests.
/// 4.1: the Database stage is gone — the DB is fully imported before the host is even
/// built, so Preload (the earliest named stage after Watermark) is the equivalent of
/// the old "just after Database" slot. Preload + 5 keeps us ahead of quest-injecting
/// mods (e.g. TTC/StatRewards use Preload + 10 for their early work), preserving the
/// old "capture before anyone adds quests" contract. Disk-reading makes this
/// belt-and-suspenders: even a mod running earlier can't alter the on-disk vanilla set.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 5)]
public sealed class CaptureVanilla : IOnLoad
{
    private readonly ISptLogger<CaptureVanilla> _logger;

    public CaptureVanilla(ISptLogger<CaptureVanilla> logger)
    {
        _logger = logger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Resolve the SPT database path from the assembly location
        // Assembly is at: SPT_Runtime/user/mods/FairProgression/FairProgression.dll
        // Quests file is at: SPT_Runtime/SPT_Data/database/templates/quests.json
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var sptRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
        var questsPath = Path.Combine(sptRoot, "SPT_Data", "database", "templates", "quests.json");

        if (!File.Exists(questsPath))
        {
            _logger.Warning($"[FairProgression] Vanilla quests file not found at: {questsPath}");
            return Task.CompletedTask;
        }

        VanillaQuestSnapshot.LoadFromFile(questsPath);
        _logger.Info($"[FairProgression] Loaded {VanillaQuestSnapshot.Ids.Count} vanilla quest IDs from disk");
        return Task.CompletedTask;
    }
}
