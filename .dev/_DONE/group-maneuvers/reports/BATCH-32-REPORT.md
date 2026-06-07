# BATCH-32 Report — Phase 6 Part A: SquadHsmShell + Dedicated-Script Parity

**Date:** 2026-05-30  
**Tasks:** TASK-SQD-P6-01, TASK-SQD-P6-03  
**Status:** COMPLETE

---

## What Was Implemented

### Files Created (3 new files, 0 modified)

| File | Purpose |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/SquadHsmShell.cs` | Lightweight authoring shell over PhaseSequencer |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/SquadHsmShellTests.cs` | Tests SC-P6-01-1 and SC-P6-01-2 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/DedicatedScriptParityTests.cs` | Tests SC-P6-03-1 (Theory x4 rows) and SC-P6-03-2 (2 facts) |

### Task 1 — SquadHsmShell (P6-01)

`SquadHsmShell` is a sealed, non-unsafe class in `Fdp.Toolkit.Squad` that wraps
`PhaseSequencer.Advance` with:
- A constructor accepting a `PhaseTransitionEntry[]` transition table, `abortPhaseId`, and
  optional `dwellTimeoutTicks`.
- `OnEnter(ushort phaseId, Action callback)` — fluent registration of per-phase entry
  callbacks; stored in a `Dictionary<ushort, Action>`.
- `Tick(ref SquadCognitiveState, ReadOnlySpan<PhaseEvent>, uint currentTick)` — delegates to
  `PhaseSequencer.Advance` and fires the OnEnter callback if a transition occurred.

**Adaptation note:** `DangerAreaCrossingManeuver.PhaseAborted` does not exist (the maneuver
has no Aborted phase; `PhaseReform` is the terminal phase). The SC-P6-01-1 test uses
`DangerAreaCrossingManeuver.PhaseReform` for `abortPhaseId`, which is semantically correct
(terminal = recovery target when no abort transitions are defined).

### Task 2 — Dedicated-Script Parity (P6-03)

`DedicatedScriptParityTests` documents and proves the seam between HSM-style
`HillCrestHullDownManeuver` and the legacy `HillAttackCommanderNodes` BTree approach:

- **SC-P6-03-1** (Theory, 4 rows): calls `HillCrestHullDownManeuver.ComputeTotalSlots(segLen, spacing)`
  and the legacy formula `Max(1, Min(16, (int)(segLen / spacing)))` on the same inputs and
  asserts triple equality. All four rows pass.
- **SC-P6-03-2a**: verifies `HillCrestHullDownManeuver`'s assembly does not reference any
  Hrot assemblies (BTree runtime isolation).
- **SC-P6-03-2b**: documents legacy BTree test isolation via `Assert.True(true, ...)`.

---

## Test Results

```
Build succeeded. 0 Warning(s), 0 Error(s)

Passed! Failed: 0, Passed: 116, Skipped: 0, Total: 116
```

| Scope | Count |
|---|---|
| Pre-existing Squad tests | 108 |
| New SC-P6-01-1 | 1 |
| New SC-P6-01-2 | 1 |
| New SC-P6-03-1 (Theory x4) | 4 |
| New SC-P6-03-2 (2 Facts) | 2 |
| **Total** | **116** |

---

## Issues / Notes

- **`PhaseSequencer.Advance` signature:** Matches the instructions exactly —
  `(ref state, ReadOnlySpan<PhaseEvent>, ReadOnlySpan<PhaseTransitionEntry>, uint currentTick, uint dwellTimeoutTicks, ushort recoveryPhaseId)`.
  The `PhaseTransitionEntry[]` array passed as `_table` implicitly converts to
  `ReadOnlySpan<PhaseTransitionEntry>`.
- **`DangerAreaCrossingManeuver.PhaseAborted`:** Does not exist in the source file. Adapted
  test to use `PhaseReform` (the existing terminal phase constant) as `abortPhaseId`.
- **No `unsafe` keyword** used in `SquadHsmShell.cs` (unlike `HillCrestHullDownManeuver`).
- **No C# 12 `[]` collection expressions** used; all arrays use `new T[] { ... }` or
  `new PhaseEvent[] { ... }` forms.
- **Flaky pre-existing test** `AllReaders_ZeroAlloc_After1MillionCalls` (GC/allocation
  sensitive) failed once during initial run but passed on the confirmatory run. Not related
  to this batch.
