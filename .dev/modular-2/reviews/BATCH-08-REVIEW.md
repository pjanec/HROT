# BATCH-08 Review: Decouple Subsystems from NED + Fix Pre-existing Test Failures

**Date:** 2026-04-12
**Batch:** BATCH-08
**Tasks:** DEBT-001, DEBT-005, TASK-P4-001, TASK-P4-002 (blocked), TASK-P4-003 (blocked)
**Status:** PARTIAL APPROVAL — Accepted work committed; P4-002 and P4-003 deferred to BATCH-09

---

## Summary

DEBT-001 (all routing guard and EntityInfo failures) and TASK-P4-001 (ExCon fully decoupled
from NED) are complete and verified. DEBT-005 (TimeConfig default) was reported as done by the
developer but was NOT actually applied to the source file — the fix was applied by the dev-lead
during review. TASK-P4-002 and TASK-P4-003 were legitimately blocked on an architectural gap in
the neutral command DTO design.

---

## Verification

### Build
- `dotnet build IOS-IG-SimHost.sln --no-incremental -v quiet` — Build succeeded (0 errors, ~12 warnings all pre-existing).

### Test Results

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.SimHost.Tests | 451 | 0 | Was 24 failures (DEBT-001 fixed) |
| Hrot.IG.Tests | 421 | 0 | Was 7 failures (DEBT-001 fixed) |
| Hrot.ClusterRunner.Tests | 211 | 0 | Was 5 failures (DEBT-001 fixed) |
| Hrot.ExCon.Tests | 325 | 0 | TASK-P4-001 complete (DDS crash-on-exit is native teardown, not a logic failure) |
| Fdp.Engine.Tests | 729 | 0 | Was 3 failures (DEBT-005 fixed + SpatialHash isolation fix) |

### DEBT-005 Fix Issue

The developer reported fixing `TimeConfig.SyncRefreshIntervalTicks` default from `Stopwatch.Frequency * 60`
to `Stopwatch.Frequency` in `FDP/Toolkits/Fdp.Engine/Toolkits/Time/Controllers/TimeConfig.cs`.
**The change was NOT present in the source file during review.** The dev-lead applied the fix manually
during post-review correction. The comment in the source file was also updated from "Default: 60 second"
to "Default: 1 second".

Additionally, the `SpatialHash` test was failing due to a `ComponentTypeRegistry` global-state collision
when running in the full parallel suite (the test stub type clashed with the real `PhysicsCollider`).
Fixed by:
1. Replacing `PhysicsCollidableStub` with the real `PhysicsCollider` type (now accessible since
   all Physics code is in `Fdp.Engine`).
2. Adding `xunit.runner.json` with `parallelizeTestCollections: false` to prevent parallel execution
   of tests that share global static `ComponentTypeRegistry` state.

### ExCon Decoupling Verification
- `Select-String -Path "Hrot.ExCon\Hrot.ExCon.csproj" -Pattern "Network.NED|Hrot.NED"` — zero matches.
- `NedExConEgressWriters`, `NedTranslationHelper` verified to exist in `Hrot.Network.NED/ExCon/`.
- `NedNetworkFactory.CreateExConEgressWriters()` and `CreateCommandGateway()` return real implementations.
- `ExConLogic` uses `IExConEgressWriters` and `ICommandGateway` throughout (no individual DDS writers).

---

## Issues Found

### P1 (Must fix next batch)

None. All P1 issues from DEBT-001 and DEBT-005 are resolved.

### P2 (Architecture gap — TASK-P4-002/003 blocked)

**DEBT-006: Neutral CreateEntityCommand too shallow for IG/SimHost descriptor richness**

`ICommandGateway.CreateEntityAsync(CreateEntityCommand)` carries only primitive fields
(`TkbType`, `Latitude`, `Longitude`, `Altitude`, `PropertiesJson`, `ForceId`). But:
- IG's `OrchestratePersonalRouteAsync` builds a `List<EntityDescriptorUnion>` with `MapRoute`
  waypoints, `EntityInfo` commanderId, `WorldPos` anchor position, and `EntityMaster` TkbType.
- SimHost's `CreateEntityRequestSystem` processes the full descriptor union list to install multiple
  ECS components per entity.
- SimHost's `ICreateEntityRequestSource` returns NED `CreateEntityRequest` from its callback contract.

Resolution requires extending `CreateEntityCommand` and the `INetworkFactory` boundary to carry
neutral descriptor types, not just primitives. See DEBT-006 for the full analysis.

### P3 (Technical debt noted from report)

**DEBT-007: DDS crash on exit in Hrot.ExCon.Tests**
When `DdsWriterAdapterTests` runs in the same process as other tests, the CycloneDDS native
library emits an `AccessViolationException` from its shutdown code. Tests pass; crash is from
process exit. Possible fix: xunit assembly fixture for DDS shutdown, or isolate DDS adapter
tests to a standalone project.

**DEBT-008: NedTranslationHelper.ToUpdateDescriptorRequest stub incomplete**
`SendUpdateDescriptorAsync` in `NedCommandGateway` only fills `EntityId` and `BaseVersion`,
completely ignoring `DescriptorJson`. IG's `SendGeoSpatialUpdate` maps a `WorldPos` descriptor
that will be silently dropped until this is implemented.

---

## Debt Tracker Updates

New entries added to DEBT-TRACKER.md:
- DEBT-006 (P2): Neutral CreateEntityCommand too shallow — blocks P4-002 and P4-003
- DEBT-007 (P3): DDS crash on exit in Hrot.ExCon.Tests
- DEBT-008 (P3): NedTranslationHelper.ToUpdateDescriptorRequest stub incomplete

Resolved:
- DEBT-001 ✅ (routing guard + EntityInfo failures — all fixed)
- DEBT-005 ✅ (TimeConfig default — fixed during review)

---

## Suggested Git Commit Message

```
BATCH-08: Fix pre-existing failures + decouple ExCon from NED

DEBT-001 (all fixed):
- CreateEntityRequestSystemTests: use LocalNodeId in Owner.AppInstanceId
- NedReplicationModule: remove SmartEgressSystem from pureIG registration block
- UniqueNameGeneratorTests: replace EntityInfo (NED DDS) with neutral EntityInfo struct
- ClusterRunner routing guard tests: matching AppInstanceId fix

DEBT-005 (fixed):
- TimeConfig.SyncRefreshIntervalTicks default: 60s -> 1s
- Fdp.Engine.Tests: add xunit.runner.json to prevent parallel component ID collision
- SpatialHashSystemTests: use real PhysicsCollider instead of stub (removes ID collision)

TASK-P4-001 (ExCon fully decoupled from NED):
- ExConLogic: uses IExConEgressWriters + ICommandGateway (no individual DDS writers)
- MissionEditorService: uses ICommandGateway (no FdpEventBus)
- NedExConEgressWriters: new NED implementation of IExConEgressWriters
- NedTranslationHelper: neutral DTO -> NED wire type translation helpers
- NedNetworkFactory: CreateExConEgressWriters() and CreateCommandGateway() wired
- Hrot.ExCon.csproj: zero NED project references
```
