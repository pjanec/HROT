# BATCH-34 Review

**Batch:** BATCH-34
**Tasks:** SC-P7-01, SC-P7-02, SC-P7-03
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

All three Phase 7 tasks implemented cleanly. 11 new tests pass, no regressions in the existing 18
overlay tests. Production code is concise, correctly guarded, and follows every established pattern.

---

## Production Code Review

### `SquadCoordinationOverlaySource.cs`

**Strengths:**
- Budget guard is the very first thing in `Emit` — correct.
- `SquadCognitiveState.Project` called on `GetComponentRW<Blackboard1024>`, not RO — correct.
- All inline-array spans use the mandatory `MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<...>(ref Unsafe.AsRef(in ...)))` defensive-copy-avoidance idiom — correct throughout.
- `EmitMemberOverlays` correctly copies the `UtilityTraceWorkingMemory1024` to a local before calling `LatestSelected()` (non-readonly method) — prevents the silent defensive-copy bug.
- `DrawObbEdges` emits exactly 12 `DrawLine` calls (4 bottom + 4 top + 4 verticals), with `ZFloor`/`ZCeiling` mapped to the Y axis as specified — verified by SC-P7-01-3.
- `EmitMemberOverlays` signature drops the unused `Entity commander` parameter compared to the initial design sketch — correct minimisation.
- Static readonly color fields named consistently and grouped by role.

**No issues found.**

---

## Test Quality Review

### `SquadCoordinationOverlaySourceTests.cs`

**Strengths:**
- `LineCapturingDrawBuilder` properly records `(start, end, LineStyle)` tuples and sphere positions — enables precise structural assertions that `CountingDrawBuilder` cannot provide.
- `CountingDrawBuilder` reused from same assembly without duplication — correct.
- `CreateTestRepo()` registers all seven component types required; no missing registrations.
- `SetupSquadCommander` helper seeds `Blackboard1024` via `SquadCognitiveState.Project`, matching the production access pattern exactly.
- SC-P7-01-3 uses two independent repos to isolate the ground fixture from the bridge-deck fixture and asserts exact `minY`/`maxY` values — strong structural test.
- SC-P7-02-1 uses a real `repo.CreateEntity()` member (not a fake packed long) so `HasComponent` calls on the member entity are safe.
- SC-P7-02-2 seeds `WriteWinnerRecord` before emitting and asserts both the dashed line and the label text containing the option id — good coverage.
- SC-P7-02-3 verifies label staleness is not an issue: label must differ between ticks when option id changes.
- SC-P7-03-2 checks `T0:` substring rather than the full label — robust against future label text changes that don't affect the tick value.
- SC-P7-03-3 checks both count (exactly 2) and positions (within 0.01) — verifies data fidelity.
- SC-P7-03-4 exercises budget priority ordering end-to-end: Channels shed → SquadAssignment still permitted → 50 squads emit.

**Minor observations (no rework required):**
- SC-P7-01-2 (`ElementColorPersistence`) checks emit count stability rather than actual color values — acceptable at this abstraction level since `IGizmoDrawBuilder` does not return color data to the caller.
- `using var arbiter` / `using var repo2` lifetimes in SC-P7-01-3 are slightly asymmetric (the `arbiter` is shared across both sub-fixtures). This works correctly because `BeginFrame` is called before each sub-fixture emit; no issue.

---

## Test Coverage Summary

| Task | Tests | Coverage |
|---|---|---|
| SC-P7-01 (element color + OBB) | 4 | Flag on/off, color determinism, OBB Y-extent for two altitude scenarios |
| SC-P7-02 (divergence lines + veto) | 3 | Solid-only path, dashed+label path, label staleness |
| SC-P7-03 (phase + contacts) | 4 | Phase label updates, entry-tick resets, sphere count+position, budget shedding |

---

## Build and Test Results

- Build: **0 errors** (11 pre-existing warnings, unrelated)
- Tests: **29/29 passed** (11 new + 18 existing)

---

## Verdict

**APPROVED.** No corrections required. All Phase 7 tasks are complete.
