# FairProgression

An SPT 4.1.6 server mod that automatically rebalances quest XP rewards to preserve the vanilla progression curve when modded quests are installed.

## The Problem

When you install mods that add quests (custom traders, quest overhauls, etc.), the extra XP from those quests causes you to level up faster than intended. This creates a mismatch: your level is high enough for better gear, but you haven't progressed through the vanilla trader quests to actually unlock it.

## The Solution

FairProgression runs **after** all other mods have loaded and:

1. **Identifies** which quests are vanilla and which are modded (by comparing the in-memory database against the on-disk vanilla quest file)
2. **Estimates** at which level each quest will realistically be completed, using:
   - XP reward amount mapped against vanilla quest distributions
   - Objective difficulty analysis (kill targets, boss fights, hideout requirements, map difficulty, etc.)
   - Prerequisite chain depth propagation (vanilla + modded)
   - Parallel chain XP accumulation per trader (e.g., 20 quest themes played simultaneously)
3. **Buffs up** under-rewarded quests first: when a quest's objectives are significantly harder than its XP reward suggests, increases the XP to match (capped at x2)
4. **Scales down** over-rewarded quests: assigns quests to level brackets aligned with trader loyalty unlocks and computes a scaling coefficient per bracket so the total quest XP matches the vanilla budget
5. **Logs** a detailed dashboard of all changes

## Installation

Extract the release ZIP into your SPT game folder (next to `EscapeFromTarkov.exe`). The mod installs to `SPT_Runtime/user/mods/FairProgression`. Start the server.

When upgrading, back up your `config/config.jsonc` before extracting the ZIP, then restore it to keep your settings. The config format is unchanged. For SPT 4.0.13, keep using FairProgression 1.0.0.

### Build from source

Requires the .NET 10 SDK and an SPT 4.1.6 installation. Set `SPT_DIR` to its `SPT_Runtime` directory, or pass it explicitly:

```powershell
dotnet build csharp/FairProgression/FairProgression.csproj -c Release -p:SPT_DIR="C:/Games/SPT-4.1/SPT_Runtime"
```

The DLL and default config are written to `csharp/FairProgression/bin/Release/net10.0`. Builds do not deploy automatically. Add `-p:DeployOnBuild=true` to copy them into an existing mod installation (this also replaces its config).

## Configuration

Edit `config/config.jsonc`:

```jsonc
{
  // "scale_all" — reduce ALL quest XP proportionally (vanilla + modded)
  // "scale_modded_only" — only reduce modded quest XP
  "mode": "scale_all",

  // Level brackets for XP budget calculation.
  //   "trader" — auto-compute from vanilla trader loyalty level requirements
  //              (e.g., [12, 20, 32, 42] based on LL2/LL3/LL4 unlock levels)
  //   [5, 10, ...] — manual JSON array of bracket upper bounds
  "level_brackets": "trader",

  // Minimum scaling coefficient (never reduce below this ratio)
  "min_coefficient": 0.25,

  // Buff under-rewarded quests (difficulty >> XP reward), capped at x2
  "buff_under_rewarded": true,

  // Minimum level gap (difficultyLevel - xpLevel) to trigger a buff
  "buff_gap_threshold": 5,

  // Print detailed dashboard in server logs
  "enable_dashboard": true,

  // Calculate and log without modifying anything
  "dry_run": false,

  // Log every modded quest with difficulty scores + under-rewarded details
  "verbose": false
}
```

## Dashboard Example

```
[FairProgression] ================================================================
[FairProgression]  XP Rebalancing Dashboard
[FairProgression]  Mode: scale_all
[FairProgression] ================================================================
[FairProgression]  Quests: 1492 total (558 vanilla, 934 modded)
[FairProgression]  Vanilla quest XP: 11.0M -> 7.9M (-27.6%)
[FairProgression]  Modded  quest XP: 24.6M -> 21.4M (-13.1%)
[FairProgression]
[FairProgression]  Bracket  Van.Q   Van.XP  Mod.Q   Mod.XP    Total  Coeff     After
[FairProgression]  -------  -----   ------  -----   ------    -----  -----     -----
[FairProgression]  1-12       167     2.0M    117    256.1k     2.2M  0.885      2.0M
[FairProgression]  13-20      145     1.8M    116    578.2k     2.4M  0.754      1.8M
[FairProgression]  21-32      151     3.2M    193      2.1M     5.3M  0.603      3.2M
[FairProgression]  33-42       55     1.7M    137      3.3M     5.0M  0.334      1.7M
[FairProgression]  43+         40     2.4M    371     18.4M    20.8M    ---     20.8M
[FairProgression]
[FairProgression]  Buffed 185 under-rewarded quests (+369.0k XP)
[FairProgression]
[FairProgression]  Total quest XP: 35.6M -> 29.3M (-17.5%)
[FairProgression] ================================================================
```

## How It Works

### Trader-Aligned Level Brackets

By default (`"level_brackets": "trader"`), brackets are auto-computed from vanilla trader loyalty level requirements:

| Bracket | Phase | Trader unlocks |
|---|---|---|
| 1-12 | Early game | Before any LL2 |
| 13-20 | LL2 phase | Therapist 13, Peacekeeper 14, Prapor/Skier/Jaeger 15, Ragman 17, Mechanic 20 |
| 21-32 | LL3 phase | Jaeger 22, Peacekeeper/Therapist 23-24, Prapor 26, Skier 28, Mechanic 30, Ragman 32 |
| 33-42 | LL4 phase | Jaeger 33, Therapist/Prapor/Skier 35-38, Mechanic 40, Ragman 42 |
| 43+ | Endgame | All traders LL4 — no scaling applied |

### Quest Level Estimation

Each quest's "estimated completion level" is the **maximum** of four signals:

| Signal | Description |
|---|---|
| **XP reward** | Mapped against vanilla quest XP medians per level |
| **Difficulty score** | Analyzed from quest objectives (kill count, target type, restrictions, map difficulty, etc.) |
| **Prerequisite propagation** | Each quest >= max(prereq levels) + 1, applied to vanilla and modded quests |
| **Parallel chain boost** | Cumulative XP from parallel chains within the same trader, converted to a level floor via the exp table |

### Difficulty Scoring

Quest objectives are scored by analyzing the `AvailableForFinish` conditions:

- **Kill scavs**: 3 + sqrt(count) x 2 (easy)
- **Kill PMCs**: 7 + sqrt(count) x 2 (moderate)
- **Kill bosses**: 18 + sqrt(count) x 2 (hard)
- **Kill restrictions** (headshot, distance, weapon, equipment, night): multiplier +10-25% each
- **Map difficulty**: Labs +50%, Lighthouse/Labyrinth +30%
- **Zone restriction**: +20% (may require keys)
- **One raid (OneSessionOnly)**: +15-100% scaled by base difficulty (kill 20 PMC headshots >> visit 6 zones)
- **Handover items**: 3 + count x 1.5 (FIR: x1.4), money handovers = trivial
- **Hideout area**: 8/18/28 for level 1/2/3+
- **Trader loyalty**: 5/12/25/38 for LL1/2/3/4
- **Skill level**: 5-30 depending on level
- **QuestsExtended placeholders** (dist >= 5555m): detected and ignored
- **FindItem + HandoverItem** in same quest: counted once, not twice
- Multiple conditions: highest score + 15% of each additional (capped at 50% of highest)

### Under-Rewarded Buff

When a quest's difficulty-estimated level exceeds its XP-estimated level by more than the threshold (default: 5 levels), the XP is increased to the vanilla median for the midpoint level. The buff is capped at x2 the original XP to avoid extreme jumps. Buffs are applied **before** bracket computation so they are included in the XP budget.

### Safe for Existing Profiles

- Only modifies quest XP rewards in memory (not on disk)
- Already-completed quests are unaffected
- Adding/removing quest mods: just restart the server, coefficients recalculate automatically

## Compatibility

- **SPT 4.1.6** (.NET 10.0); declares compatibility with 4.1.6 and later 4.1.x patches
- Compatible with any mod that adds quests (TarkovTradingCards, custom traders, quest overhauls, etc.)
- Detects QuestsExtended placeholder conditions (dist >= 5555m) and ignores them
- Rebalances during `PostLoad` at priority `PostLoad + 999`; quest mods must add their quests before this point

## License

MIT
