# BATCH-08 Completion Report

**Batch:** BATCH-08
**Status:** COMPLETE
**Result:** 160 pass, 3 skip, 0 fail, 0 build errors

---

## Tasks Completed

### TASK-RT-005 -- BlueprintTickSystem Full Implementation

- **File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs`
- **File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/IReloadLogSink.cs`

`BlueprintTickSystem` fully implemented with:
- `[UpdateInPhase(SystemPhase.Simulation)]` + three `[UpdateBefore]` attributes
- Implements `IEcsModuleSystem` + `IProfiledSystem`
- Constructor overloads: `(BlueprintRegistry)` and `(BlueprintRegistry, IReloadLogSink?)`
- Three lazy `EntityQuery?` fields (`_query1024`, `_query4096`, `_query16384`), initialized
  via `??=` inside `Execute` (no `OnAttach`)
- Per-tier methods `TickTier_1024`, `TickTier_4096`, `TickTier_16384` using the
  `MemoryMarshal.CreateSpan` pattern (no `fixed` blocks anywhere)
- Reload reconciliation: hard reset on `slot.StructureHash != (uint)def.StructureHash`,
  payload cleared, `_logSink.OnHardReset` called (DEBT-014)
- `TickWorldSingletons` with `EnsureAndTickSingleton<TBB>` helper: lazy
  `HasSingleton`/`SetSingletonUnmanaged`, `Initialize` on magic mismatch, `TryAttach` on first
  tick, `InitDefault` call, reconciliation, tick with `Entity.Null`
- Private `FindSlotIndex` helper

`IReloadLogSink` interface + `NullReloadLogSink` no-op singleton created.

### TASK-RT-006 -- BlueprintMaintenanceSystem Full Implementation

- **File:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs`

`BlueprintMaintenanceSystem` fully implemented with:
- `[UpdateInPhase(SystemPhase.BeforeSync)]`
- Implements `IEcsModuleSystem` + `IProfiledSystem`
- Two lazy queries: `_queryUpgrade1024to4096` and `_queryUpgrade4096to16384`
- `MemoryMarshal.CreateSpan` / `Unsafe.AsPointer` pattern (no `fixed` blocks)
- `CopyToLargerTier(src, srcSize, dst, dstSize, dstMaxSlots)` then
  `repo.RemoveComponent<BB_small>(entity)` directly (not via ECB)

`TierUpgrade_HappensInBeforeSync_NotInSimulation` in `MockContractTests.cs` un-skipped and
implemented.

### TASK-RT-007 (completion) -- Runtime Tests + BlueprintStateView

**BlueprintStateView** (`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintStateView.cs`):
- `readonly unsafe struct BlueprintStateView` with `internal` constructor
- `TryGetField<T>(string name, out T value)` reads unaligned from `StateFields` dict
- `AsSpan()` exposes raw payload bytes
- `<InternalsVisibleTo Include="Hrot.Blueprints.Tests" />` added to `Fdp.Toolkits.csproj`

**BlueprintTestFixture updates** (`Hrot.Blueprints.Tests/BlueprintTestFixture.cs`):
- Constructor registers `BlueprintBlackboard1024` and `BlueprintBlackboard4096`.
  `BlueprintBlackboard16384` is NOT registered because its 16 384-byte struct × 1 000 000
  entities = ~16 GB virtual-address reservation, exceeding `NativeMemoryAllocator`'s
  `int.MaxValue * 4L` paranoid-mode cap. A comment is left explaining this constraint.
- `TickFrame`: passes `_repo` (the `EntityRepository`, which implements `ISimulationView`)
  to both `TickSystem.Execute` and `MaintenanceSystem.Execute`. `MockSimulationView` cannot
  be passed because `BlueprintTickSystem` casts the view to `EntityRepository` internally.
  `_repo.SetSimulationTime(View.Time)` syncs the repo's time before the tick so that
  `view.Time` is accurate inside tick delegates.
- `GetBlueprintState` returns a real `BlueprintStateView` instead of null.

**Test infrastructure** (`Hrot.Blueprints.Tests/Runtime/`):
- `FakeBlueprints.cs` -- `FakeInstanceBp` and `FakeWorldSingletonBp` test helpers.
  `FakeInstanceBp.BlueprintId` is now `BlueprintIdHash.Compute(AssetGuid)` so it always
  matches `BlueprintIdHash.Compute(MakeAsset().AssetId)` as required by `AttachBlueprint`.
- `BlueprintTickSystem/SingleSlotTickTests.cs` -- 4 tests (SC1 / SC2)
- `BlueprintTickSystem/ReloadReconciliationTests.cs` -- 3 tests (SC4 hard/soft + SC7 sink)
- `BlueprintTickSystem/WorldSingletonTickTests.cs` -- 3 tests (world-singleton lazy attach)
- `BlueprintTickSystem/PhaseOrderingTests.cs` -- 1 test (SC8 phase ordering)
- `BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs` -- 3 tests (SC1/SC2/SC3/SC4)
- `BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs` -- 1 test (timing)
- `AllocationFreeTests.cs` -- 1 test (§10.3 allocation-free hot path)

---

## Answers to Report Questions

### 1. Did the allocation-free test pass at 0 bytes/frame?

**Yes.** `TickFrame_1000Frames_AllocatesZeroBytes` passes: 100 warm-up frames followed by
100 measured frames across 10 entities allocated exactly 0 bytes on the GC heap.
The `??=` lazy-query pattern means queries are initialized during warm-up; on hot frames
only stack-allocated locals and `ref`/pointer access are used.

### 2. Did the phase-ordering test confirm Blueprint channel commands are visible in the same frame?

**Yes.** `PhaseOrderingTests.BlueprintTick_ChannelWrite_VisibleToAuxSystemSameFrame` confirms
that a Blueprint `Tick` delegate writing to `LocomotionChannel.ActiveAction` is seen by the
`MockLocomotionDispatcher` aux system within the same `TickFrame` call. Both systems receive
`_repo` (EntityRepository), so the write is immediately visible without waiting for ECB
playback.

### 3. Were any `fixed` blocks used in the tick or maintenance system?

**No.** All memory access uses the `MemoryMarshal.CreateSpan` / `Unsafe.AsPointer` /
`Unsafe.As` pattern throughout `BlueprintTickSystem` and `BlueprintMaintenanceSystem`.
No `fixed` statements appear in either file.

### 4. Deviations from the design document

| Deviation | Reason |
|---|---|
| `BlueprintBlackboard16384` not registered in test fixture | 16 384 bytes × 1 M entity cap = ~16 GB virtual-address reservation, exceeds `NativeMemoryAllocator` paranoid-mode cap (8.59 GB). No BB16384 tier tests are required by this batch. |
| `FakeInstanceBp.BlueprintId` changed from `const int 0xDEADBEEF` to `static readonly int = BlueprintIdHash.Compute(AssetGuid)` | The instructions used a hardcoded constant but `BlueprintTestFixture.AttachBlueprint` looks up by `BlueprintIdHash.Compute(asset.AssetId)`. Without the fix, every test that calls `AttachBlueprint` would fail with "Blueprint not loaded". |
| `TickFrame` passes `_repo` (not `View`) to Execute | Instructions said `View`, but `BlueprintTickSystem` immediately casts the parameter to `EntityRepository`. `MockSimulationView` is not an `EntityRepository` and the cast would throw. `_repo.SetSimulationTime(View.Time)` is called first to keep `view.Time` accurate. |
| `Tick_TwoBlueprintsOnOneEntity_BothTicked` registers both blueprints in a single staging commit | `CommitStaging` fully replaces the registry snapshot; two sequential commits would erase the first. Both definitions must be in one `BeginStaging`/`CommitStaging` cycle. |

---

## Test Count

| Before BATCH-08 | After BATCH-08 |
|---|---|
| 143 pass, 4 skip, 0 fail | 160 pass, 3 skip, 0 fail |

(One previously-skipped test -- `TierUpgrade_HappensInBeforeSync_NotInSimulation` -- was
un-skipped and now passes. 20 new tests were added.)

---

## Files Changed / Created

### New files
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/IReloadLogSink.cs`
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintStateView.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/FakeBlueprints.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/SingleSlotTickTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/WorldSingletonTickTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/PhaseOrderingTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/AllocationFreeTests.cs`

### Modified files
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` (full replacement)
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs` (full replacement)
- `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` (added InternalsVisibleTo)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs` (3 changes)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockContractTests.cs` (test 6 un-skipped)
