# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## What This Is

An SPT 4.1 server mod that automatically rebalances quest XP rewards to preserve the vanilla progression curve when modded quests are installed. Prevents over-leveling from modded quest XP while keeping the vanilla experience intact.

## Build Commands

```bash
# Requires SPT_DIR environment variable pointing to SPT 4.0 server root
# e.g., export SPT_DIR="C:/Games/SPT-4.0/SPT"

# Build the mod
dotnet build csharp/FairProgression/FairProgression.csproj -c Release
```

SPT assemblies are referenced via `SPT_DIR` HintPath — the build will fail if the env var is unset or the assemblies are missing.

## Architecture

**Framework:** .NET 9.0 targeting SPT 4.0's DI system. Classes use `[Injectable(TypePriority = ...)]` for auto-discovery and ordering.

**Lifecycle (two stages):**
1. `CaptureVanilla` (priority: `OnLoadOrder.Preload + 5`) — captures vanilla quest IDs before any mod adds quests
2. `PostDb` (priority: `OnLoadOrder.PostLoad + 999`) — runs AFTER all other mods, analyzes quests, computes scaling, applies changes

**Services:**
- **VanillaQuestSnapshot** — static set of vanilla quest IDs captured before mods load
- **QuestAnalyzer** — classifies quests (vanilla/modded), computes prereq depth, estimates completion level
- **XPBudgetCalculator** — divides quests into level brackets and computes scaling coefficients
- **XPScaler** — applies coefficients to quest XP rewards in the database
- **Dashboard** — formatted log output of all rebalancing decisions
- **ConfigLoader** — JSONC config parser

**Configuration** is in `config/config.jsonc` — supports two modes: `scale_all` (proportional) and `scale_modded_only`.

## Code Style

- `.editorconfig`: UTF-8, CRLF, 4-space indentation for C#
- Nullable reference types enabled, implicit usings enabled
- No automated test suite
