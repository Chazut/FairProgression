# FairProgression

An SPT 4.0 server mod that automatically rebalances quest XP rewards to preserve the vanilla progression curve when modded quests are installed.

## The Problem

When you install mods that add quests (custom traders, quest overhauls, etc.), the extra XP from those quests causes you to level up faster than intended. This creates a mismatch: your level is high enough for better gear, but you haven't progressed through the vanilla trader quests to actually unlock it.

## The Solution

FairProgression runs **after** all other mods have loaded and:

1. **Identifies** which quests are vanilla and which are modded (by comparing the in-memory database against the on-disk vanilla quest file)
2. **Estimates** at which level each quest will realistically be completed, using:
   - XP reward amount mapped against vanilla quest distributions
   - Objective difficulty analysis (kill targets, boss fights, hideout requirements, etc.)
   - Prerequisite chain depth propagation
   - Parallel chain XP accumulation (e.g., 20 quest themes played simultaneously)
3. **Assigns** quests to level brackets and computes a scaling coefficient per bracket so the total quest XP matches the vanilla budget
4. **Scales** quest XP rewards accordingly
5. **Logs** a detailed dashboard of all changes

## Installation

1. Build with `dotnet build csharp/FairProgression/FairProgression.csproj -c Release`
2. Copy `FairProgression.dll` and the `config/` folder to `SPT/user/mods/FairProgression/`
3. Start the SPT server — FairProgression runs automatically

Or just drop the release zip into your `user/mods/` folder.

## Configuration

Edit `config/config.jsonc`:

```jsonc
{
  // "scale_all" — reduce ALL quest XP proportionally (vanilla + modded)
  // "scale_modded_only" — only reduce modded quest XP
  "mode": "scale_all",

  // Level brackets for budget calculation
  "level_brackets": [5, 10, 15, 20, 25, 30, 40, 50],

  // Minimum scaling coefficient (never reduce below this ratio)
  "min_coefficient": 0.25,

  // Print detailed dashboard in server logs
  "enable_dashboard": true,

  // Calculate and log without modifying anything
  "dry_run": false,

  // Log every modded quest with its difficulty score and bracket
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
[FairProgression]  Modded quest XP: 24.3M (68.9% of total)
[FairProgression]
[FairProgression]  Bracket  Van.Q   Van.XP  Mod.Q   Mod.XP    Total  Coeff     After
[FairProgression]  -------  -----   ------  -----   ------    -----  -----     -----
[FairProgression]  1-5        218     1.2M     98    320.5k     1.5M  0.789      1.2M
[FairProgression]  6-10       156     5.0M    201      1.1M     6.1M  0.820      5.0M
[FairProgression]  11-15       42    382.4k     44    367.7k   750.1k  0.510    382.4k
[FairProgression]  ...
[FairProgression]  51+          7    950.5k     88     13.3M    14.3M    ---     14.3M
[FairProgression]
[FairProgression]  Total quest XP: 35.2M -> 24.8M (70.5%)
[FairProgression] ================================================================
```

## How It Works

### Quest Level Estimation

Each quest's "estimated completion level" is the **maximum** of four signals:

| Signal | Description |
|---|---|
| **XP reward** | Mapped against vanilla quest XP medians per level |
| **Difficulty score** | Analyzed from quest objectives (kill count, target type, restrictions, hideout requirements, etc.) |
| **Prerequisite propagation** | Each quest ≥ max(prereq levels) + 1 |
| **Parallel chain boost** | Cumulative XP from parallel chains within the same trader, converted to a level floor |

### Difficulty Scoring

Quest objectives are scored by analyzing the `AvailableForFinish` conditions:

- **Kill scavs**: 3 + √count × 2 (easy)
- **Kill PMCs**: 7 + √count × 2 (moderate)
- **Kill bosses**: 18 + √count × 2 (hard)
- **Kill restrictions** (headshot, distance, weapon, map, night): multiplier +10-25% each
- **Handover items**: 3 + count × 1.5 (FIR: ×1.4), money handovers = trivial
- **Hideout area**: 8/18/28 for level 1/2/3+
- **Trader loyalty**: 5/15/28/40 for LL1/2/3/4
- **Skill level**: 5-30 depending on level
- Multiple conditions: highest score + 20% of each additional

### Safe for Existing Profiles

- Only modifies quest XP rewards in memory (not on disk)
- Already-completed quests are unaffected
- Adding/removing quest mods: just restart the server, coefficients recalculate automatically

## Compatibility

- **SPT 4.0** (.NET 9.0)
- Compatible with any mod that adds quests (TarkovTradingCards, custom traders, quest overhauls, etc.)
- Must load **after** all quest mods — uses `PostSptModLoader` phase (priority 1.1M+)

## License

MIT
