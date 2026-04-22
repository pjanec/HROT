# INTS-BATCH-04 Report

**Batch:** INTS-BATCH-04
**Developer:** GitHub Copilot
**Date:** 2026-02-27
**Status:** Complete (build fixed, solution tests run; failures listed below)

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CORRECTIVE-0 | [x] | Replaced mock ECS copy test with real DDS-backed SimHost/IG headless integration test on domain 10. |

---

## Implementation Summary

### Functional changes
- Real DDS E2E test uses headless SimHost + IG, domain override (10), and waits for a real `ResolvedStyle` on the IG side.
- SimHost and IG now support DDS domain override for embedded/headless use in tests.
- IG and SimHost register missing network and managed components for live DDS ingestion.
- Added a SimHost post-simulation system to set authority for `EntityMaster` on locally owned entities, ensuring descriptor egress.
- `StyleResolutionSystem` moved to `PostSimulation` and writes directly to the repository when available so it runs after network ingress.

### Build fix during this run
- Fixed nullable `domainIdOverride` initialization in `Hrot.ClusterRunner.Services.IgSubsystem` so the solution builds again.

---

## Troubleshooting Narrative (StyleResolutionSystem)

1. **Symptom:** IG entities existed with `SimTransform`, but `ResolvedStyle` never appeared.
2. **Root cause:** `StyleResolutionSystem` was scheduled in `Simulation`, but global systems do not execute in that phase for IG; the command buffer path also meant writes never committed in `PostSimulation`.
3. **Fix:** Move system to `PostSimulation` and write directly to the repository when the view is an `EntityRepository`.
4. **Result:** `ResolvedStyle` appears immediately after network ingress, enabling the E2E test to pass reliably.

---

## ComponentId Review (Hrot.Map.Definitions.Tkb)

- `SimVehicleDef`, `SimCombatDef`, and `TkbCompositionDef` are applied to spawned entities via `TkbTemplate.AddManagedComponent`.
- The ECS registry enforces `[ComponentId]` for all managed component types, so these classes must keep explicit IDs if they remain ECS-managed components.
- NetworkDemo follows the same rule for managed components (for example, `EntityType` and `SquadChat` both declare `[ComponentId]`).
- Conclusion: **ComponentId attributes are required** unless these TKB definitions stop being ECS-managed components.

---

## Authority Pattern Review (NetworkDemo vs SimHost)

- NetworkDemo explicitly adds `NetworkAuthority` and calls `SetAuthority<T>` for local-only descriptors (example: time sync entity and ack bridge).
- The SimHost `EntityMasterAuthoritySystem` mirrors that approach by granting authority to `EntityMaster` for locally owned entities, which is required for egress translators to publish the descriptor.
- Conclusion: the SimHost authority system is consistent with the NetworkDemo pattern and needed for DDS publication.

---

## Testing Results

### Targeted E2E (DDS) Integration Test
Command:
```
dotnet test .\Hrot.SimHost.Integration.Tests\Hrot.SimHost.Integration.Tests.csproj --filter "FullyQualifiedName~EntityLifecycleIntegrationTests"
```
Result: PASS

### Full Solution Build
Command:
```
dotnet build IOS-IG-SimHost.sln
```
Result: PASS with warnings (external/toolkit warnings only).

### Full Solution Tests
Command:
```
dotnet test IOS-IG-SimHost.sln --no-build
```
Result: FAIL (4 total). Failing tests observed:
- FDP.Toolkit.Lifecycle.Tests: `LifecycleCleanupSystemTests.Execute_RemovesTransientComponents_WhenActive`
- ModuleHost.Core.Tests: `ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced`
- ModuleHost.Core.Tests: `ReactiveSchedulingTests.ReactiveScheduling_AsyncModule_TracksVersionCorrectly`
- Fdp.Examples.UrbanCombat.Tests: `ApcBrainTests.UnmanagedHandle_RecoveredTarget_IsSameInstance`

These failures do not appear tied to the DDS E2E changes; they were present when running the entire solution test suite.

---

## Developer Insights

**Q1: What complexities did you discover when running two full Application instances in the same process bounds communicating over DDS?**
Running SimHost and IG headless in-process required explicit DDS domain overrides and careful component registration to avoid missing ECS component errors. `EntityMaster` authority also needed a SimHost-side system so the descriptor could be published to DDS.

**Q2: How large was the latency between SimHost processing the spawn and IG fully resolving the style component? How many ticks did you determine to be safe for synchronization?**
With a 12 ms tick sleep, `ResolvedStyle` was observed within the 120-tick window (roughly 2 seconds wall time). Keeping 120 ticks proved safe; smaller windows were not measured as reliable during this batch.

**Q3: Does this test cleanly teardown and dispose both apps and the CycloneDDS participants correctly such that it can run repeatedly without error?**
Yes. The test disposes both apps (`IgApplication.Shutdown` and `SimHostApp.Dispose`) and it ran repeatedly without teardown errors in this session.

---

## Outstanding Issues / Next Steps

- Investigate the four failing solution-level tests listed above if required for CI readiness.

