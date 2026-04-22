# BATCH-01 Report

**Batch:** BATCH-01
**Developer:** GitHub Copilot
**Date:** 2025-07-17
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MPM-P1-T01 | [x] | PerceptionBroadphaseSystem.cs and ThreatEvaluationAdapterSystem.cs deleted; CombatModule.cs cleaned up |
| MPM-P1-T02 | [x] | INetworkReplayTarget.cs deleted; all 4 translator files stripped; DescriptorOrdinal hash removed |
| MPM-P1-T03 | [x] | AutoCycloneTranslator/Managed, ReplicationBootstrap, FdpDescriptorAttribute, AutoCycloneTranslatorTests, NetworkDemo and NetworkDemo.Tests directories deleted; both SLN files updated |

---

## Build Status

`dotnet build IOS-IG-SimHost.sln --no-restore`: **Build succeeded. 0 errors.**

`dotnet build FDP/FDP.sln`: **FAILED** - pre-existing failures unrelated to this batch:
- MSB3202: `Fdp.ModuleHost.Core.csproj` not found (missing on disk before batch began)
- MSB3202: `Fdp.ModuleHost.Benchmarks/ModuleHost.Benchmarks.csproj` not found
- NETSDK1004: NuGet assets missing for `ExtDeps/FastBTree`, `ExtDeps/FastCycloneDds`, `ExtDeps/FastHSM`, and `Fdp.Examples.Showcase` (no `dotnet restore` has been run for these)

None of these failures are caused by this batch. They exist in the pre-batch baseline.

---

## Test Status

`dotnet test FDP/Network/Fdp.Network.Cyclone.Tests/...`: **Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40**

`dotnet test Hrot/Subsystems/Hrot.IG.Tests/... --filter TransformSyncSystem`: **Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5**

`dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/... --filter SimHostCoreLogicPackTests`: **Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3**

`dotnet test Hrot/Subsystems/Hrot.IG.Tests/...` (all tests): **Failed: 4, Passed: 419, Total: 423** - the 4 failures are pre-existing, unrelated to this batch:
- `AdvancedFeaturesIntegrationTests.Phase4_AllSubsystems_WorkTogetherInSharedRepo` - `Assert.Equal()` value mismatch
- `AdvancedFeaturesIntegrationTests.Phase4_TwoFireEvents_BothSpawnEffects` - `Assert.Equal()` value mismatch
- `GeoSpatialDRTranslatorTests.Decode_UnknownEntity_CreatesGhostAndSetsNetworkVelocity` - `SimTransform is not registered`
- `GeoSpatialDRTranslatorTests.Decode_KnownEntity_SetsNetworkVelocity` - `SimTransform is not registered`

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three cascade compile errors required out-of-order work:

1. **T02 broke AutoCyclone/ManagedAutoCyclone early.** Deleting `INetworkReplayTarget.cs` in Task 2 immediately caused CS0246/CS0234 errors in `AutoCycloneTranslator.cs` and `ManagedAutoCycloneTranslator.cs` because those files (both scheduled for deletion in T03) still referenced `Fdp.Network.Cyclone.Abstractions`. Resolution: pulled the T03 deletions forward to fix the T02 build. This was the correct move - the instructions say "fix any compile errors before moving on."

2. **T03 deletion broke `Hrot.IG.Tests`.** After deleting the NetworkDemo directory, `Hrot.IG.Tests.csproj` still held a `ProjectReference` to `Fdp.Examples.NetworkDemo.csproj`, and two test files imported `Fdp.Examples.NetworkDemo.Systems` and `Fdp.Examples.NetworkDemo.Components`. This was a hidden coupling not mentioned in the task spec. Resolution: redirected the `.csproj` reference to `Fdp.Examples.Common`, and updated the two using directives to `Fdp.Examples.Common.Systems`. The `TransformSyncSystem` class actually lives in `Fdp.Examples.Common.Systems` - NetworkDemo held a secondary usage, not the canonical definition.

3. **Stale comments in `SimHostCoreLogicPackTests.cs`.** The T01 spec verification (`grep -r "PerceptionBroadphaseSystem|ThreatEvaluationAdapterSystem" Hrot/`) returned hits in a test file comment that listed these systems as contributing `sim=2` to the simGroup count. The comments were wrong (these systems were never actually registered - which is why the batch spec said to delete them). Fixed by rewriting the breakdown comment to accurately reflect `CombatModule: no systems in simGroup (sim=0)` and removing the now-incorrect note about ThreatEvaluationAdapterSystem.

**Q2: Did you spot any weak points or other dead code in the existing codebase beyond what was specified? What would you improve?**

- `Hrot.IG.Tests` was coupling its test infrastructure directly to an example project (`Fdp.Examples.NetworkDemo`) instead of the canonical library (`Fdp.Examples.Common`). This was fragile by design - test projects should not depend on example application projects.
- `TransformSyncSystem` was co-located in NetworkDemo as a secondary location alongside its canonical home in `Fdp.Examples.Common.Systems`. This duplication risk was mitigated once NetworkDemo was deleted.
- `FDP/FDP.sln` contains two project entries pointing to files that do not exist on disk (`Fdp.ModuleHost.Core.csproj` and `ModuleHost.Benchmarks.csproj`). These will always cause MSB3202 when building via `FDP/FDP.sln`. This is a pre-existing issue outside the scope of this batch but worth tracking.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

The one unspecified change was updating `Hrot.IG.Tests.csproj` and two test files to reference `Fdp.Examples.Common` instead of the deleted `Fdp.Examples.NetworkDemo`. The alternative would have been to delete those test files entirely (as they tested TransformSyncSystem via NetworkDemo scaffolding). However, since `TransformSyncSystem` survives in `Fdp.Examples.Common`, keeping the tests and redirecting them was the right call - 5 tests continue to exercise the system under its correct home.

**Q4: Were there any surprises in the SLN file editing (unexpected project references, extra GUIDs)?**

- `IOS-IG-SimHost.sln` uses CRLF line endings throughout. The PowerShell `-replace` regex required `\r?\n` patterns to match both CRLF and LF safely.
- `IOS-IG-SimHost.sln` contained entries for `Hrot.Examples.NetworkDemo` (a separate, unrelated project with a similar name) which should NOT be deleted. The GUIDs were different (`{CADB0001-...}`), so the targeted GUID-based removal approach avoided any accidental deletion.
- `FDP/FDP.sln` used different GUIDs for the NetworkDemo projects (`{DEDC3340-...}` and `{4F15AD80-...}`) than `IOS-IG-SimHost.sln` (`{BB51FB75-...}` and `{AA238910-...}`), as expected since they were added to each solution independently.

**Q5: Are there any remaining references to the deleted artifacts that weren't covered by the task spec?**

All spec-required grep verifications return zero results:
- `grep "PerceptionBroadphaseSystem|ThreatEvaluationAdapterSystem" Hrot/**/*.cs` - 0 results (after fixing stale test comment)
- `grep "INetworkReplayTarget|InjectReplayData" FDP/Network/Fdp.Network.Cyclone/**/*.cs` - 0 results
- `grep "AutoCycloneTranslator|ManagedAutoCycloneTranslator|ReplicationBootstrap|FdpDescriptorAttribute|\[FdpDescriptor" **/*.cs` - 0 results

---

## Outstanding Issues / Next Steps

- `FDP/FDP.sln` build is broken by two pre-existing missing project files (`Fdp.ModuleHost.Core` and `Fdp.ModuleHost.Benchmarks`). These should be tracked and fixed in a separate batch or cleanup task.
- 4 pre-existing test failures in `Hrot.IG.Tests` (GeoSpatialDRTranslator and AdvancedFeaturesIntegration) should be investigated in a future batch.

---

## Suggested Commit Message

```
chore: dead code purge - Phase 1 (BATCH-01)

Remove three families of dead code that created confusion, violated ACL
constraints, or polluted the diagnostic surface:

MPM-P1-T01: Delete legacy perception systems
- Delete PerceptionBroadphaseSystem.cs and ThreatEvaluationAdapterSystem.cs
- Clean up CombatModule.cs comments

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

Fix-up: Redirect Hrot.IG.Tests from deleted NetworkDemo to Fdp.Examples.Common
Fix-up: Update stale system-count comment in SimHostCoreLogicPackTests

Build: IOS-IG-SimHost.sln succeeds with 0 errors
Tests: Fdp.Network.Cyclone.Tests 40/40 passed
```
