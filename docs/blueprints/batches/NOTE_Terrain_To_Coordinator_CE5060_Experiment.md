<!--STATUS
state: LIVE — a NOTE (protocol §5) asking for a REVIEW and a MERGE · updated: 2026-11-26 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Load_GeoReference_From_Nodes.md — the design (v2 + §8 the independent review + §9 the experiment's as-built and measurements)
  - ../../DESIGN_Terrain_Import_UTM.md — §13.2 (import once per machine; CE-5060 / CE-11061)
  - ../../DESIGN_Cluster_Load_Phase.md — §12 (LoadProgress, the heartbeats)
  - ../../DESIGN_Geo_Origin.md — §4 ⑨ ⑩ (the origin set ahead; the master publishes)
-->
# NOTE — `terrain` → `coordinator`: please review and merge the CE-5060 experiment (no terrain import on the master; a first-use import keeps its node alive)

| | |
|---|---|
| **branch** | ⭐ **`exp/ce5060-origin-peek`** — 5 commits on top of `terrain@2f7a14901` (`7bb5dbc3e` · `c8f701668` · `1668374d3` · `3a1b06358` · `4d1ba2c87`); ⛔ NOT merged into `terrain` |
| **user** | *"try this yourself, as experiment, testing thoroughly in both single editor and cluster modes first. i do not want to break anything for other lanes"* · *"pls write note for coordinator, it will review it and will merge it if all ok"* |
| **what it fixes** | [CE-5060](../Blueprint_Issues_Tracker.md) (a first-use terrain import fails the load) — `stechovice-battle` on a machine with no import failed the director's 30 s stall check; it now loads (195.6 s import) |
| **design** | [DESIGN_Load_GeoReference_From_Nodes.md](../../DESIGN_Load_GeoReference_From_Nodes.md) — §2 the decision, §8 an independent review (v1 found unsound, rewritten), ⭐ §9 the as-built and every measurement |

## What changed — and which lanes' files it touches

| file | lane | the change |
|---|---|---|
| ⚠ `FDP/Engine/Fdp.Core/LoadProgress.cs` | **shared (every lane)** | `ReportDetail` also ADVANCES the count (with its call site, as `Report` does). 📐 Measured: a detail-only import stage (road levelling, 12 s) got the node EJECTED 8 s in — both heartbeat sources beat only while the count moves. One place fixes every detail-only loader |
| ⚠ `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.GeoReference.cs` | **backend** | one line: `NoteLoadGeoReference` reads through `PeekDefinition` (never imports) instead of `ReadDefinition`. 📐 Measured: this read was the load's FIRST terrain resolve and ran the whole import on the master's frame before any load step existed (CE-5060) |
| `FDP/Toolkits/Fdp.Toolkits.Navigation.Recast/NavTilePack.cs` | terrain / navmesh | `Pack` reports per tile read / pack written / tile and folder removed. 📐 A gap probe on the real import found 16 s silent here (~60 000 tiles) |
| `FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainCatalog.cs`, `TerrainSources.cs` | terrain | `TerrainCatalog.PeekDefinition` (import-free) + `ITerrainSourceImport.TryReadInputDefinition` (a default method — an implementer that lacks it keeps today's behaviour) |
| `Hrot/Engine/Hrot.Terrain.Import/SourceImport.cs`, `TerrainFolderWriter.cs` | terrain | the origin from the input set's `manifest.json` through `TerrainFolderWriter.OriginOf` — the ONE rule the writer also uses; the import-lock wait reports |
| `Hrot/Engine/Hrot.Core/Services/TerrainResidency.cs` | terrain | one line: `ApplyOriginAhead` reads through `PeekDefinition` |

## Gates (the report contract)

| # | gate (verbatim) | `--no-build` | result | vs base `terrain@2f7a14901` |
|---|---|---|---|---|
| 1 | `dotnet test Hrot/Engine/Hrot.Terrain.Import.Tests --filter SourceImportTests` | after its own build | **24 / 0 / 0** | +4 rails (`CE5060_*`) |
| 2 | `dotnet test FDP/Engine/Fdp.Core.Tests --filter LoadProgressTests` | after its own build *(⚠ restored first: it had never been restored on this VM)* | **7 / 0 / 0** | +1 rail |
| 3 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "RecastNavmeshFactoryTests\|TerrainBakeTests\|OverlayCompilerTests"` | after its own build | **59 / 0 / 0** | +1 rail |
| 4 | `… Fdp.Toolkits.Tests --filter "Terrain.TerrainSources\|TerrainCatalog"` | yes | **9 / 0 / 0** | = |
| 5 | `dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests` (WHOLE suite; the AQ107C master rails included) | yes | **483 / 0 / 0** | = |
| 6 | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests` (WHOLE suite) | yes | 3327 / **16** / 6 | ⭐ **10 of the 16 are red on the base too** (pre-existing: `CgfLogicPackTests` ×3, `SimHostCoreLogicPack_EmptyWorld`, `UrbanCombatCatalogRails.CreateTkb…`, `BehaviourHostingSnapshotTests.CE1431`, `StrictNetworkSeparationTests.CE4208`, `UtmEverywhereTests.TheEightCommittedTerrains…`, `ShippedTreesAreCSharpTests(BasicSquadSop)`, `SopParamsRoundTripTests.CE2079`); the other 6 (`EpisodeLoadClusterOpHandlerTests` ×5, `LoadPhaseChainTests.CE3345`) are **green 3× in isolation on the branch** — they went red only while a branch switch + build ran beside the suite |
| 7 | every new rail red-proved by a COMPILING mutation | — | 6 / 6 red under their mutation, green restored | `PeekDefinition` through `ResolveDefinition` · `ReportDetail` without the advance (3 rails) · the lock wait without its report · `Pack`'s read loop without its report |
| 8 | ⭐ the INTEGRATION shapes (row 8 of the contract — this changes the load and the heartbeats) | — | see the live table | — |
| 9 | the working tree clean after every run | — | ✅ | — |
| 10 | `tracker-counts.py --check` | — | ✅ (no ids allocated by the experiment) | — |

## Live — first-use imports (the terrain absent from every root before each run)

| mode | terrain | result |
|---|---|---|
| editor (`scripts/scenario-regression.sh`) | `village-street` (20.8 s import) | load ✅; longest silence 1.1 s; GeoReference = the import's origin to the last digit; 2 REALISM rows red — ⭐ the SAME 2 on the base (pre-existing, not filed by me) |
| editor | ⭐ `Stechovice` (195.6 s import) | ⭐ **load ✅ — it failed the stall check before (CE-5060)**; no ejection; longest silence 1.7 s; the origin set ahead of the bake from the input set before the import |
| cluster `--mode all` (`TIER=cluster`) | `village-street` | ONE import (13.4 s), three nodes waited on its lock reporting (≤ 1.3 s silence); same origin on all four; no ejection |
| cluster, MULTI-PROCESS (orchestrator + SimHost + CGF + Traffic, own staging root, DDS domain 47) | `village-street` | SimHost imported (28 s), CGF + Traffic waited (≤ 1.8 s silence); no ejection; ⭐ the orchestrator never imported; GeoReference = the import's origin exactly |
| ⚠ not run | Stechovice in a cluster | the user asked to keep the VM light: *"test the principles … on some small terrain"* — village-street's import is longer than the 5 s ejection window, so every principle is exercised |

## What to look at in review

| # | the question | where |
|---|---|---|
| ① | ⭐ the `LoadProgress.ReportDetail` semantics change — any caller that reports a detail while NOT advancing (a stuck loop re-reporting the same value) would now keep its node alive. 📐 Measured callers: the navmesh baker (per tile), the road levelling (per corridor), the import stages, the lock wait, the packing — all inside real work | `LoadProgress.cs`; `grep -rn ReportDetail` |
| ② | the master's read is now the INPUT SET's origin when the terrain was imported from one: a stale or missing import publishes what the import is about to write (by construction the same — `TerrainFolderWriter.OriginOf`); a hand-made terrain keeps its own file | `TerrainCatalog.PeekDefinition`, `SourceImport.TryReadInputDefinition` |
| ③ | ⏳ NOT built: §5 P3 — a cancelled load does not cancel its import (`ResolveDefinition` passes `default`) | design §5 |
| ④ | ⏳ still open elsewhere: CE-5063 (the master can still import at save / recompile / new scenario — backend) | tracker |

⭐ If it is all OK: `git merge origin/exp/ce5060-origin-peek` into your branch; CE-5060 can then be closed (its progress half — the stale-hit half was CE-11061, already on `terrain`). If not, reply with a NOTE to terrain and I fix it on the branch.
