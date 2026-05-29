# BATCH-01 Report

**Batch:** BATCH-01 — Utility AI Phase 0 Prerequisites  
**Date:** 2025-01-27  
**Status:** Complete

---

## Task Completion

| Task ID          | Status | Notes                                                        |
|------------------|--------|--------------------------------------------------------------|
| TASK-UAI-P0-01   | [x]    | `WeaponState.MaxAmmo` added; spawn site updated              |
| TASK-UAI-P0-02   | [x]    | `WeaponMountInfo`, `WeaponMountQuery`, multi-mount translator |
| TASK-UAI-P0-03   | [x]    | `MaxTrackedTargets = 16`; perception tests updated           |
| TASK-UAI-P0-04   | [x]    | `UnitRoster.Add` / `IndexOf` static methods                  |
| TASK-UAI-P0-05   | [x]    | `Blackboard1024.Project<T>` projection method                |
| TASK-UAI-P0-06   | [x]    | `UtilityTestWorld` helper (all 7 spawn helpers)              |
| TASK-UAI-P0-07   | [x]    | `Phase0_Bundle_Integration` gate test                        |

---

## Testing Results

**P0 batch tests:**  Passed: **38 / 38** (0 failures)

**Full suite:**  Passed: 1519 / 1573  (54 failures — all pre-existing, none in P0 scope)

**P0 test breakdown:**

| Test file                     | Tests | Status  |
|-------------------------------|-------|---------|
| `WeaponStateTests.cs`         |   5   | All pass |
| `WeaponMountTests.cs`         |   7   | All pass |
| `PerceptionComponentTests.cs` |   8   | All pass |
| `UnitRosterTests.cs`          |   5   | All pass |
| `Blackboard1024Tests.cs`      |   4   | All pass |
| `UtilityTestWorldTests.cs`    |   8   | All pass |
| `Phase0IntegrationTests.cs`   |   1   | All pass |

New test methods added by BATCH-01: **30** (excluding the 8 updated perception tests).

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`GlobalComponentIds.WeaponMountInfo`** — the constant ID `216` had to be registered in `GlobalComponentIds.cs`.  The existing pattern of reserving a contiguous block made the insertion straightforward; `WeaponMountInfo = 216` was placed in the Combat region immediately after existing weapon IDs.

2. **Multi-mount child-entity pattern** — The spec required that index 0 stays on the owner entity and indices 1+ get child entities with `PartMetadata` back-linking to the owner.  This was modelled after the `PartMetadata` precedent already present in the Replication toolkit; the translator iterates mounts, calls `repo.CreateEntity()` for each additional mount, and copies the `WeaponState` plus `WeaponMountInfo` and `PartMetadata` onto the child.

3. **AimAndFireExecutor cooldown drain bug (Batch-01 review fix)** — The test file `AimAndFireExecutorTests.cs` contained a `// Batch-01 review fix` marker.  Inspection confirmed the executor returned `Running` when `CooldownSecondsRemaining > 0` but never decremented the counter, so the cooldown timer never expired.  Fixed by adding `weapon.CooldownSecondsRemaining -= dt;` before the early return, matching the drain-then-fire semantics that the two newly failing tests (`AimAndFire_DoesNotFire_WhenCooldownActive`, `AimAndFire_DrainsCooldown_ByDt_UntilCanFire`) required.  Both tests now pass.

4. **Pre-existing `CombatComponentTests` struct-size failures (4 tests)** — These were failing before BATCH-01 started and are NOT caused by P0 work.  Root cause: `bool IsRemote` was added to `WeaponFireIntent`, `WeaponFireNotification`, `DetonationNotification`, and `DamageAssessedEvent` after the size-assertion tests were written.  `Marshal.SizeOf` marshals a plain `bool` as a 4-byte Windows `BOOL`, inflating each struct by 4 bytes.  The tests expect sizes that predate `IsRemote`.  Fix would require adding `[MarshalAs(UnmanagedType.I1)]` to each `bool IsRemote` field and updating the expected values by +1; left out of scope for BATCH-01 to avoid expanding the diff.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `WeaponFireEvents.cs` / `DetonationNotification.cs` / `DetonationEvents.cs` — `bool` fields in wire structs silently grow to 4 bytes under `Marshal.SizeOf`.  Adding a code-style rule (`[MarshalAs(UnmanagedType.I1)]` mandatory for booleans in serializable structs, or use `byte` instead) would prevent future silent bloat.

- `UnitRoster` — the backing arrays are fixed-size stack fields (`FixedDesignation`, `SubordinateEntities`).  Consider adding an overflow guard (`Debug.Assert` or `throw`) inside `Add` instead of the current silent return of `-1`; silent capacity overflow is easy to miss during development.

**Q3: What design decisions did you make beyond the instructions?**

- **`WeaponMountQuery` query API:** Used `repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build()` (two `With<>` constraints).  The spec only required iterating mounts; the `PartMetadata` constraint was added to restrict results to child entities only (the owner mount does not have `PartMetadata`), avoiding double-counting the owner in multi-mount scenarios.

- **`UtilityTestWorld.SpawnLeader` / `SpawnSquadMember`:** These two helpers went beyond the minimum spawn set specified in TASK-UAI-P0-06.  They were added because `Phase0_Bundle_Integration` (P0.07) exercises the roster linkage, and inline roster plumbing inside the integration test would have been verbose.  Three additional `UtilityTestWorldTests` cover these helpers directly.

- **`UnitRoster.Add` with designation overload:** The instruction specified `Add(Entity e)` returning `int`.  The implementation also accepts an optional `ReadOnlySpan<char> designation` to write into the parallel `FixedDesignation` array that already existed in the struct.  The extra test `Add_WithDesignation_StoresDesignationInParallelSlot` validates this path.

**Q4: What edge cases did you discovered that weren't mentioned in the spec?**

- **`Entity.PackedValue` is `ulong`, not `long`:** `UnitRoster.SubordinateEntities` is a `FixedList512Bytes<long>` (per existing code).  Storing an entity requires `(long)entity.PackedValue`.  The cast is safe for all live entities (high bit is never set in practice) and matches how the existing HillAttack scenario code stores entity handles.

- **`Blackboard1024.Project<T>` aliasing across struct types:** The spec required write-through but did not explicitly call out that two different value types projected at offset 0 fully alias each other.  The test `Project_TwoDifferentStructTypes_AreAliased` validates this, which matters for Phase-1 code that will project its own scorer-specific structs over the same blob.

- **`WeaponMountInfo` on owner entity vs. child entities:** The owner entity gets `WeaponMountInfo` with `MountIndex = 0` but no `PartMetadata` component.  The `EnumerateMounts` query therefore only sees child entities.  The `WeaponMountTests.EnumerateMounts_ThreeMounts_ReturnsOwnerThenChildrenInOrder` test had to query the owner's weapon state separately before enumerating via `EnumerateMounts`.  The integration test handles this correctly.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `Blackboard1024.Project<T>` uses `MemoryMarshal.Cast<byte, T>` which is a zero-copy slice — no allocation, no copy. Appropriate for per-frame use in a hot ECS tick loop.

- `WeaponMountQuery.EnumerateMounts` builds a query per call.  If this is called frequently (e.g., once per entity per frame), caching the built query would reduce allocations.  For Phase 0 this is acceptable; worth revisiting in Phase 1 when the scorer calls it per-frame.

---

## Outstanding Issues / Next Steps

- [ ] **Pre-existing CombatComponentTests failures (4):** Add `[MarshalAs(UnmanagedType.I1)]` to `bool IsRemote` in `WeaponFireIntent`, `WeaponFireNotification`, `DetonationNotification`, and `DamageAssessedEvent`; update test size expectations to include the 1-byte bool (+1 each for Pack=1 structs, +1 and add Pack=1 for `DamageAssessedEvent`).  These 4 failures pre-date BATCH-01 and are tracked separately.

- [ ] **Remaining 50 pre-existing failures** (ReplayBrowser export, Navigation integration, SimTransformBridgeSystem, BicycleModel, Gizmos, IdAllocation, FdpAutoSerializerFixedBuffer, ReferenceHandler) — all out of scope for BATCH-01.

---

## Suggested Commit Message

```
feat(utility-ai): Phase 0 prerequisites (BATCH-01)

- WeaponState.MaxAmmo field + spawn initialisation (P0.01)
- WeaponMountInfo component, WeaponMountQuery, multi-mount translator (P0.02)
- PerceptionConstants.MaxTrackedTargets raised to 16 (P0.03)
- UnitRoster.Add / IndexOf static methods (P0.04)
- Blackboard1024.Project<T> write-through projection (P0.05)
- UtilityTestWorld helper + 7 spawn helpers (P0.06)
- Phase0_Bundle_Integration gate test (P0.07)
- Fix AimAndFireExecutor cooldown drain (Batch-01 review fix)
- 30 new test methods; all 38 P0 tests pass
```
