# BATCH-06 Review

**Batch:** BATCH-06 -- Phase 2 Runtime Foundation (RT-001, RT-002, RT-003)
**Reviewer:** Dev Lead
**Verdict:** APPROVED WITH CORRECTIONS (P2 items in BATCH-07)

---

## Summary

BATCH-06 is complete. 127 tests pass, 5 skipped, 0 failures, full solution builds clean.
RT-001 (BlueprintRegistry), RT-002 (BlueprintDefinition + delegates + cursor), and RT-003
(Blackboard layout types) are all implemented. Key structural deviations are documented below;
two P2 items must be handled before the partition allocator (BATCH-07).

---

## Scope Check

- **TASK-RT-001:** COMPLETE. Full BlueprintRegistry with `int blueprintId` keys, duplicate
  guards, WorldSingletonList pre-materialization, `OnRegistryChanged`, collision-throwing
  staging. `BlueprintIdHash.Compute` (FNV-1a) created. 11 tests pass all 7 SCs.

- **TASK-RT-002:** COMPLETE. `BlueprintDefinition` is now a `sealed record` with all required
  fields. `InitDefaultDelegate`, `TickDelegate`, `EventHandlerDelegate` created. `BlueprintLatentCursor`
  corrected (uint ResumeAt + float WaitUntilTime). `BlueprintFieldDescriptor` sealed record created.
  `BlueprintRegistrarAttribute` fixed (`Inherited = false`). `BlueprintDispatchKind` duplicated in
  `Fdp.Toolkit.Blueprints` (see P3 note). 10 tests.

- **TASK-RT-003:** COMPLETE. Layout constants added to all three Blackboard tier components.
  `Memory` field name used (was `Data`). ComponentId values corrected (B4096=205, B16384=206).
  `BlueprintBlackboardHeader` (32 bytes), `BlueprintSlotEntry` (16 bytes), `BlueprintFreeBlockHeader`
  (4 bytes) created. 16 tests covering all 7 SCs.

---

## Design Alignment

### P2 Defects

**P2-BATCH06-001: `BlueprintSlotEntry.StructureHash` is `uint` (32-bit), not `ulong` (64-bit)**

TASK-RT-003 spec requires `StructureHash (ulong)` in a 16-byte slot entry alongside
`BlueprintId (int)`, `InstanceVersion (uint)`, `PayloadOffset (ushort)`, `PayloadSize (ushort)`.
The field sizes total 4+4+2+2+8 = 20 bytes, which cannot fit in 16. The developer correctly
identified this spec contradiction and used `uint StructureHash` (4 bytes) to keep the struct
exactly 16 bytes.

**Impact on BATCH-07 (RT-005):** The `BlueprintTickSystem` reload reconciliation compares
`slot.StructureHash (uint)` with `def.StructureHash (ulong)`. The developer must use
`slot.StructureHash == (uint)def.StructureHash` (lower 32 bits only). This must be explicit
in the comparison code to avoid silent truncation. Document the truncation with a comment.

**Action for BATCH-07 Corrective Task 0:**
- Add XML comment to `BlueprintSlotEntry.StructureHash`: "Lower 32 bits of the Blueprint's
  64-bit structure hash. Stored as uint to fit the 16-byte slot-entry budget."
- In `BlueprintTickSystem` reload detection: `slot.StructureHash != (uint)def.StructureHash`
  with a comment "// Truncation: slot entry stores only lower 32 bits."

**P2-BATCH06-002: `StateFields` is `IReadOnlyList<BlueprintFieldDescriptor>` (not dict)**

TASK-RT-002 specifies `StateFields (IReadOnlyDictionary<string, BlueprintFieldDescriptor>)`.
The developer used `IReadOnlyList<>` instead, without documenting the reason. The dict form
is required by `BlueprintStateView.GetField<T>(string fieldName)` which must look up fields
by name. Using a list requires a linear scan; using a dict gives O(1) lookup.

However, `BlueprintStateView` is a Phase 1 stub (currently returns null for GetField) so no
regression exists today. This must be corrected in BATCH-07 when BlueprintBlackboardPartitions
is real and `BlueprintStateView.GetField` is implemented for real.

**Action for BATCH-07 Corrective Task 0:**
- Change `StateFields` type from `IReadOnlyList<BlueprintFieldDescriptor>` to
  `IReadOnlyDictionary<string, BlueprintFieldDescriptor>`. Default to empty dict.
- Update `BlueprintDefinitionTests` to match.

### Acceptable Deviations

**`BlueprintDispatchKind` duplicated in `Fdp.Toolkit.Blueprints`:**
The design says "do NOT redefine if already in `Hrot.Blueprints.Core.Assets`." However,
`Fdp.Toolkits` cannot reference `Hrot.Blueprints.Core` (wrong dependency direction). The
developer correctly created a second enum in `Fdp.Toolkit.Blueprints`. The two enums have
identical values and no runtime interaction. This is a permanent architectural compromise.
Track as P3 debt (DEBT-013): document in a code comment on both enums that they are mirrors.

**FNV-1a for `BlueprintIdHash.Compute` (vs "first 4 bytes"):**
The instructions offered "first 4 bytes as int" as a suggestion. FNV-1a is a better hash
function with uniform distribution. Acceptable deviation.

**EventHandlerDelegate has 7 parameters (not 8):**
TASK-DETAIL mentions 8 params in one place but the Runtime DD §3.3 explicitly shows 7.
Developer used 7, which is correct.

---

## Test Quality Assessment

### BlueprintRegistryTests (11 tests)

GOOD. All 7 SCs covered. SC3 tests both `Count == 1` and same-reference check (hot-path
zero-alloc requirement verified). SC4 tests both direct-registration duplicate AND staging
duplicate. SC7 tests both non-empty staging and empty staging. Assertions check specific
values, not just non-null.

### BlueprintDefinitionTests (10 tests)

GOOD. SC2 uses `Unsafe.SizeOf<BlueprintLatentCursor>() == 16`. SC3 verifies unmanaged
constraint via a generic helper method with `where T : unmanaged`. SC4 tests non-inheritable
attribute. SC5 verifies delegate parameter counts via reflection. SC6 uses record `with { }`
copy to test value equality -- correct approach given Dictionary limitation.

### BlackboardLayoutTests (16 tests)

GOOD. SC1/SC2 use `Unsafe.SizeOf<>()`. SC3 constants are verified algebraically (not
hardcoded). SC4 tests `MaxSlots * SlotEntrySize == SlotTableSize`. SC5 uses reflection on
`ComponentIdAttribute`. SC6 creates a default struct and verifies zeroed-out memory.

---

## Developer Insights Extraction (for DEBT-TRACKER)

- **DEBT-013 (P3):** `BlueprintDispatchKind` enum exists in both `Hrot.Blueprints.Core.Assets`
  and `Fdp.Toolkit.Blueprints`. Required because cross-assembly reference is impossible.
  Both should carry a comment: "Mirror of Fdp.Toolkit.Blueprints.BlueprintDispatchKind /
  Hrot.Blueprints.Core.Assets.BlueprintDispatchKind -- kept in sync manually."

- **DEBT-014 (P2):** `BlueprintSlotEntry.StructureHash` is `uint` (not `ulong` per spec) due
  to 16-byte struct budget constraint. In `BlueprintTickSystem` reload detection, compare with
  `(uint)def.StructureHash`. Comment the truncation. Addressed as CT0 in BATCH-07.

- **DEBT-015 (P2):** `StateFields` on `BlueprintDefinition` is `IReadOnlyList<>` instead of
  `IReadOnlyDictionary<string, BlueprintFieldDescriptor>`. Must be corrected before
  `BlueprintStateView.GetField` is implemented in Phase 2 systems. Addressed as CT0 in BATCH-07.

---

## Test Execution Results

```
Build succeeded. 0 warnings, 0 errors (full solution).
Passed! - Failed: 0, Passed: 127, Skipped: 5, Total: 132, Duration: ~430 ms
```

---

## Suggested Git Commit Message

```
feat(blueprints): BATCH-06 -- Phase 2 Runtime foundation types

- Add: BlueprintRegistry full implementation (int blueprintId, staging, collision guards)
- Add: BlueprintIdHash.Compute (FNV-1a Guid->int)
- Add: BlueprintDefinition sealed record with all fields + delegate types
- Add: BlueprintFieldDescriptor, BlueprintDelegates.cs, BlueprintDispatchKind
- Fix: BlueprintLatentCursor (uint ResumeAt + float WaitUntilTime, was Guid GraphId)
- Fix: BlueprintRegistrarAttribute (Inherited = false)
- Add: BlueprintBlackboard{1024,4096,16384} layout constants (MaxSlots, PayloadStart etc.)
- Fix: Blackboard ComponentIds corrected (B4096=205, B16384=206)
- Add: BlueprintBlackboardHeader (32B), BlueprintSlotEntry (16B), BlueprintFreeBlockHeader (4B)
- Add: 37 new runtime tests (11 registry, 10 definition, 16 layout)
- Tests: 132 total (127 pass, 5 skip)
- P2 notes: StructureHash uint truncation and StateFields type to be corrected in BATCH-07 CT0
```

---

## TASK-TRACKER Updates

- [x] TASK-RT-001 -- COMPLETE
- [x] TASK-RT-002 -- COMPLETE
- [x] TASK-RT-003 -- COMPLETE
