# BATCH-07 Review

**Tasks:** TKB-016, TKB-021, TKB-018  
**Reviewer:** Dev Lead  
**Verdict:** APPROVED

---

## Review Summary

All three tasks are correctly implemented. The implementation was done directly by the dev lead
due to subagent unavailability. A namespace bug in `ScenarioFileService.cs` was identified
and fixed during implementation (wrong `using Fdp.Toolkit.Tkb;` for `ITkbDatabase` which lives
in `Fdp.Interfaces`).

---

## Task-by-Task Analysis

### TKB-016 — ScenarioHeaderDto.TkbName

**Implementation:** Correct. Property added with null-comment doc.

**Tests (3):**
- Deserializes when present — tests the core function
- Absent property yields null — edge case covered
- Explicit null in JSON yields null — edge case covered

**Quality:** Adequate. Uses `HrotSerializerOptions.HrotJsonOptions` correctly, same as other
tests in the project. All 3 required success conditions from TASK-DETAIL verified.

### TKB-021 FDP — ScenarioHeader + ScenarioSerializer

**Implementation:** Correct. `TkbName = null` default param; conditional write avoids
polluting JSON with null `TkbName` key.

**Tests (2):**
- With TkbName → header JSON contains TkbName — exercises the conditional write
- Without TkbName → header JSON omits TkbName entirely — exercises the null-omit path

**Quality:** Good. Tests exercise the actual DOM nodes, not just round-trip serialization.

### TKB-021 HROT — ScenarioFileService

**Implementation:** Correct.
- `_tkbDb` field added
- Optional `ITkbDatabase? tkbDb = null` parameter preserves backward compatibility
- `TkbName = _tkbDb?.ActiveTkbName` stamped in both `ScenarioHeaderDto` and `ScenarioHeader`
- Namespace bug fixed: `using Fdp.Interfaces;` (not `Fdp.Toolkit.Tkb`)

**Tests (3):**
- Active TkbName → stamped in saved file header — full integration (write+read)
- Null ActiveTkbName → null TkbName in header — covers null propagation
- No ITkbDatabase → null TkbName in header — covers optional parameter path

**Quality:** Good. Tests do full save-then-read cycle using `HrotSerializerOptions.HrotJsonOptions`.
Covers all three constructor call permutations affecting TkbName.

### TKB-018 — Orchestrator consensus check

**Implementation:** Correct.
- `CheckTkbNameConsensus(files)` called after empty-dir guard, before parallel loop
- `PeekTkbNameFromFile` uses forward-only `Utf8JsonReader` — no DOM allocation
- Handles both `"Header"` and `"header"` property names
- Handles both `"TkbName"` and `"tkbName"` property names
- Throws `InvalidOperationException` with clear message on conflict
- Skips non-.json files
- Skips null/empty TkbName values in consensus

**Tests (5):**
- Same TkbName all files — happy path
- Conflicting TkbNames — verifies `InvalidOperationException` thrown
- All null TkbNames — null-consensus passes
- Mixed null + same name — null treated as "no opinion"
- Non-JSON files ignored — .bin files not checked

**Quality:** Excellent. All 5 success conditions from TASK-DETAIL covered exactly.
Tests use real temp files and teardown properly.

---

## Pre-existing Failures (Not caused by BATCH-07)

- `Hrot.Core.Tests`: 5 `LogArchiveExtractionServiceTests` failures
- `Hrot.Orchestrator.Tests`: 4 pre-existing failures (ClusterMasterContext, PrefetchScenario,
  ReferenceArchive, StorageProcess)
- `Hrot.Presentation.Tests`: 2 `EntityDragGizmoTests` floating-point assertion failures

All confirmed pre-existing by reverting changes and re-running.

---

## Technical Debt Additions

None. All tasks cleanly implemented per spec.

---

## Debt Tracker (unchanged from BATCH-06)

- D-001 (P3): TryGetDescriptor<T> struct overload not tested
- D-002 (P2): WithHeavyMemory no-op -> Blackboard1024 not restored
- D-003 (P2): UrbanAmbushIntegrationTests fail
- D-004 (P3): TkbDescriptorRegistry TryGetParser allocates string
- D-005 (P3): LOH test is heuristic
- D-006 (P3): TkbLoadClusterStateHandler cache miss on timestamp change not separately unit-tested

---

## Final Test Summary

| Project | Filter | Passed |
|---|---|---|
| Fdp.Toolkits.Tests | FullyQualifiedName~Tkb | 111 |
| Hrot.Core.Tests | FullyQualifiedName~ScenarioHeaderDto | 3 |
| Hrot.Presentation.Tests | FullyQualifiedName~ScenarioFileServiceTkb | 3 |
| Hrot.Orchestrator.Tests | FullyQualifiedName~TkbConsensus | 5 |
| Hrot.SimHost.Tests | FullyQualifiedName~Tkb | 29 |

All BATCH-07 tests pass. APPROVED for commit.
