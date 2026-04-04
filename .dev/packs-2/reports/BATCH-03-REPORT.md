# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2026-04-04  
**Status:** ✅ Complete

---

## Task Completion Table

| Task | Status | Notes |
|------|--------|-------|
| PACK2-P002 | ✅ Complete | Both packs implemented and tested |
| PACK2-E001 | ✅ Complete | Scaffolded; 0 CycloneDDS/Hrot.NED direct refs |
| DEBT-05 | ✅ Complete | 3 standalone unit tests (2 Spawn + 1 Destroy) |

---

## Files Created / Modified

| File | Change |
|------|--------|
| `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` | New |
| `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` | New |
| `Hrot.SimHost.Tests/ActuatorIntentsEgressPackTests.cs` | New — 4 tests |
| `Hrot.Map.Common.Tests/Replication/Egress/EntityStatesIngressPackTests.cs` | New — 2 tests |
| `Hrot.Map.Common.Tests/Replication/Egress/SpawnEntityCommandEgressTranslatorTests.cs` | New — 2 tests |
| `Hrot.Map.Common.Tests/Replication/Egress/DestroyEntityCommandEgressTranslatorTests.cs` | New — 1 test |
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | New |
| `Hrot.ScenarioEditor/ScenarioEditorModule.cs` | New |
| `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` | New |
| `Hrot.ScenarioEditor.Tests/ScenarioEditorModuleTests.cs` | New — 2 tests |
| `IOS-IG-SimHost.sln` | Added `Hrot.ScenarioEditor` + `Hrot.ScenarioEditor.Tests` |
| `Hrot.Map.Common/Hrot.Map.Common.csproj` | Added `InternalsVisibleTo: Hrot.SimHost.Tests` |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/CycloneTranslator.cs` | Made `participant` nullable; added null-guard in `PollIngress` |
| `Hrot.Map.Common/Replication/Ingress/GeoSpatialIngressTranslator.cs` | Changed constructor to accept `DdsParticipant?` |
| `Hrot.Map.Common/Replication/Ingress/EntityDamageIngressTranslator.cs` | Changed constructor to accept `DdsParticipant?` |

---

## Test Results

### Build
- `dotnet build IOS-IG-SimHost.sln --no-incremental` → **Build succeeded. 0 Error(s)** ✅

### Unit Tests

| Suite | Total | Passed | Failed | Notes |
|-------|-------|--------|--------|-------|
| `Hrot.Map.Common.Tests` | 99 | 99 | 0 | Includes 5 new tests (DEBT-05 x3, EntityStatesIngressPack x2) |
| `Hrot.SimHost.Tests` | 440 | 439 | 1 | 1 pre-existing failure (see below) |
| `Hrot.ScenarioEditor.Tests` | 2 | 2 | 0 | New project — all pass |

### Integration Tests

| Suite | Total | Passed | Failed | Notes |
|-------|-------|--------|--------|-------|
| `Hrot.ClusterRunner.Integration.Tests` | 49 | 46 | 3 | All 3 failures are pre-existing (from BATCH-02) |

### Pre-existing Failures (not introduced by this batch)

| Test | Project | Reason |
|------|---------|--------|
| `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` | `Hrot.SimHost.Tests` | Reads from DDS topic "GeoSpatial" but `GeoSpatialEgressTranslator` tombstones "WorldPos"; test was already broken before this batch |
| `ClusterOpE2eScriptTests.LiveFromReplayBranch_Passes` | `Hrot.ClusterRunner.Integration.Tests` | Replay infrastructure issue; confirmed pre-existing in BATCH-02 report |
| `ClusterOpE2eScriptTests.PreviewStateRestore_Passes` | `Hrot.ClusterRunner.Integration.Tests` | Flaky timing-sensitive replay test; confirmed pre-existing in BATCH-02 report |
| `MiniExConIntegrationTests.MiniExConSpawnWithWanderMission_...` | `Hrot.ClusterRunner.Integration.Tests` | WanderMilitary mission task assignment issue; confirmed pre-existing in BATCH-02 report |

---

## Answers to Q1–Q4

**Q1:** `CycloneNetworkIngressSystem` (used by `EntityStatesIngressPack`) is defined inside `CycloneNetworkModule.cs`. Did you use it directly, or did you use `CycloneIngressSystem` from `CycloneIngressSystem.cs`?

Used `CycloneNetworkIngressSystem` directly from `ModuleHost.Network.Cyclone.Modules` as instructed. This is a local class defined at the bottom of `CycloneNetworkModule.cs` and is what the instructions explicitly referenced. There is no separate `CycloneIngressSystem.cs` file in this codebase — all ingress system logic for the Cyclone module is contained in `CycloneNetworkIngressSystem` inside `CycloneNetworkModule.cs`. This class processes `PollIngress` on all translators in `SystemPhase.Input`.

**Q2:** Did `ActuatorIntentsEgressPack` need any new project references in `Hrot.SimHost.csproj`?

No new references were needed. `Hrot.SimHost.csproj` already references `ModuleHost.Network.Cyclone` (for `CycloneEgressSystem`), `Hrot.Map.Common` (for `SpawnEntityCommandEgressTranslator`, `UpdateEntityCommandEgressTranslator`, `DestroyEntityCommandEgressTranslator`), `CycloneDDS.Runtime` transitively, and `Fdp.Toolkit.Geographic` (for `IGeographicTransform`). All five egress translators and the `CycloneEgressSystem` were already reachable.

**Q3:** Did `FDP.Toolkit.Vis2D` introduce any transitive references to `Hrot.NED` or `CycloneDDS`?

No. Inspection of `FDP.Toolkit.Vis2D.csproj` shows it only references `Fdp.Kernel`, `ModuleHost.Core`, `Raylib-cs`, and `ImGui.NET`. No `CycloneDDS` or `Hrot.NED` are pulled in by this toolkit. Running `dotnet list Hrot.ScenarioEditor package` confirms "No packages were found" — no CycloneDDS NuGet package is present as a direct or transitive *NuGet* reference. The `FDP.Toolkit.NetworkSpawning` project reference does bring in `ModuleHost.Network.Cyclone` (a project, not a NuGet package), which builds CycloneDDS from source, but this doesn't appear as a NuGet package reference.

**Q4:** Did you encounter any issues with `null` participant in `EntityStatesIngressPack` unit tests?

Yes. `GeoSpatialIngressTranslator` and `EntityDamageIngressTranslator` both extend `CycloneTranslator<TDds, TView>`, which takes a non-nullable `DdsParticipant` and immediately creates `new DdsReader<TDds>(participant)` — throwing `NullReferenceException` when null is passed. Resolution: modified `CycloneTranslator`'s constructor parameter to `DdsParticipant?`, changed `Reader`/`Writer` creation to only instantiate when participant is non-null, and added a null-guard in `PollIngress` (matching the pattern already used in `MapRouteIngressTranslator` and `EntityInfoIngressTranslator`). Also updated `GeoSpatialIngressTranslator` and `EntityDamageIngressTranslator` to accept `DdsParticipant?` in their constructors.

---

## Suggested Git Commit Message

```
feat(pack): PACK2-P002 ActuatorIntentsEgressPack + EntityStatesIngressPack composites

- Add ActuatorIntentsEgressPack (Hrot.SimHost/Translators/) bundling 5 egress translators
  (NavigationIntent, WeaponFireIntent, SpawnEntityCommand, UpdateEntityCommand,
  DestroyEntityCommand) under a single CycloneEgressSystem registration.
- Add EntityStatesIngressPack (Hrot.Map.Common/Translators/) bundling 6 ingress translators
  (EntityMaster, GeoSpatial, EntityInfo, MapVisualOverlay, MapRoute, EntityDamage)
  under a single CycloneNetworkIngressSystem registration.
- Scaffold Hrot.ScenarioEditor project (stub ScenarioEditorModule, network-agnostic,
  no CycloneDDS/Hrot.NED direct references) — PACK2-E001.
- Add standalone unit tests for SpawnEntityCommandEgressTranslator and
  DestroyEntityCommandEgressTranslator (DEBT-05).
- Fix: make CycloneTranslator.participant nullable so translators can be instantiated
  without a live DDS participant in unit-test mode.

Tests: 99/99 Map.Common.Tests, 439/440 SimHost.Tests (1 pre-existing),
       2/2 ScenarioEditor.Tests, 46/49 Integration (3 pre-existing).
```
