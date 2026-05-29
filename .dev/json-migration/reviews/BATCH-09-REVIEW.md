# BATCH-09 Review

**Batch:** BATCH-09
**Reviewer:** Development Lead
**Date:** 2026-05-29
**Status:** APPROVED WITH MINOR NOTES

---

## Summary

BATCH-09 delivers the Phase 2 foundation:
- JM-P2-001: `05-integration-patches.md` catalogues 14 touchpoints covering all formats
  from TASK-DETAILS.md (Scenario, Blueprint, TKB, RoadNetwork, Replay, and all passthrough writers).
- JM-P2-002: `HrotDocumentTypes.cs`, `PassthroughFormatsModule.cs`, 4 skeleton modules,
  and a new `Hrot.Common.Tests` project with 7 tests — all passing.

Build is clean (excluding the pre-existing Hrot.Blueprints.Tests Stride-editor dependency
error which predates this batch). 7/7 new tests pass.

---

## Integration-Patches Document Quality (JM-P2-001)

The document meets the gate criteria: every touchpoint from TASK-DETAILS.md §JM-P2-001 is
covered with current shape, target shape, adapter type, docType constant, and before/after
pseudo-code.

Notable quality points:
- **RecordingDumper correctly deferred** to RecordingExportService (RecordingDumper is a
  thin wrapper; the JSON shape lives in the export service). This saves a no-op patch.
- **ReplayBrowserContext correctly marked N/A** — no direct JSON read/write; the subsystem
  context dispatches to RecordingExportService.
- **TransientMasterBuilder correctly noted as cascade-only** — the ScenarioSerializer update
  propagates automatically, no independent changes needed.
- **Key Findings section** captures 7 non-obvious integration constraints that Phase 2
  developers MUST respect (async vs sync loader, null-report fast-path, StructEdit string check,
  NodeConfiguration exception-swallowing, etc.).

**Approved for gating JM-P2-003 through JM-P2-009.**

---

## Code Quality Assessment (JM-P2-002)

### HrotDocumentTypes.cs

Correct. BehaviorTree constant present but clearly documented as non-registered (C-1).
OrchestratorContext doc-comment explains v2 registration (C-4). Subsystem routing
identifiers carried forward from HrotSubsystemTypes for routing consistency.

### PassthroughFormatsModule.cs

Correct. Static class, static method, 5 formats registered at correct versions (including
OrchestratorContext at v2 per C-4). Null guard on registry parameter is correct.

### Skeleton migration modules

Functionally correct but deviate from the design spec shape. The design (Migration-system.md
§9.1) shows `public static class ScenarioMigrationModule` with `public static void RegisterAll`.
The implementation uses `public sealed class` with instance `RegisterAll` method.

This inconsistency is tracked as a P3 debt item (D-017). It does not block Phase 2 but
must be fixed before the bootstrap wiring task (JM-P2-009) so the bootstrap code can call
`ScenarioMigrationModule.RegisterAll(reg)` statically without instantiation.

### Tests (ModuleRegistrationTests.cs)

Tests are meaningful and test the right things:
- T01: Verifies 5 specific docType registrations by name (not just "no exception")
- T02-T05: Assert both `IsRegistered` and `GetCurrentVersion` — pins the version contract
- T06: Reflection-based exhaustive check of all constants — good guard for future additions
- T07: End-to-end fast-path test using a real JSON document, confirming `WasMigrated == false`
  and `Report == null` — this is the most important test, it proves C-4 is correctly applied

T07's decision to assert `Assert.Null(outcome.Report)` is correct and well-documented.
The null semantics of `ReadOnlyLoadOutcome.Report` are noted as a weak point worth
documenting on the property itself.

---

## Debt Items

New items to add to DEBT-TRACKER.md:

| ID | Source | Description | Priority |
|----|--------|-------------|----------|
| D-017 | BATCH-09 | Skeleton modules (ScenarioMigrationModule, BlueprintMigrationModule, TkbMigrationModule, RoadNetworkMigrationModule) are `sealed class` with instance `RegisterAll` rather than `static class` with `static RegisterAll` as the design specifies (§9.1). Bootstrap wiring (JM-P2-009) expects static calls. Fix before JM-P2-009. | P2 |
| D-018 | BATCH-09 | `ReadOnlyLoadOutcome.Report` is `null` on the fast path (no migration needed) but this contract is not documented on the property. Phase 2 call sites that access `outcome.Report.Warnings` will NullReferenceException. Add XML doc comment to property clarifying `null` means no migration occurred. | P3 |
| D-019 | BATCH-09 | `RoadNetworkLoader.LoadFromJson` is synchronous. Phase 2 (JM-P2-006) must choose between: (a) making it async (preferred, but breaking), or (b) using `.GetAwaiter().GetResult()` synchronous wrapper. Decision should be made before JM-P2-006. | P2 |
| D-020 | BATCH-09 | `NodeConfiguration.LoadFrom` swallows all exceptions and returns defaults. Phase 2 (JM-P2-008) must preserve this behavior when wrapping with the migration adapter. Consider a try-catch wrapper around the adapter call. | P2 |

---

## Verdict

**Status: APPROVED** — JM-P2-001 and JM-P2-002 are complete and correct.

Phase 2 code patches (JM-P2-003+) may proceed. D-017 must be resolved as part of JM-P2-009
(or the batch immediately before it) since the bootstrap wiring relies on static calls.

---

## Commit Message

```
feat: Phase 2 foundation — integration-patches doc + HROT module skeletons (BATCH-09)

JM-P2-001: .dev/json-migration/05-integration-patches.md
  - 14 touchpoints catalogued: Scenario (3), Blueprint (1), TKB (1),
    RoadNetwork (1), Replay (4), passthrough writers (4)
  - Each touchpoint: current shape, target shape, adapter type,
    docType constant, before/after pseudo-code
  - Key findings: OrchestratorContext at v2, Blueprint cold-start,
    StructEdit string check, RoadNetworkLoader sync, NodeConfig swallows exceptions

JM-P2-002: HROT-side migration module scaffolding
  - Hrot.Common/Scenario/HrotDocumentTypes.cs (12 constants)
  - Hrot.Common/Scenario/Migrations/PassthroughFormatsModule.cs (5 formats, v1/v2)
  - Skeleton modules: Scenario, Blueprint, TKB, RoadNetwork (CurrentVersion=1)
  - Hrot.Common.Tests: new test project, 7 tests (all passing)

Tests: 7/7 new (Hrot.Common.Tests) | 350/350 existing (Fdp.Core migration tests)
```
