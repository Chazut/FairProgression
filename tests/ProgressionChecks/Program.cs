using FairProgression.Models;
using FairProgression.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
}

QuestInfo Make(int id, int xp, bool vanilla, int level = 5) => new()
{
    Id = id.ToString("x24"), Name = "Quest " + id, OriginalXp = xp, ScaledXp = xp,
    IsVanilla = vanilla, MinLevel = level, EstimatedCompletionLevel = level
};

var baseline = Make(1, 100_000, true);
var extra = Make(2, 80_000, false);
var late = Make(3, 500, false, 50) with { LevelFromDifficulty = 30 };
var snapshot = new ProgressionSnapshot([baseline, extra, late], [12, 20]);
var defaults = new FairProgressionConfig();
var protectedMode = defaults with { Mode = "scale_modded_only", ExtraXpPercent = 25 };
var plan = snapshot.Preview(protectedMode);
Check(plan.Brackets[0].TotalXpAfter == 120_000 && plan.Brackets[0].TargetXp == 120_000, "Protected budget");
Check(plan.Quests[0].ScaledXp == 100_000 && plan.Quests[1].ScaledXp == 20_000, "Vanilla protection");
Check(plan.Quests[2].BracketIndex == -1 && plan.Quests[2].ScaledXp == 500, "Above final bracket");
Check(snapshot.Preview(defaults).Brackets[0].TotalXpAfter == 100_000, "Default vanilla budget");
Check(snapshot.Preview(defaults with { Mode = "scale_modded_only" }).Quests[1].ScaledXp == 44_444, "Older protected config");
Check(snapshot.Preview(defaults with { ExtraXpPercent = 100 }).Quests[1].ScaledXp == 80_000, "All extra XP retained");
Check(snapshot.Preview(defaults with { ExtraXpPercent = 25 }).Brackets[0].TotalXpAfter == 120_000, "Shared budget");
var limited = snapshot.Preview(protectedMode with { MinCoefficient = 0.5 });
Check(limited.Brackets[0].FloorApplied && limited.Brackets[0].TotalXpAfter == 140_000, "Floor and actual total");
Check(!plan.Brackets[0].FloorApplied && plan.Quests[1].ScaledXp == 20_000 && extra.OriginalXp == 80_000, "Previews are independent");
Check(snapshot.Preview(protectedMode with { LevelBrackets = "[3,6]" }).Quests[0].BracketIndex == 1, "Manual limits");
Check(new ProgressionSnapshot([], [12]).Preview(defaults).Brackets[0].TotalXpAfter == 0, "Empty bracket");
var onlyMod = new ProgressionSnapshot([extra], [12]).Preview(defaults with { ExtraXpPercent = 50 });
Check(onlyMod.Brackets[0].TotalXpAfter == 40_000, "No vanilla XP in bracket");
var difficult = Make(4, 10_000, false) with { LevelFromDifficulty = 15 };
var boosted = new ProgressionSnapshot([baseline, difficult], [20]).Preview(defaults with { ExtraXpPercent = 50 });
Check(boosted.Quests[1].PreBuffXp == 10_000 && boosted.Quests[1].OriginalXp == 20_000, "Difficulty boost");
Check(boosted.Brackets[0].TargetXp == 105_000 && boosted.Brackets[0].TotalXpAfter == 105_000, "Extra XP measured before boost");

var questId = new MongoId(extra.Id);
var rewards = new List<Reward> { new() { Type = RewardType.Experience, Value = 60_000 }, new() { Type = RewardType.Item, Value = 2 }, new() { Type = RewardType.Experience, Value = 20_000 } };
var quest = new Quest { Id = questId, TraderId = new MongoId(baseline.Id), Name = "Fixture", Description = "", Location = "", Image = "", Side = "", Type = default, Conditions = new(), Restartable = false, CanShowNotificationsInGame = false, Rewards = new() { ["Success"] = rewards } };
Dictionary<MongoId, Quest> database = new() { [questId] = quest };
XPScaler.ApplyRewards(plan.Quests, database);
Check(rewards[0].Value == 15_000 && rewards[2].Value == 5_000 && rewards[1].Value == 2, "Split XP entries and other rewards");
XPScaler.ApplyRewards(plan.Quests, database);
Check(rewards[0].Value == 15_000 && rewards[2].Value == 5_000, "Applying the same plan twice");

var folder = Directory.CreateTempSubdirectory();
try
{
    var path = System.IO.Path.Combine(folder.FullName, "config.jsonc");
    File.WriteAllText(path, "{\n  // custom notes\n  \"other\": \"https://example.org\",\n  \"level_brackets\": [12,20],\n}");
    var original = ConfigDocument.Open(path);
    ConfigLoader.Save(path, original, original.Settings with { ExtraXpPercent = 25 });
    var saved = ConfigDocument.Open(path);
    Check(saved.Settings.ExtraXpPercent == 25 && saved.Settings.LevelBrackets == "[12,20]", "Read and write mixed config formats");
    Check(saved.Source.Contains("// custom notes") && saved.Source.Contains("https://example.org"), "Comments and extensions kept");
    bool stale = false;
    try { ConfigLoader.Save(path, original, original.Settings); } catch (InvalidOperationException) { stale = true; }
    Check(stale && File.ReadAllText(path) == saved.Source, "Stale edit cannot overwrite another save");
    foreach (var value in new[] { "[12,4]", "[0,5]", "[]", "[5,5]", "[2.5]", "not a bracket" })
        Check(!ConfigLoader.TryReadLimits(value, out _), "Invalid limits " + value);
}
finally { folder.Delete(true); }
Console.WriteLine($"{checks} FairProgression checks passed.");
