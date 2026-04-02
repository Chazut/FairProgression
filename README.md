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
3. **Scales down** over-rewarded quests: assigns quests to level brackets and computes a scaling coefficient per bracket so the total quest XP matches the vanilla budget
4. **Buffs up** under-rewarded quests: when a quest's objectives are significantly harder than its XP reward suggests, increases the XP to match the vanilla median for that difficulty level
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

  // Buff under-rewarded quests (difficulty >> XP reward)
  "buff_under_rewarded": true,

  // Minimum level gap to trigger a buff
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
[FairProgression]  Vanilla quest XP: 11.0M -> 8.3M (-24.3%)
[FairProgression]  Modded  quest XP: 24.3M -> 19.7M (-18.6%)
[FairProgression]
[FairProgression]  Bracket  Van.Q   Van.XP  Mod.Q   Mod.XP    Total  Coeff     After
[FairProgression]  -------  -----   ------  -----   ------    -----  -----     -----
[FairProgression]  1-5        347     6.1M     98    101.2k     6.2M  0.984      6.1M
[FairProgression]  6-10        27   136.7k     94    240.9k   377.6k  0.362    136.7k
[FairProgression]  11-15       52   402.2k    171    820.2k     1.2M  0.329    402.2k
[FairProgression]  16-20       33   400.2k     85    421.5k   821.7k  0.487    400.2k
[FairProgression]  21-25       33   547.0k     97    834.9k     1.4M  0.396    547.0k
[FairProgression]  26-30       23   489.6k    114      1.6M     2.1M  0.250    530.1k
[FairProgression]  31-40       25   683.4k    136      3.8M     4.5M  0.250      1.1M
[FairProgression]  41-50       11     1.3M     31      2.0M     3.3M  0.386      1.3M
[FairProgression]  51+          7   950.5k    108     14.3M    15.3M    ---     15.3M
[FairProgression]
[FairProgression]  Buffed 238 under-rewarded quests (+1.5M XP)
[FairProgression]
[FairProgression]  Total quest XP: 35.2M -> 28.1M (-20.3%)
[FairProgression] ================================================================
```

## How It Works

### Quest Level Estimation

Each quest's "estimated completion level" is the **maximum** of four signals:

| Signal | Description |
|---|---|
| **XP reward** | Mapped against vanilla quest XP medians per level |
| **Difficulty score** | Analyzed from quest objectives (kill count, target type, restrictions, hideout requirements, etc.) |
| **Prerequisite propagation** | Each quest >= max(prereq levels) + 1 |
| **Parallel chain boost** | Cumulative XP from parallel chains within the same trader, converted to a level floor via the exp table |

### Difficulty Scoring

Quest objectives are scored by analyzing the `AvailableForFinish` conditions:

- **Kill scavs**: 3 + sqrt(count) x 2 (easy)
- **Kill PMCs**: 7 + sqrt(count) x 2 (moderate)
- **Kill bosses**: 18 + sqrt(count) x 2 (hard)
- **Kill restrictions** (headshot, distance, weapon, map, night): multiplier +10-25% each
- **Handover items**: 3 + count x 1.5 (FIR: x1.4), money handovers = trivial
- **Hideout area**: 8/18/28 for level 1/2/3+
- **Trader loyalty**: 5/12/25/38 for LL1/2/3/4
- **Skill level**: 5-30 depending on level
- **QuestsExtended placeholders** (dist >= 5555m): detected and ignored
- **FindItem + HandoverItem** in same quest: counted once, not twice
- Multiple conditions: highest score + 15% of each additional (capped at 50% of highest)

### Under-Rewarded Buff

When a quest's difficulty-estimated level exceeds its XP-estimated level by more than the threshold (default: 5 levels), the XP is increased to the vanilla median for the difficulty level. This ensures hard quests are properly rewarded even if the modder set a low XP value.

### Safe for Existing Profiles

- Only modifies quest XP rewards in memory (not on disk)
- Already-completed quests are unaffected
- Adding/removing quest mods: just restart the server, coefficients recalculate automatically

## Compatibility

- **SPT 4.0** (.NET 9.0)
- Compatible with any mod that adds quests (TarkovTradingCards, custom traders, quest overhauls, etc.)
- Detects QuestsExtended placeholder conditions (dist >= 5555m) and ignores them
- Must load **after** all quest mods — uses `PostSptModLoader` phase (priority 1.1M+)

## License

MIT
