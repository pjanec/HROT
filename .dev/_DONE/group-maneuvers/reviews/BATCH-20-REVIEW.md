# BATCH-20 Review

**Batch:** BATCH-20
**Tasks:** P0-01 through P0-05 (group-maneuvers Phase 0 prerequisites)
**Verdict:** APPROVED

---

## Overall Assessment

All 5 Phase-0 tasks implemented correctly. 14 new tests added, 14/14 pass. No regressions
in utility-ai, ThreatMatrix, or StandardInputs test suites. Pre-existing failures are
unaffected (ReplayBrowser.Export, Replication.IdAllocation, Navigation frustration watchdog).

---

## Task-by-Task Review

### P0-01 — Shrink AssignmentSlot 64->16 bytes + migrate call sites

PASS.

- `AssignmentSlot` is exactly 16 B: `long AssignedTargetHandle(8) + float AssignmentScore(4) +
  byte FocusFireCount(1) + byte Flags(1) + ushort _pad(2)`. Verified by
  `AssignmentSlot_SizeIs16Bytes`.
- `AssignmentSlotArray` is 256 B (16 x 16). Verified by `AssignmentSlotArray_SizeIs256Bytes`.
- Old `ThreatMatrixAssignmentState` struct and its `Project(ref Blackboard1024)` are deleted.
- `GetSlot`/`GetAssignedTarget`/`SetAssignment` moved to `AssignmentSlotArray` instance methods
  using `MemoryMarshal.CreateSpan` (avoids InlineArray defensive-copy trap). Correct pattern.
- All 4 call sites migrated atomically:
  - `ThreatMatrixAssignmentSystem.cs` line 57
  - `StandardInputs.cs`
  - `UtilityTestWorld.cs`
  - `StandardInputReaderTests.cs` (3 sites)
- Round-trip test `RoundTrip_Slot7_NoAliasing` confirms no aliasing between slot writes. PASS.
- The 66 ThreatMatrix/StandardInputs/StarterPack tests all continue to pass.

### P0-02 — SquadCognitiveState 1024-byte blackboard projection

PASS.

- Layout (Sequential, no explicit size attribute — relying on field arithmetic):
  Scalars@0(16) + Elements@16(32) + Slots@48(96) + Roles@144(32) + Assignment@176(256)
  + Contacts@432(592) = 1024 B.
- `SquadCognitiveStateOffsets` constants published and verified against unsafe field-pointer
  arithmetic in `SquadCognitiveState_OffsetsMatchConstants`.
- `Project(ref Blackboard1024)` delegates to `Blackboard1024.Project<SquadCognitiveState>`.
  Aliasing test confirms ref read-back matches the write.
- `SquadStateMarker` carries `[ComponentId(GlobalComponentIds.SquadStateMarker)]` with ID 256.
  `GlobalComponentIds.cs` updated with squad block 256-299 and doc comment. PASS.
- `[DataPolicy(DataPolicy.NoSave)]` attribute applied correctly (NoSave = 1<<3 in existing enum).

**Debt (P2):** `SquadCognitiveState._scalarPad` should be renamed to `public uint Flags`
per the design spec (bit 0 = missionOverride). Required before Phase 3 mission-override work.

**Debt (P2):** `SquadContact._pad` (ushort at bytes 30-31) should become
`public ushort SourceMembersMask` per the design spec. Required before Phase 2 perception merge.

Both deferred intentionally — no Phase 0 consumer needs either field.

### P0-03 — DecisionKind.ManeuverSelect=3 + UT0151 analyzer

PASS.

- `DecisionKind.ManeuverSelect = 3` added as 4th enum value. Verified by
  `DecisionKind_ManeuverSelect_ValueIs3` (asserts `(int)ManeuverSelect == 3`).
- `UT0151_ManeuverSelectInvalidContext` diagnostic added to `SharedUtilityDiagnostics.cs`.
- `UtilityAuthoringAnalyzer` SupportedDiagnostics updated; `CheckManeuverSelectContextBinding`
  fires UT0151 when a ManeuverSelect Build method accesses `.Candidate` or `.Target` member.
- Pattern mirrors the existing `CheckPostureSelectContextBinding` — consistent style.
- `ManeuverSelect_InCatalog_WithCorrectKind` integration test checks all 3 requirements
  (enum value, analyzer diagnostic type, no regression in UT0101/UT0102 existing rules). PASS.

### P0-04 — DangerAreaDescriptor + IDangerAreaProvider + FakeDangerAreaProvider

PASS.

- `DangerAreaDescriptor`: 68 B sequential. Layout doc comment matches actual offsets.
  `PinnedSize = 68` constant. Verified by `DangerAreaDescriptor_PinnedSize_MatchesActual`.
- `DangerAreaKind` enum: Unknown=0, OpenGround=1, StreetCrossing=2, Intersection=3,
  ChokePoint=4, CrestLine=5. PASS. (Design said OpenGround=0 but starting at 0 for Unknown
  and shifting by 1 is a safe deviation — Unknown=0 is the correct sentinel choice.)
- `IDangerAreaProvider.Refresh` signature: `(EntityRepository, Entity, Span<DangerAreaDescriptor>,
  out int)`. Zero-alloc on hot path.
- `FakeDangerAreaProvider`: fluent `Add` method builds descriptors; `Fnv1a32` is stable UTF-8
  FNV-1a-32; pinned value `Fnv1a32("street-east-01") == 0x720E1D2C` verified by test.
- `FakeDangerAreaProvider_ZeroAllocsOnRefresh` confirms no heap allocations during Refresh.
- `Fnv1a32` uses `Encoding.UTF8.GetBytes` (heap alloc) only in the helper — acceptable in
  test-only code. Not called on the hot path.

### P0-05 — Integration gate tests

PASS.

- `Layout_WriteManeuverKind`: writes ManeuverKind to SquadCognitiveState, reads back via
  Project — confirms end-to-end projection correctness.
- `DangerArea_RoundTrip_4Features`: adds 4 descriptors (all kinds), refreshes into 16-slot
  span, verifies counts and field values. Solid boundary test.
- `ManeuverSelect_InCatalog_WithCorrectKind`: confirms DecisionKind.ManeuverSelect==3 and
  UT0151 diagnostic descriptor is registered.

---

## Test Quality Assessment

| Dimension | Rating |
|-----------|--------|
| Layout/size pinning | Excellent — unsafe pointer arithmetic, not just sizeof |
| Aliasing safety | Good — slot-7 no-aliasing test present |
| Projection aliasing | Good — write-and-read-back via Project() |
| Round-trip coverage | Good — 4-feature danger area round-trip |
| Hash pinning | Good — exact expected value asserted |
| Zero-alloc coverage | Good — GC.GetTotalMemory before/after pattern |
| Integration gating | Adequate — 3 integration tests cover main cross-concerns |

One minor gap: no test verifies `SquadStateMarker` component size is 1 byte (Size=1 in
StructLayout). Not blocking — the `[StructLayout(Sequential, Size=1)]` attribute is correct
and non-zero-size is inert for ECS purposes.

---

## Regressions

None. 66 ThreatMatrix/StandardInputs/StarterPack tests pass. 1742 total, 58 failures (all
pre-existing).

---

## Action Items for BATCH-21

1. Implement Phase 1 tasks (P1-01 through P1-05) — primitives library.
2. Track P2 debt: `SquadCognitiveState._scalarPad` -> `Flags` and `SquadContact._pad` ->
   `SourceMembersMask` before Phase 2/3 respectively.
