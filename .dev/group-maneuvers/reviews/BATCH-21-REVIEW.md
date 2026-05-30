# BATCH-21 Review

**Batch:** BATCH-21
**Tasks:** P1-01 through P1-05 (group-maneuvers Phase 1 primitives library)
**Verdict:** APPROVED

---

## Overall Assessment

All 5 Phase-1 primitives implemented correctly as pure static classes. 16 new tests added,
16/16 pass. `ThreatMatrixAssignmentSystem` refactored to use `GreedyMatrixAssigner` with
no behavioral change (51/51 regression tests pass). No new warnings.

---

## Task-by-Task Review

### P1-01 — ElementPartitionPrimitive

PASS.

- `MemberPartitionInput` struct: 4 floats with indexed accessor `this[int i]`. No heap. PASS.
- `ElementPartitionPrimitive.Partition` uses the `MemoryMarshal.CreateSpan` / `Unsafe.As`
  pattern correctly for `MemberElementIndexArray` writes. No defensive-copy trap. PASS.
- Hysteresis: member stays in current element unless `inputs[i][newBest] - inputs[i][current]
  > decisiveGap`. Correct strict-greater-than (a tie or marginal gap does not flip). PASS.
- `LastRepartitionTick` NOT bumped by the primitive (caller does it) — correct separation of
  concerns per instructions. PASS.
- Zero-alloc test uses `GC.GetAllocatedBytesForCurrentThread()` (monotone, thread-local,
  immune to background GC noise). Post-loop GC.Collect(2) cleanup protects subsequent tests
  that use `GC.GetTotalMemory`. Good defensive testing practice. PASS.
- SC-P1-01-1 through SC-P1-01-4: all pass.

### P1-02 — TacticalFeatureHandles

PASS.

- `Acquire`: only writes `state.ActiveFeatureId` when `!= featureId`. Idempotent. PASS.
- `TryRefresh`: `foreach (ref readonly var d in descriptors)` — zero-alloc O(N) scan. PASS.
- On failure: `descriptor = default`; `state.ActiveFeatureId` untouched. PASS.
- SC-P1-02-1 through SC-P1-02-3: all pass. The eviction test correctly uses a second span
  without the original featureId and asserts `state.ActiveFeatureId == 20u` unchanged. PASS.

### P1-03 — GreedyMatrixAssigner + ThreatMatrixAssignmentSystem refactor + RoleSlotAssignmentPrimitive

PASS.

**GreedyMatrixAssigner:**
- Pure static; `unsafe` for `stackalloc int[candidateCount]`. Max 16 candidates. PASS.
- Row-major matrix `scoreMatrix[m * candidateCount + c]`. Focus-fire cap via `focusCount`.
  Returns -1 when all candidates saturated. PASS.

**ThreatMatrixAssignmentSystem refactor:**
- `using Fdp.Toolkit.Squad.Primitives` added cleanly.
- Pre-builds matrix via `stackalloc float[maxMembers * maxTargets]` and `UtilityScorer.Evaluate`.
- Delegates to `GreedyMatrixAssigner.Assign`. Writes back in a second pass.
- `FocusFireCount` computed correctly in a third pass via `focusCount[]` array.
- All 51 existing regression tests pass. No behavioral change. PASS.

**RoleSlotAssignmentPrimitive:**
- `RoleSlotCandidate` is 2 bytes (byte RoleId + byte _pad). Clean.
- Uses `MemoryMarshal.CreateSpan` / `Unsafe.As<RoleAssignmentArray, RoleSlot>` for writes
  into `state.Roles`. Correct defensive-copy-safe pattern. PASS.
- `AssignRoles` with empty candidates returns immediately (no-op). SC-P1-03-3 PASS.
- Score matrix is caller-provided `ReadOnlySpan<float>`. Zero allocation. PASS.
- SC-P1-03-1 through SC-P1-03-3: all pass.

### P1-04 — PhaseSequencer

PASS.

- `PhaseEventKind` enum: all 6 event kinds defined. `VetoDetected = 4` overrides. PASS.
- `PhaseTransitionEntry`: 2B `FromPhaseId` + 1B `EventKind` + 1B `_pad` + 2B `ToPhaseId` = 6B
  (sequential layout). `#pragma warning disable CS0169` on the pad field. PASS.
- `Advance` scan order: VetoDetected FIRST (full events scan), then table match, then dwell
  timeout. SC-P1-04-3 confirms veto overrides a co-arriving FarSideReached event. PASS.
- `state.PhaseEnteredTick = currentTick` on every transition. SC-P1-04-1 tick bump. PASS.
- Dwell timeout: `currentTick - state.PhaseEnteredTick >= dwellTimeoutTicks`. Note: this is
  uint subtraction; if `currentTick < state.PhaseEnteredTick` (impossible in practice but
  worth noting), the result wraps to a large uint and would trigger a false timeout. Acceptable
  for Phase 1 — tick regression is not a production scenario.
- SC-P1-04-1 through SC-P1-04-3: all pass.

### P1-05 — SlotRotation

PASS.

- `SlotRotationState`: 4 bytes (`ushort UsedMask` + `ushort BurnedMask`). Clean. PASS.
- `AcquireSlot`: checks `BurnedMask` first, then `UsedMask`. Returns -1 when no slot available.
  Sequential (lowest index first) per spec. PASS.
- `ReleaseSlot`: clears `UsedMask` bit only. `BurnedMask` untouched. PASS.
- `BurnSlot`: sets `BurnedMask` bit; ALSO clears `UsedMask` bit (slot no longer "in use",
  future `AcquireSlot` will skip it via `BurnedMask`). Correct domination semantics. PASS.
- SC-P1-05-2: `BurnThenRelease` test acquires 8 slots and verifies slot 3 never returned.
  Correct — `BurnedMask[3]=1` is checked before `UsedMask` in `AcquireSlot`. PASS.
- SC-P1-05-1 through SC-P1-05-3: all pass.

---

## Test Quality Assessment

| Dimension | Rating |
|-----------|--------|
| Hysteresis boundary conditions | Excellent — both marginal (below gap) and decisive (above gap) cases tested |
| Feature-ref eviction | Good — eviction tested with a distinct second span |
| Role assignment correctness | Good — 4x4 matrix with hand-verified expected outcome |
| Phase transition ordering | Good — veto-vs-matching-event co-arrival tested |
| Slot burn domination | Good — burn+release sequence tested |
| Zero-alloc | Good — thread-local allocation counter (immune to GC noise) |
| Regression (ThreatMatrix refactor) | Excellent — 51 existing tests pass unchanged |

One minor gap: no test verifies `PhaseTransitionEntry` struct size (6B). Not blocking.

---

## Regressions

None. 1758 total tests (+16), 67 failures (all pre-existing). The
`ThreatMatrixAssignmentSystem` refactor produces byte-identical results for all 51
ThreatMatrix/StarterPack/StandardInputs tests.

---

## Action Items for BATCH-22

1. Implement Phase 2: `SquadPerceptionMergeSystem` (10 Hz + event-driven), `SquadKnowsContact`
   input reader, `DangerAreaSensor + DangerAreaCognitiveBuffer` components, and the Phase-2
   integration test.
2. Before Phase 2 starts: rename `SquadCognitiveState._scalarPad` to `public uint Flags` and
   `SquadContact._pad` to `public ushort SourceMembersMask` (noted as P2 debt in BATCH-20 review).
   These renames should be in BATCH-22 as task P2-00 (pre-flight fixes) OR folded into the
   first task of Phase 2 that needs them.
