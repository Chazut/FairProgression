# FairProgression

<img src="branding/fair-progression.png" alt="FairProgression logo" width="84" />
<p align="center">
  <img src="https://img.shields.io/github/stars/Chazut/FairProgression?style=flat-square&label=STARS&color=007ec6" />
  <img src="https://img.shields.io/github/issues/Chazut/FairProgression?style=flat-square&label=ISSUES&color=44cc11" />
  <img src="https://img.shields.io/github/downloads/Chazut/FairProgression/total?style=flat-square&label=DOWNLOADS&color=44cc11" />
</p>

Custom traders and quest packs can push your PMC through the early levels before you've earned the trader loyalty to go with them. FairProgression adjusts quest completion XP for each stage of the wipe. The default aims for the vanilla pace, and a slider lets you keep as much of the extra quest XP as you like.

## Installation

1. Download the [release ZIP](https://github.com/Chazut/FairProgression/releases) for **SPT 4.1.6** and extract it next to `EscapeFromTarkov.exe`. The mod runs on the server.
2. Start the SPT server. FairProgression automatically picks up quests from your installed mods.
3. To change the defaults, open **FairProgression** in SPT's web menu. Its page is also available at `/fairprogression` on your server.

Existing profiles can use FairProgression mid-wipe. It adjusts future quest payouts in memory. XP your PMC has already earned stays on the profile. When upgrading, keep your copy of `SPT_Runtime/user/mods/FairProgression/config/config.jsonc`. For SPT 4.0.13, use the [1.0.0 release](https://github.com/Chazut/FairProgression/releases/tag/1.0.0).

## Choose your progression pace

**Keep extra quest XP** sets how much XP quest packs add to the vanilla total in each level range. Suppose the vanilla quests in one range offer 100,000 XP and a custom trader adds another 80,000 XP.

| Slider | Extra XP kept | Target for that level range |
| --- | ---: | ---: |
| 0% (default) | 0 | 100,000 XP |
| 25% | 20,000 | 120,000 XP |
| 100% | 80,000 | 180,000 XP |

Open a level range in the preview and select a trader to see each quest's original and final reward. By default, vanilla and modded quests share the adjustment. Select **Advanced settings → Quests to rebalance → Modded quests only** to protect vanilla payouts. In the 25% example, vanilla quests would still pay 100,000 XP and the custom trader's quests would share 20,000 XP.

**Minimum reward** puts a floor under each adjustment. For example, raising it to 50% in that same scenario gives the custom trader's quests at least 40,000 XP, bringing the actual total to 140,000 XP. The preview shows both the target and the result. Choose **Save settings** and restart the server to apply your changes.

If you're upgrading an older config that already protected vanilla rewards, it keeps its previous calculation until you move the slider or select **Use the displayed budget** and save.

## How quests are balanced

- **Level ranges** follow vanilla trader loyalty requirements by default. You can enter your own upper limits in the editor. Quests estimated above the last range keep their original XP.
- **Quest placement** uses level requirements and prerequisite chains. When a modded quest has no minimum level above 1, its XP and objectives help estimate when you're likely to finish it. Parallel quest chains from the same trader can raise that estimate. A boss kill with weapon restrictions can place a quest later than a simple item handover.
- **Difficulty boosts** give under-rewarded modded quests a larger share of the budget. By default, a gap of at least five levels between estimated difficulty and XP value qualifies. The boost is capped at twice the original XP before the level range is balanced.

On startup, FairProgression uses the quest IDs in SPT's vanilla database to identify added quests. It balances the quests loaded by `PostLoad + 999`. Restart the server after adding or removing a quest pack to recalculate the rewards.

## Configuration and diagnostics

The web editor and the [JSONC config](config/config.jsonc) use the same settings. For troubleshooting, enable **Dry run** to calculate rewards at startup while the game uses its original payouts. The **XP summary** shows totals per level range in the server log. Turn on quest details to inspect individual adjustments.

## Build and test

From the repository root, use the .NET 10 SDK and point `SPT_DIR` at your SPT 4.1.6 server runtime.

```powershell
$env:SPT_DIR = "C:/Games/SPT-4.1/SPT_Runtime"
dotnet build csharp/FairProgression/FairProgression.csproj -c Release
dotnet run --project tests/ProgressionChecks/ProgressionChecks.csproj -c Release
```

The build writes the DLL and default config to `csharp/FairProgression/bin/Release/net10.0/`.

[Release notes](RELEASE_NOTES_v1.2.0.md) · [Report an issue](https://github.com/Chazut/FairProgression/issues) · [MIT license](LICENSE)
