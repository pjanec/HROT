# BATCH-22 Review

**Batch:** BATCH-22
**Tasks:** P2-00 (pre-flight), P2-01 (SquadPerceptionMergeSystem), P2-02 (SquadInputs)
**Verdict:** APPROVED

---

## Overall Assessment

Pre-flight renames applied cleanly. `SquadPerceptionMergeSystem` correctly implements the
cadence-gate + event-driven path with a `ChangeEpoch` XOR-checksum. `SquadInputs` mirrors
`StandardInputs` patterns exactly. All 9 new tests pass; 58/58 regression tests pass (squad +
ThreatMatrix + StarterPack suite). No build warnings.

---

## Task-by-Task Review

### P2-00 Pre-flight renames + TargetMemory.ChangeEpoch

PASS.

- `SquadCognitiveState.Flags` (`public uint`) replaces `_scalarPad`. Doc comment added. PASS.
- `SquadContact.SourceMembersMask` (`public ushort`) replaces `_pad`. Doc comment added.
  Layout comment line updated. PASS.
- `SquadContactPool._memberEpochChecksum` (`internal ulong`) replaces `_r0`. Doc comment describes
  semantics clearly (XOR checksum, reset on merge). PASS.
- `TargetMemory.ChangeEpoch` (`public uint`) added after `Modalities` fixed array and before
  the mutation API. Bumped on new-slot allocation and on eviction; NOT bumped on score updates.
  Both call sites confirmed. PASS.
- Layout: `TargetMemory` size increases by 4 bytes. No downstream serializer hardcodes the size
  (confirmed by grep — only design docs reference `sizeof(TargetMemory)`). PASS.

### P2-01 SquadPerceptionMergeSystem

PASS.

- Guard: early return if `UnitRoster` or `Blackboard1024` absent. PASS.
- Epoch checksum: XOR over all members' `mem.ChangeEpoch` cast to `ulong`. PASS.
- Cadence gate: `LastMergeTick == 0` treated as "never populated" (first-run guard). This is
  correct — without this, an empty pool (checksum = 0 == initial `_memberEpochChecksum`) with
  a large interval would never run. SC-P2-01-4 tests this path directly. PASS.
- `MergeContact`: finds existing by `EntityId`, applies max-ThreatScore + latest-LastSeenTick +
  OR-`SourceMembersMask` + OR-`Flags` (modalities). Pool-full eviction uses a linear scan for
  lowest-score slot. PASS.
- Note: modalities are written into `SquadContact.Flags` (the field renamed from `_pad`). The
  field is general-purpose flags; using it to carry modality bits is consistent with its intended
  "status flags" semantics. PASS.
- Sort: insertion sort after full merge (not per-contact), descending by ThreatScore. PASS.
- Write-back: uses `MemoryMarshal.CreateSpan / Unsafe.As` for the `SquadContactPoolSlots`
  InlineArray. Correct defensive-copy-safe pattern. PASS.
- Entity reconstruction: `new Entity((ulong)roster.SubordinateEntities[m])` uses the
  `Entity(ulong packed)` constructor which unpacks Index + Generation correctly. PASS.
- SC-P2-01-1 through SC-P2-01-5: all pass.

### P2-02 SquadInputs

PASS.

- `SquadInputIds`: FNV-1a-16 constants `SquadKnowsContact=0xBA51`,
  `SquadContactThreatLevel=0x2457`. No collision with `StandardInputIds`. PASS.
- `RegisterAll`: uses `UtilityInputReaderStore.Register(id, &method)` — matches
  `StandardInputs.RegisterAll` pattern exactly. PASS.
- `SquadKnowsContact`: walks `UnitSubordinate.Commander` → `Blackboard1024` →
  `SquadCognitiveState.Project` (via `Unsafe.AsRef` for read-only bb) → scans contact pool.
  Uses `ctx.Context.PackedValue` cast to `long` for the candidate id comparison. PASS.
- `SquadContactThreatLevel`: same walkup, returns `Math.Clamp(span[i].ThreatScore, 0f, 1f)`.
  `StandardInputs.ContactThreatLevel` also clamps, so this is consistent. PASS.
- Default-safe: both readers return `0f` for all missing-component cases. PASS.
- SC-P2-02-1 through SC-P2-02-4: all pass.

---

## Test Quality Assessment

| Dimension | Rating |
|-----------|--------|
| First-run guard (SC-P2-01-4 variant) | Excellent |
| Epoch-driven re-merge with large interval | Excellent |
| Cadence skip / boundary | Good |
| Max-threat + OR-mask merge semantics | Good |
| Capacity eviction reject/replace | Good |
| SquadKnowsContact default-safe paths | Good |
| SquadContactThreatLevel normalization | Good |

One minor gap: no test explicitly checks that updating an existing contact's score does NOT
bump ChangeEpoch. Not blocking (the behavior is only observable if the cadence gate logic
ever changes).

---

## Regressions

None. 1767 total tests (+9), ~67 failures (all pre-existing in ReplayBrowser/Replication/Navigation).

---

## Action Items for BATCH-23

Phase 2 remaining:
1. P2-03: `DangerAreaSensor` + `DangerAreaCognitiveBuffer` ECS components +
   `DangerAreaRefreshSystem`. Needs two new GlobalComponentIds entries (257, 258).
2. P2-04: Phase-2 integration test (4-member squad, contacts A and B, StreetCrossing danger area,
   cap invariant, zero-alloc over 100 ticks).
