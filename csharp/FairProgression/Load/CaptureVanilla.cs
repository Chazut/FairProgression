using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using FairProgression.Services;

namespace FairProgression.Load;

/// <summary>
/// Loads the vanilla quest ID set from the on-disk quests.json file.
/// This runs early (before other mods' PostDB) but reads from disk rather than
/// the in-memory database, so it doesn't matter if other mods already added quests.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Database + 5)]
public sealed class CaptureVanilla : IOnLoad
{
    private readonly ISptLogger<CaptureVanilla> _logger;

    public CaptureVanilla(ISptLogger<CaptureVanilla> logger)
    {
        _logger = logger;
    }

    public Task OnLoad()
    {
        // Resolve the SPT database path from the assembly location
        // Assembly is at: SPT/user/mods/FairProgression/FairProgression.dll
        // Quests file is at: SPT/SPT_Data/database/templates/quests.json
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
