# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Development Lead
**Date:** 2026-05-03
**Status:** APPROVED

---

## Summary

All 7 tasks complete. Phase 5 (node-side handler, NLog file target, LocalTempRoot isolation)
and Phase 7 (IFileDialogService) are implemented and build clean.
127/127 orchestrator tests pass (regression-free).

---

## Issues Found

### Issue 1: MappedDiagnosticsLogicalContext obsolete — P3

**File:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs`
**Problem:** `NLog.MappedDiagnosticsLogicalContext` is marked `[Obsolete]` in NLog 5.
Produces two CS0618 warnings.
**Fix:** Use `NLog.ScopeContext.PushProperty("nodeId", ...)` and update the layout to
`${scopeproperty:nodeId}`.
**Note in DEBT-TRACKER as P3.** The functionality is correct.

### Issue 2: DomainPayload deserialization path — Design note (accepted)

**File:** `Hrot/Engine/Hrot.Common/Diagnostics/DiagnosticsDumpClusterOpHandler.cs`
**Observation:** Handler uses `intent.DomainPayload as DiagnosticDumpPayloadDto` rather
than deserializing from a raw JSON string. This requires `NodeOpSlaveTranslator` to handle
`CollectDiagnostics` in `DeserializeNodePayload`. The developer correctly added that case.
This is the correct pattern (matches how `StartEpisode` and `StopEpisode` work).

### Issue 3: NLog file target layout uses `${mdlc:nodeId}` — P3

**File:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs`
**Observation:** MDLC (`${mdlc:nodeId}`) is the NLog 4 / 5 layout renderer for MDLC values.
Since the code already uses `MappedDiagnosticsLogicalContext.Set`, this is self-consistent.
Captured in DEBT-TRACKER with P3 priority (see Issue 1 above).

---

## Test Quality Assessment

- 127/127 orchestrator tests pass (pre-existing + BATCH-03 diagnostics tests)
- FDP.Presentation compiles with 0 errors (4 pre-existing warnings)
- Hrot.Common, Hrot.Orchestrator, Hrot.ClusterRunner all build clean
- CycloneDDS.CodeGen transient file-lock error during parallel `dotnet test` invocations is
  a known flaky infrastructure issue; unrelated to code changes

---

## Verdict

**Status:** APPROVED

---

## Commit Message

```
feat: node-side diagnostics handler, NLog file target, LocalTempRoot isolation, IFileDialogService (BATCH-04)

Completes DD-P5-T01, DD-P5-T02, DD-P5-T04, DD-P5-T05, DD-P7-T01, DD-P7-T02, DD-P7-T03

Phase 5 - Node-Side Handler:
- --log-dir CLI option in HrotRunnerConfiguration (DD-P5-T02)
- NLog FileTarget with 50MB rolling archives, MDLC nodeId tag (DD-P5-T01)
- DiagnosticsDumpClusterOpHandler: entities/architecture/events/logs into LocalTempRoot/dumps/{tx:N}/ (DD-P5-T04)
- Hrot.Common.csproj: added Fdp.ModuleHost + Fdp.Toolkits project refs
- NodeOpSlaveTranslator: CollectDiagnostics deserialization case
- NodeBootstrapper: optional diagnosticsDumpHandler parameter
- ClusterConfiguration.NasBasePath (default C:\FDP_Temp\shared) (DD-P5-T05)
- OrchestratorSubsystem: NasBasePath wiring for all process managers
- SimHostApp: per-node LocalTempRoot (nodes/node-{id})

Phase 7 - IFileDialogService:
- IFileDialogService interface in Fdp.Presentation/ImGui/Abstractions (DD-P7-T01)
- ImGuiFileDialogService: async TCS modal dialog with directory navigator (DD-P7-T02)
- WindowManager.SetFileDialogService + Draw() at end of Render() (DD-P7-T03)

Debt: NLog MappedDiagnosticsLogicalContext obsolete (P3), noted in DEBT-TRACKER
```

---

**Next Batch:** BATCH-05 — Phase 6 (ClusterDiagnosticsPanel) + Phase 8 (DiagnosticLogMergeWorker)
