# BATCH-01 Review

**Batch:** BATCH-01 - Phase 1: Dead Code Purge  
**Tasks:** MPM-P1-T01, MPM-P1-T02, MPM-P1-T03  
**Reviewer:** Dev Lead  
**Date:** 2026-04-22  
**Result:** APPROVED

---

## Overall Assessment

All three tasks completed correctly. Build succeeds with zero errors. The developer handled unexpected cascade errors (NetworkDemo references in Hrot.IG.Tests) appropriately without stopping or asking permission.

---

## Task-by-Task Verification

### MPM-P1-T01 - Delete Legacy Perception Systems

**Status: PASS**

- `PerceptionBroadphaseSystem.cs` and `ThreatEvaluationAdapterSystem.cs` confirmed deleted (files do not exist on disk).
- `CombatModule.cs` comment block removed.
- Zero grep matches for either system name in `Hrot/` source.
- Stale comment in `SimHostCoreLogicPackTests.cs` correctly updated.

### MPM-P1-T02 - Delete INetworkReplayTarget

**Status: PASS**

- `INetworkReplayTarget.cs` deleted.
- `CycloneTranslator<TDds,TView>` class declaration no longer includes `: INetworkReplayTarget`.
- `InjectReplayData` method removed from all 4 translator files.
- `DescriptorOrdinal = topicName.GetHashCode()` hack removed from `CycloneNativeEventTranslator`.
- `Fdp.Network.Cyclone.Tests`: 40/40 passed.

### MPM-P1-T03 - Delete AutoCycloneTranslators, ReplicationBootstrap, NetworkDemo

**Status: PASS**

- All specified files and directories deleted.
- Both `FDP/FDP.sln` and `IOS-IG-SimHost.sln` updated (project blocks + `ProjectConfigurationPlatforms` + `NestedProjects`).
- Full build: `IOS-IG-SimHost.sln` succeeds with zero errors.
- Unspecified fix applied correctly: `Hrot.IG.Tests.csproj` reference redirected from deleted NetworkDemo to `Fdp.Examples.Common`. 5 `TransformSyncSystem` tests continue to pass.

---

## Build Verification

```
dotnet build IOS-IG-SimHost.sln --no-restore
Build succeeded. 0 errors.
```

```
dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/...
Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40
```

---

## Test Issues Noted (Pre-existing, Not Caused by This Batch)

4 failing tests in `Hrot.IG.Tests` are pre-existing and not caused by this batch:
- `AdvancedFeaturesIntegrationTests.Phase4_AllSubsystems_WorkTogetherInSharedRepo`
- `AdvancedFeaturesIntegrationTests.Phase4_TwoFireEvents_BothSpawnEffects`
- `GeoSpatialDRTranslatorTests.Decode_UnknownEntity_CreatesGhostAndSetsNetworkVelocity`
- `GeoSpatialDRTranslatorTests.Decode_KnownEntity_SetsNetworkVelocity`

These are tracked as P3 tech debt below.

---

## Technical Debt Recorded

| ID | Priority | Description | Target Batch |
|----|----------|-------------|--------------|
| DEBT-001 | P3 | `FDP/FDP.sln` references two missing project files (`Fdp.ModuleHost.Core.csproj`, `ModuleHost.Benchmarks.csproj`). Always breaks `FDP/FDP.sln` build. Pre-existing issue. | Deferred |
| DEBT-002 | P3 | 4 pre-existing test failures in `Hrot.IG.Tests` (2 `AdvancedFeaturesIntegration`, 2 `GeoSpatialDRTranslator`). Unrelated to this batch. | Deferred |

---

## Developer Insights Extracted

- **Hidden test coupling discovered:** `Hrot.IG.Tests` was incorrectly depending on `Fdp.Examples.NetworkDemo` (an example project) instead of `Fdp.Examples.Common` (the canonical library). Deletion exposed the fragile dependency. Fixed by redirecting. Future batches should not introduce test → example-project couplings.
- **GUID uniqueness in SLN files confirmed:** `IOS-IG-SimHost.sln` has a separate `Hrot.Examples.NetworkDemo` project (different GUIDs) which was correctly preserved.
- **CRLF handling in SLN PowerShell editing:** Developer needed `\r?\n` patterns for robust multi-line block removal from SLN files on Windows.

---

## Suggested Commit Message

```
chore: dead code purge - Phase 1 (MPM-P1-T01, T02, T03)

Remove three families of dead code:

MPM-P1-T01: Delete legacy perception systems
- Delete PerceptionBroadphaseSystem.cs and ThreatEvaluationAdapterSystem.cs
- Clean up CombatModule.cs comments and stale test comment

MPM-P1-T02: Delete INetworkReplayTarget and strip from translators
- Delete INetworkReplayTarget.cs
- Strip interface and InjectReplayData from CycloneTranslator,
  CycloneNativeEventTranslator, CycloneManagedEventTranslator,
  MultiInstanceCycloneTranslator
- Remove DescriptorOrdinal=topicName.GetHashCode() hack from
  CycloneNativeEventTranslator constructor

MPM-P1-T03: Delete AutoCycloneTranslators, ReplicationBootstrap, NetworkDemo
- Delete AutoCycloneTranslator.cs, ManagedAutoCycloneTranslator.cs
- Delete ReplicationBootstrap.cs, FdpDescriptorAttribute.cs
- Delete AutoCycloneTranslatorTests.cs
- Delete Fdp.Examples.NetworkDemo/ and Fdp.Examples.NetworkDemo.Tests/
- Remove deleted projects from FDP/FDP.sln and IOS-IG-SimHost.sln
- Redirect Hrot.IG.Tests from deleted NetworkDemo to Fdp.Examples.Common

Build: IOS-IG-SimHost.sln succeeds with 0 errors
Tests: Fdp.Network.Cyclone.Tests 40/40 passed
```
