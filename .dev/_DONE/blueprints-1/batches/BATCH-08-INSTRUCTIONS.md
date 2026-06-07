# BATCH-08 Instructions

**Branch:** `blueprints`
**Workspace root:** `d:\WORK\IOS-IG-SimHost-FDP`

**Scope:** TASK-RT-005 (BlueprintTickSystem), TASK-RT-006 (BlueprintMaintenanceSystem),
and TASK-RT-007 completion (BlueprintTickSystem + MaintenanceSystem tests + AllocationFreeTests).
Also: implement `BlueprintStateView` (currently a stub) so tests can verify slot state.

**Design references:**
- `.dev/blueprints-1/TASK-DETAIL.md` sections TASK-RT-005, TASK-RT-006, TASK-RT-007
- `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md` §6, §7, §8, §9, §11
- `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design_InlinePatches.md` Q-12.1 through Q-12.4, Corrections 1 and 2

---

## Important Engine API Notes (read before implementing)

1. **`IEcsModuleSystem.Execute` takes TWO parameters:**
   ```csharp
   void Execute(ISimulationView view, float deltaTime);
   ```
   Both `BlueprintTickSystem` and `BlueprintMaintenanceSystem` must implement this signature.

2. **`ISimulationView` has `float Time` but NOT `DeltaTime`.** Pass the `deltaTime` parameter
   directly to tick delegates:
   ```csharp
   def.Tick(span, view, ecb, entity, view.Time, deltaTime, slot.InstanceVersion);
   ```

3. **`MemoryMarshal.CreateSpan` (not `fixed` + `new Span<byte>`)** is the engine's canonical
   pattern (Hot-path Correction 2 in InlinePatches). See the corrected §6.3 in InlinePatches.

4. **Lazy query init via `??=`** inside `Execute` (Q-12.3 in InlinePatches). No `OnAttach` callback.

5. **`(EntityRepository)view` cast** is canonical, not brittle (Q-12.2).

6. **`StructureHash` comparison uses truncation:**
   ```csharp
   if (slot.StructureHash != (uint)def.StructureHash)  // DEBT-014
   ```

7. **`RemoveComponent<T>` is a direct call during `BeforeSync`** -- not via ECB.
   ```csharp
   repo.RemoveComponent<BlueprintBlackboard1024>(entity);
   ```

---

## TASK-RT-005 — BlueprintTickSystem Full Implementation

### File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs`

Replace the stub entirely. The full implementation is described in Runtime DD §6 with the
corrections from Runtime DD InlinePatches §Q-12.2, Q-12.3, Q-12.4, Correction 2.

### New class structure

```
namespace Fdp.Toolkit.Blueprints.Systems
using: Fdp.Core, Fdp.ModuleHost.Abstractions, Fdp.Toolkit.Blueprints,
       Fdp.Toolkit.Blueprints.Partitioning, Fdp.Toolkit.Blueprints.Components,
       System.Runtime.CompilerServices, System.Runtime.InteropServices
```

Attributes:
- `[UpdateInPhase(SystemPhase.Simulation)]`
- `[UpdateBefore(typeof(LocomotionDispatcherSystem))]`
- `[UpdateBefore(typeof(WeaponDispatcherSystem))]`
- `[UpdateBefore(typeof(InteractionDispatcherSystem))]`

Implements: `IEcsModuleSystem`, `IProfiledSystem`

### `IReloadLogSink` interface and `NullReloadLogSink`

Create `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/IReloadLogSink.cs`:

```csharp
namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>Called by BlueprintTickSystem when a hard-reload reset occurs for a slot.</summary>
public interface IReloadLogSink
{
    void OnHardReset(int blueprintId, uint newInstanceVersion);
}

/// <summary>No-op singleton implementation.</summary>
public sealed class NullReloadLogSink : IReloadLogSink
{
    public static readonly NullReloadLogSink Instance = new();
    private NullReloadLogSink() { }
    public void OnHardReset(int blueprintId, uint newInstanceVersion) { }
}
```

### BlueprintTickSystem constructor

```csharp
private readonly BlueprintRegistry _registry;
private readonly IReloadLogSink _logSink;

public BlueprintTickSystem(BlueprintRegistry registry)
    : this(registry, NullReloadLogSink.Instance) { }

public BlueprintTickSystem(BlueprintRegistry registry, IReloadLogSink? logSink = null)
{
    _registry = registry;
    _logSink  = logSink ?? NullReloadLogSink.Instance;
}
```

### Lazy queries and Execute

```csharp
private EntityQuery? _query1024;
private EntityQuery? _query4096;
private EntityQuery? _query16384;

public void Execute(ISimulationView view, float deltaTime)
{
    var repo = (EntityRepository)view;
    var ecb  = view.GetCommandBuffer();

    _query1024  ??= repo.Query().With<BlueprintBlackboard1024>().Build();
    _query4096  ??= repo.Query().With<BlueprintBlackboard4096>().Build();
    _query16384 ??= repo.Query().With<BlueprintBlackboard16384>().Build();

    TickTier_1024(repo, view, ecb, deltaTime);
    TickTier_4096(repo, view, ecb, deltaTime);
    TickTier_16384(repo, view, ecb, deltaTime);

    TickWorldSingletons(repo, view, ecb, deltaTime);
}
```

Note: use `EntityQuery?` (concrete type, not `IEntityQuery?`). Check existing engine systems
(`MovingEntitySystem`, `InteractionDispatcherSystem`) for the exact field type.

### Per-tier tick method (MemoryMarshal.CreateSpan pattern)

Use the corrected pattern from InlinePatches Correction 2. Key pattern (for BB1024, B4096 and
B16384 are mechanical copies with the component type swapped):

```csharp
private unsafe void TickTier_1024(
    EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb, float deltaTime)
{
    foreach (var entity in _query1024!)
    {
        ref var bb     = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory   = (byte*)Unsafe.AsPointer(ref memRef);

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue) continue;

        int   slotCount = header.SlotCount;
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < slotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

            if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

            // Reload reconciliation (hard reset if structure hash changed)
            if (slot.StructureHash != (uint)def.StructureHash) // DEBT-014 truncation
            {
                BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                if (def.InitDefault is not null)
                {
                    var initSpan = MemoryMarshal.CreateSpan(
                        ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                        slot.PayloadSize);
                    def.InitDefault(initSpan);
                }
                _logSink.OnHardReset(slot.BlueprintId, slot.InstanceVersion);
            }

            // Tick
            if (def.Tick is not null)
            {
                var tickSpan = MemoryMarshal.CreateSpan(
                    ref Unsafe.Add(ref memRef, slot.PayloadOffset),
                    slot.PayloadSize);
                def.Tick(tickSpan, view, ecb, entity,
                         view.Time, deltaTime, slot.InstanceVersion);
            }
        }
    }
}
```

**Note on magic value:** Use `BlueprintBlackboardHeader.MagicValue` (the constant defined on
the header struct) rather than hardcoding `0x42504257`.

### TickWorldSingletons (lazy init, per Q-12.4)

Follow the `EnsureAndTickSingleton<TBB>` pattern from InlinePatches §Q-12.4 verbatim.
Key points:
- `repo.HasSingleton<TBB>()` + `repo.SetSingletonUnmanaged<TBB>(default)` if not present
- Same `MemoryMarshal.CreateSpan` pattern (no `fixed`)
- `Entity.Null` for world-singleton ticks (no per-entity Self)
- `FindSlotIndex` private helper returns the slot index given `blueprintId`

Private helper:
```csharp
private static int FindSlotIndex(byte* slotTable, int slotCount, int blueprintId)
{
    for (int i = 0; i < slotCount; i++)
    {
        ref var s = ref Unsafe.AsRef<BlueprintSlotEntry>(
            slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);
        if (s.BlueprintId == blueprintId) return i;
    }
    return -1;
}
```

### Update BlueprintTestFixture

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

1. Change `TickFrame` to pass `deltaTime` to both system `Execute` calls:
   ```csharp
   TickSystem.Execute(View, deltaTime);
   // ...
   MaintenanceSystem.Execute(View, deltaTime);
   ```

2. Register `BlueprintBlackboard4096` and `BlueprintBlackboard16384` in the constructor (currently
   only `BlueprintBlackboard1024` is registered):
   ```csharp
   _repo.RegisterComponent<BlueprintBlackboard1024>();
   _repo.RegisterComponent<BlueprintBlackboard4096>();
   _repo.RegisterComponent<BlueprintBlackboard16384>();
   ```

3. Fix `GetBlueprintState` -- currently it always returns null at the end. Change it to return
   a real view:
   ```csharp
   public unsafe BlueprintStateView? GetBlueprintState(BlueprintAsset asset, Entity entity)
   {
       if (!Registry.TryGetById(BlueprintIdHash.Compute(asset.AssetId), out var def))
           return null;
       if (!TryGetSlotAcrossTiers(asset.AssetId, entity, out var tier, out var payloadOffset))
           return null;

       GetTierMemoryAndMeta(entity, tier, out byte* memory, out _, out _);
       return new BlueprintStateView(memory + payloadOffset, def!.StateSize, def!);
   }
   ```

---

## TASK-RT-006 — BlueprintMaintenanceSystem Full Implementation

### File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintMaintenanceSystem.cs`

Replace the stub. Follow Runtime DD §7 and InlinePatches Q-12.3 (lazy query via `??=`).

Key implementation notes:
- `[UpdateInPhase(SystemPhase.BeforeSync)]` attribute
- Implements `IEcsModuleSystem`, `IProfiledSystem`
- `Execute(ISimulationView view, float deltaTime)` -- deltaTime unused but required by interface
- Two lazy queries: `_queryUpgrade1024to4096` and `_queryUpgrade4096to16384`
- Use `MemoryMarshal.CreateSpan`... actually `CopyToLargerTier` takes raw pointers, not spans.
  Use `ref byte + Unsafe.AsPointer` pattern per InlinePatches Correction 2:
  ```csharp
  ref var oldBB   = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
  ref byte srcRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref oldBB);
  byte* src       = (byte*)Unsafe.AsPointer(ref srcRef);
  // same for dst
  ```
- After `CopyToLargerTier`, call `repo.RemoveComponent<BlueprintBlackboard1024>(entity)` directly.

### Un-skip `TierUpgrade_HappensInBeforeSync_NotInSimulation`

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockContractTests.cs`

Locate the `[Fact(Skip = ...)]` attribute on `TierUpgrade_HappensInBeforeSync_NotInSimulation`
and implement the test body. The test should:
1. Create an entity with `BlueprintBlackboard1024` already attached
2. Also add `BlueprintBlackboard4096` to that entity (simulating the tier-upgrade flag)
3. Call `fixture.TickFrame(dt)` (which runs `MaintenanceSystem` in BeforeSync)
4. Assert the entity has `BB4096` but NOT `BB1024` after the frame

If the mock test already has a reasonable body, remove the Skip attribute instead.

---

## TASK: Implement BlueprintStateView

File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintStateView.cs`

Replace the empty stub with a usable read-only view for tests:

```csharp
namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Read-only view over a single Blueprint instance's blackboard slot.
/// Returned by BlueprintTestFixture.GetBlueprintState for test assertions.
/// </summary>
public readonly unsafe struct BlueprintStateView
{
    private readonly byte* _slotMemory;     // pointer to start of payload
    private readonly int   _payloadSize;
    private readonly BlueprintDefinition _def;

    internal BlueprintStateView(byte* slotMemory, int payloadSize, BlueprintDefinition def)
    {
        _slotMemory  = slotMemory;
        _payloadSize = payloadSize;
        _def         = def;
    }

    /// <summary>
    /// Reads a field by name from the slot's payload using the definition's StateFields dict.
    /// Returns false if field not found or size mismatch.
    /// </summary>
    public bool TryGetField<T>(string name, out T value) where T : unmanaged
    {
        if (!_def.StateFields.TryGetValue(name, out var fd) ||
            fd.SizeBytes != Unsafe.SizeOf<T>())
        {
            value = default;
            return false;
        }
        value = Unsafe.ReadUnaligned<T>(_slotMemory + fd.OffsetBytes);
        return true;
    }

    /// <summary>Returns the raw payload as a read-only span.</summary>
    public ReadOnlySpan<byte> AsSpan()
        => new ReadOnlySpan<byte>(_slotMemory, _payloadSize);
}
```

### Update `GetBlueprintState` in BlueprintTestFixture

`GetBlueprintState` currently returns null always. With the real allocator and new state view,
it can now return a real view. Update it to:
1. Find which tier holds the slot using `TryGetSlotAcrossTiers` (pointer-based version)
2. Get the component pointer
3. Call `TryGetSlotOffset` to get `payloadOffset`
4. Return `new BlueprintStateView(memory + payloadOffset, def.StateSize, def)`

The fixture needs a private helper that returns `(byte* memory, int payloadOffset)` given
`(Guid assetId, Entity entity)`. Since `TryGetSlotAcrossTiers` now returns the tier, you can
derive the component pointer from the tier.

---

## TASK-RT-007 (completion) — Runtime Tests

### FakeInstanceBp helper class

To be reused in all tick tests. Create as a static class in the test namespace
(e.g., in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/FakeBlueprints.cs`):

```csharp
public static class FakeInstanceBp
{
    public const int BlueprintId = unchecked((int)0xDEADBEEF);
    public const ulong StructureHash = 0x0123456789ABCDEFU;

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public BlueprintLatentCursor Cursor;  // 16 bytes
        public int TickCount;                 // 4 bytes
    }

    public static int StateSize => Unsafe.SizeOf<State>();

    public static void InitDefault(Span<byte> bytes) => bytes.Clear();

    public static void Tick(
        Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(
            ref MemoryMarshal.GetReference(bytes));
        s.TickCount++;
    }

    public static BlueprintDefinition MakeDefinition() => new BlueprintDefinition
    {
        Name          = "FakeInstance",
        Kind          = BlueprintDispatchKind.Instance,
        StructureHash = StructureHash,
        StateSize     = StateSize,
        InitDefault   = InitDefault,
        Tick          = Tick,
        StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
        {
            ["TickCount"] = new BlueprintFieldDescriptor(
                "TickCount", typeof(int),
                OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),  // 16
                SizeBytes: sizeof(int),
                CategoryOrEmpty: ""),
        },
    };

    public static void Register(BlueprintRegistry registry, BlueprintAsset asset)
    {
        var staging = registry.BeginStaging();
        staging.Add(BlueprintIdHash.Compute(asset.AssetId), MakeDefinition());
        registry.CommitStaging(staging);
    }
}
```

Also create `FakeWorldSingletonBp` with a similar structure, differing only in
`BlueprintId`, `StructureHash`, and `Name`. World-singleton tests need the registry to
recognize the blueprint as a world singleton.

### Test files to create

**`Runtime/BlueprintTickSystem/SingleSlotTickTests.cs`** -- §11.3 scenarios:
- `Tick_SingleBlueprintSlot_IncrementsTick` (SC1): attach one Blueprint, tick once, verify TickCount == 1
- `Tick_TwoFrames_TickCountIsTwo` (SC1 extended): two frames, TickCount == 2
- `Tick_TwoBlueprintsOnOneEntity_BothTicked` (SC2): two fake blueprints on same entity, both TickCount == 1
- `Tick_SkipsEntity_WithoutBlackboard` (negative): entity without any BB component is not ticked

**`Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs`** -- §11.4:
- `HardReload_ChangedStructureHash_ResetsPayloadAndBumpsVersion` (SC4):
  Attach, tick x2, commit new staging with SAME blueprintId but DIFFERENT StructureHash.
  Tick once. Verify TickCount == 1 (was reset), InstanceVersion == 2.
- `SoftReload_SameStructureHash_PreservesState` (SC4 soft path):
  Attach, tick x2, commit staging with same hash. Tick once. TickCount == 3 (preserved).
- `HardReload_LogSink_CalledExactlyOnce` (SC7):
  Use a capturing sink, hard-reload, assert `OnHardReset` called once per slot.

**`Runtime/BlueprintTickSystem/WorldSingletonTickTests.cs`** -- §11.3 world-singleton:
- `WorldSingleton_AttachedLazily_OnFirstTick` (SC5): register singleton, tick once, verify slot exists
- `WorldSingleton_NotReattached_OnSecondTick` (SC5): tick twice, verify SlotCount stays 1
- `WorldSingleton_InitDefault_CalledOnLazyAttach` (SC5): InitDefault flag set on first tick

**`Runtime/BlueprintTickSystem/PhaseOrderingTests.cs`** -- §11.2:
- `BlueprintTick_CommandVisibleToDispatcher_SameFrame` (SC3):
  Add `BlueprintTickSystem` and `MockLocomotionDispatcher` to the fixture, attach a Blueprint
  that writes to `LocomotionChannel.ActiveAction` in its Tick delegate. After one TickFrame,
  assert the dispatcher observed the command (InvokeCount == 1, non-zero ActiveAction).

**`Runtime/BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs`** -- §11.5:
- `TierUpgrade_WhenBothComponentsPresent_MigratesState` (SC1/SC2):
  Manually add BB1024 + initialize with a slot, also add BB4096 to same entity.
  Call TickFrame (runs MaintenanceSystem in BeforeSync). Assert: entity has BB4096, NOT BB1024,
  and TryGetSlotOffset on BB4096 pointer returns true for the original blueprintId.
- `TierUpgrade_EntityWithOnlyBB1024_NotTouched` (SC3):
  Entity with only BB1024, no BB4096. After TickFrame, entity still has BB1024.
- `TierUpgrade_StatePreserved_AfterUpgrade` (SC4):
  Attach Blueprint + tick once (TickCount == 1). Add BB4096. TickFrame. Verify TickCount == 1
  in the new BB4096 slot.

**`Runtime/BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs`** -- §11.5 timing:
- `TwoFrame_FrameN_BothComponentsPresent`:
  Frame N: both components present (manually set up). Frame N+1: only BB4096 remains.
  (This tests the two-frame migration timing described in Runtime DD §7.2.)

**`Runtime/AllocationFreeTests.cs`** -- §10.3:
- `TickFrame_1000Frames_AllocatesZeroBytes`:
  Set up fixture with 10 entities, each with one FakeInstanceBp attached.
  Warm up 100 frames. Then measure `GC.GetAllocatedBytesForCurrentThread()` across 100 frames.
  Assert delta == 0.

### Success criteria

All existing 143 passing tests continue to pass.

New tests pass:
- `TierUpgrade_HappensInBeforeSync_NotInSimulation` un-skipped and passing
- All new tick system and maintenance system tests

Expected total: approximately 175-185 pass (143 + ~35 new), 3 skip.

Build: `dotnet build IOS-IG-SimHost.sln` completes with 0 errors, 0 warnings.

---

## Output

Write completion report to `.dev/blueprints-1/reports/BATCH-08-REPORT.md`.

Answer these specific questions:
1. Did the allocation-free test pass at 0 bytes/frame? If not, what was allocating?
2. Did the phase-ordering test confirm Blueprint channel commands are visible to
   `MockLocomotionDispatcher` in the same frame?
3. Were any `fixed` blocks used in the tick or maintenance system (they should NOT be)?
4. Any deviations from the design document?
