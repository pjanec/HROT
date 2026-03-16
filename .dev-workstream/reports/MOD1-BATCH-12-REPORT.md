# MOD1-BATCH-12 Report

**Batch:** MOD1-BATCH-12  
**Developer:** GitHub Copilot  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| DB-MOD1-26 | ✅ Complete | Root cause: stale binaries. Full rebuild resolved both failing tests (31/31). See Q1. |
| DB-MOD1-02 | ✅ Complete | Reflection-based uniqueness tests added. No duplicates found in either class. See Q2. |
| DB-MOD1-23 | ✅ Complete | `FDP.Toolkit.Navigation.Contracts` created; `NavigationIntent`/`NavigationStatus` moved. IDs retained at 67/68. See Q4. |
| DB-MOD1-04 | ✅ Complete | Added `SetAuthority<SimTransform>` + `Vector3.Distance > 0.01f` assertion. Test fails without authority (before = after = (0,0,0)). |
| DB-MOD1-05 | ✅ Complete | Added `EntityQuery.IsEmpty` property + `if (q.IsEmpty) return;` guard in `HsmTickSystem.OnUpdate()`. |
| DB-MOD1-06 | ✅ Complete | `TrajectoryPoolManager` and `FormationTemplateManager` are now lazy (`??=`) in `GroundKinematicsModule`. |
| DB-MOD1-09 | ✅ Complete | `NodeConfiguration` is the unified survivor; `SimHostConfig` deleted. See Q3. Also fixed a config.json regression discovered during manual testing (see DB-MOD1-09 fix below). |
| DB-MOD1-12 | ✅ Complete | `MapCanvas(new RaylibInputProvider())` wrapped in `IgPresentationModule` in production path; headless path uses `IgPresentationModule(canvas: null)`. |
| DB-MOD1-18 | ✅ Complete | `Assert.False(seekTask.IsCompleted, ...)` added immediately after `SeekToFrameAsync` call, before `await`. |
| DB-MOD1-25 | ✅ Complete | Dirty-flag guard in `DamageSystem`: read existing `HealthData.Current` and only call `SetComponent` when value has changed. New test `HealthData_DirtyGuard_OnlyWritesWhenCurrentChanges` using Max=999 sentinel. |

---

## 🐛 Regression Fix (Not in Batch)

### DB-MOD1-09 Config File Regression — Entity Spawning Broken in `-m all` Mode

**Symptom:** After the batch was implemented, `bagira.runner.exe -m all` stopped spawning entities. The `CreateEntityRequest` was published (log: `[TRACE-GW] Sending CreateEntityRequest`) but SimHost never processed it.

**Root cause (two-part):**

1. **Config.json was no longer being loaded.** The old `SimHostConfig.Load("config.json")` call in `OnLoad()` was replaced with `_nodeConfig ?? new NodeConfiguration()`. In the Runner path, `_nodeConfig` is always `null` (the Runner passes only `domainOverride`), so `new NodeConfiguration()` was used — all defaults, config.json was never read.

2. **JSON key name mismatch.** `config.json` uses `"DomainId": 0` but `NodeConfiguration` has property `DdsDomainId`. Even after fixing item 1, these strings are different even with `PropertyNameCaseInsensitive = true` (`"domainid" ≠ "ddsdomainid"`), so the file's value would have been silently ignored.

**Effect on domain ID:**
- Runner calls `SimHostSubsystem.Initialize()`: `int? domainOverride = config.DomainId > 0 ? config.DomainId : null` → with default `DomainId = 0`, this yields `null`
- `SimHostApp.OnLoad()`: `_domainOverride ?? nodeConfig.DdsDomainId` → `null ?? 42 = 42`
- SimHost joined DDS domain **42**; all other nodes (IG, IOS) were on domain **0** → `CreateEntityRequest` published to domain 0 was invisible to SimHost

**Fix applied to `SimHostApp.cs` and `config.json`:**

```csharp
// Before (broken):
var nodeConfig = _nodeConfig ?? new NodeConfiguration();

// After (fixed):
var nodeConfig = _nodeConfig ?? NodeConfiguration.LoadFrom("config.json");
if (_nodeConfig == null) nodeConfig.ApplyEnvironment();
```

```json
// config.json — before:
{ "DomainId": 0, ... }

// config.json — after:
{ "DdsDomainId": 0, ... }
```

**Verification:** 183/183 `Bagira.SimHost.Tests` pass; 31/31 integration tests pass.

---

## 🧪 Testing Results

| Test Suite | Result | Count |
|---|---|---|
| `Bagira.Runner.Integration.Tests` | ✅ Passed | 31 / 31 |
| `Bagira.SimHost.Tests` | ✅ Passed | 183 / 183 |
| `FDP.Toolkit.Combat.Tests` | ✅ Passed | 28 / 28 |
| `FDP.Toolkit.CarKinem.Tests` | ✅ Passed | 126 / 126 |
| Solution build (Release) | ✅ Clean | 0 errors |

**Key test scenarios verified:**
- [x] `SimHostDrag_IgReceivesPositionUpdateWithinFewFrames` — passes unconditionally (31/31 runner tests)
- [x] `GlobalComponentIds_NoToolkitBlockDuplicates` — no duplicates found
- [x] `BagiraComponentIds_NoDuplicates` — no duplicates found
- [x] `System_AvoidanceMovesVehicle` — non-vacuous; fails without `SetAuthority`
- [x] `HealthData_DirtyGuard_OnlyWritesWhenCurrentChanges` — sentinel Max=999 preserved on skipped write; overwritten on actual write
- [x] `ReplayModule_SeekToFrameAsync_IsOffMainThread` — `task.IsCompleted == false` before await
- [x] `IgPresentationModule_ProductionCanvas_IsSameAsProvided` — canvas identity preserved
- [x] `NodeConfiguration_Parse_SimulationRateHz_And_GeodeticOrigin` — absorbed fields pass
- [x] `SimHostApp_ParseNodeConfig_LoadsFromFile_WhenFlagPresent` — LoadFrom path works

---

## 📝 Developer Insights

**Q1 — DB-MOD1-26: What was the actual root cause? Which `[D2c]`/`[D2d]` log line revealed it? What was missing?**

The root cause was **stale compiled binaries**. Neither `[D2c]` nor `[D2d]` diagnostic log lines were added, and neither were necessary to diagnose the issue. A full incremental-disabled rebuild (`dotnet build --no-incremental`) resolved both failing tests immediately. The diagnosis was: old DLL files in the output directory did not reflect recent source changes, causing the tests to execute outdated code. Neither `NetworkTransform` registration nor `DescriptorOwnership` was actually missing from the production code — the system was correct, just not rebuilt. After the clean rebuild, 31/31 integration tests passed without any source changes.

**Q2 — DB-MOD1-02: Did the uniqueness test find any actual duplicate IDs in `GlobalComponentIds` or `BagiraComponentIds`?**

No duplicates were found in either class. Both reflection-based tests pass cleanly:
- `GlobalComponentIds_NoToolkitBlockDuplicates` — enumerates all `const byte` fields on `GlobalComponentIds` via reflection; asserts each value appears exactly once.
- `BagiraComponentIds_NoDuplicates` — equivalent for `BagiraComponentIds` in `Bagira.Map.Common`.

The 20–49 toolkit block in `GlobalComponentIds` is fully occupied, which informed the decision in DB-MOD1-23 (see Q4).

**Q3 — DB-MOD1-09: Which config type was chosen as the survivor? How many files changed?**

**`NodeConfiguration`** was chosen as the survivor — it had significantly more existing usages (12 test references vs 3 for `SimHostConfig`) and already had `LoadFrom` / `ParseRole` / `ParseNodeConfig` infrastructure.

Fields absorbed from `SimHostConfig`:
- `SimulationRateHz` (int, default 60)
- `GeodeticOrigin` (new `GeodeticOriginConfig` sealed record, Tel Aviv defaults)

**Files changed: 4**
| File | Change |
|------|--------|
| `Bagira.SimHost/NodeConfiguration.cs` | Added `SimulationRateHz`, `GeodeticOriginConfig` type, and `GeodeticOrigin` property |
| `Bagira.SimHost/SimHostApp.cs` | Replaced `SimHostConfig.Load("config.json")` with `_nodeConfig ?? NodeConfiguration.LoadFrom("config.json")`; replaced `config.DomainId` with `(int)nodeConfig.DdsDomainId`; replaced `config.SimulationRateHz` |
| `Bagira.SimHost.Tests/SimHostConfigTests.cs` | Completely rewritten to test `NodeConfiguration`'s new absorbed fields |
| `Bagira.SimHost/Configuration/SimHostConfig.cs` | **Deleted** |

A post-batch regression was also discovered and fixed: the original `_nodeConfig ?? new NodeConfiguration()` in `OnLoad()` (a simplification error from the initial implementation) and the `"DomainId"` vs `"DdsDomainId"` key mismatch in `config.json` together caused SimHost to use DDS domain 42 instead of 0 in Runner mode. Fixed immediately upon discovery.

**Q4 — DB-MOD1-23: Which IDs were assigned to `NavigationIntent` and `NavigationStatus` in the new contracts assembly? Confirm the 20–49 block still has no duplicates after the move.**

`NavigationIntent` retained **ID 67** and `NavigationStatus` retained **ID 68**. These IDs were not reassigned to the 20–49 toolkit block because:
1. The uniqueness test (DB-MOD1-02) confirmed the 20–49 block is **full** — there are no free slots.
2. The numeric values 67 and 68 had no conflicts in the 50–79 range where they already lived.
3. Reusing the same IDs avoids any ECS registry incompatibility with in-flight records or snapshot files.

The tombstone comments in `GlobalComponentIds.cs` read:
```
// 67–68 freed — moved to FDP.Toolkit.Navigation.Contracts (NavigationContractsComponentIds).
```

The new `NavigationContractsComponentIds` class lives in `FDP.Toolkit.Navigation` namespace within the new thin assembly. The 20–49 block in `GlobalComponentIds` is unchanged — no duplicates, no new assignments.

---

## ⚠️ Notable Design Decision

**Why `NavigationIntent`/`NavigationStatus` IDs stayed at 67–68 (not reassigned to 20–49):**

The batch instructions specified "reassign from the 20–49 toolkit block". However, the uniqueness guard task (DB-MOD1-02) was completed first as instructed, and it confirmed the **20–49 block is fully occupied** — there are no free IDs available. Since the types already had numerically valid IDs in the 50–79 range with no conflicts, retaining 67/68 was the correct resolution. The instruction's intent was to avoid using kernel-space IDs (0–19) for toolkit types; 67/68 satisfies this requirement.

---

## 🔗 Files Changed Summary

| File | Change |
|------|--------|
| `Bagira.SimHost/SimHostApp.cs` | Restored `LoadFrom("config.json")` in `OnLoad`; `IgPresentationModule` wiring |
| `Bagira.SimHost/NodeConfiguration.cs` | Added `SimulationRateHz`, `GeodeticOriginConfig`, `GeodeticOrigin` |
| `Bagira.SimHost/config.json` | `"DomainId"` → `"DdsDomainId"` (key name fix for deserialization) |
| `Bagira.SimHost/Configuration/SimHostConfig.cs` | **Deleted** |
| `Bagira.SimHost.Tests/SimHostConfigTests.cs` | Rewritten to cover absorbed fields |
| `Bagira.SimHost.Tests/PresentationModuleTests.cs` | Added `IgPresentationModule_ProductionCanvas_IsSameAsProvided` |
| `FDP/Kernel/Fdp.Kernel/EntityQuery.cs` | Added `IsEmpty` property |
| `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` | Tombstone comments for IDs 67–68 |
| `FDP/Kernel/Fdp.Kernel/CoreComponents/NavigationComponents.cs` | **Deleted** (moved to contracts assembly) |
| `FDP/Kernel/Fdp.Kernel.Tests/ComponentIdAttributeTests.cs` | Added `GlobalComponentIds_NoToolkitBlockDuplicates` |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/` | **New project** (3 files: csproj, NavigationContractsComponentIds.cs, NavigationComponents.cs) |
| `FDP/Toolkits/FDP.Toolkit.Navigation/FDP.Toolkit.Navigation.csproj` | Added ref to Navigation.Contracts |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/FDP.Toolkit.CarKinem.csproj` | Added ref to Navigation.Contracts |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` | Added `if (q.IsEmpty) return;` guard |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Modules/GroundKinematicsModule.cs` | Lazy-allocate `TrajectoryPool` and `_formationTemplates` via `??=` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Systems/CarKinematicsSystemTests.cs` | `System_AvoidanceMovesVehicle` now non-vacuous with `SetAuthority` |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs` | Dirty-flag guard before `HealthData` sync |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/DamageSystemTests.cs` | Added `HealthData_DirtyGuard_OnlyWritesWhenCurrentChanges` |
| `FDP/Toolkits/FDP.Toolkit.Replay.Tests/ReplayModuleTests.cs` | `Assert.False(seekTask.IsCompleted)` before await |
| `Bagira.Map.Common.Tests/ComponentIdTests.cs` | **New file**: `BagiraComponentIds_NoDuplicates` |
| `IOS-IG-SimHost.sln` | Added `FDP.Toolkit.Navigation.Contracts` project and build configs |
