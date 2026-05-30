# Blueprint Subsystem — Runtime Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Runtime_Detailed_Design.md` from architect's review.
> **Effect:** Two performance refinements (cached singleton list, `MemoryMarshal.CreateSpan` instead of `fixed`+`Span`) and four §12 open-question resolutions.
> **Reads alongside:** the main Runtime DD; nothing in the main doc is invalidated, only refined.

---

## Resolutions to §12 open questions

### Q-12.1 — `[UpdateBefore]` dispatcher names: confirmed

The engine uses exactly the three names the Runtime DD anticipated:
- `LocomotionDispatcherSystem`
- `WeaponDispatcherSystem`
- `InteractionDispatcherSystem`

There is no `SystemPhase.SimulationCognitive` (or similar sub-phase). The explicit `[UpdateBefore]` attributes are the architecturally mandated way to enforce this ordering.

**Action:** No change to the Runtime DD §6.2 phase declaration. Strike the "if a sub-phase abstraction exists, prefer that" hedging in §6.2 and §12.1.

### Q-12.2 — `(EntityRepository)view` cast: standard convention

The `var repo = (EntityRepository)view;` pattern is **not** brittle — it is the engine convention. Engine systems like `CarKinematicsSystem`, `InteractionDispatcherSystem`, and `MissionAdapterSystem` all use this exact pattern to escalate from read-only `ISimulationView` to write-capable `EntityRepository`.

**Action:** Strike the "ugly" and "comment it explicitly" hedging in §6.3 and §12.2. The cast stands as-is, no commentary needed beyond the existing inline note that it's a write-access escalation.

### Q-12.3 — Lazy query caching via `??=` (correction)

The Runtime DD §6.4 proposed building queries in an `OnAttach` callback. **The engine has no such callback.** Engine systems (e.g. `MovingEntitySystem`, `EditorZoneAuthoringSystem`) build queries lazily on first execution using the `??=` operator.

**Action:** Replace §6.4 with the lazy pattern.

#### Updated §6.4 — Lazy query caching

```csharp
public sealed class BlueprintTickSystem : IEcsModuleSystem, IProfiledSystem
{
    private readonly BlueprintRegistry _registry;
    private IEntityQuery? _query1024;
    private IEntityQuery? _query4096;
    private IEntityQuery? _query16384;

    public BlueprintTickSystem(BlueprintRegistry registry) => _registry = registry;

    public void Execute(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        var ecb = view.GetCommandBuffer();

        _query1024  ??= repo.Query().With<BlueprintBlackboard1024>().Build();
        _query4096  ??= repo.Query().With<BlueprintBlackboard4096>().Build();
        _query16384 ??= repo.Query().With<BlueprintBlackboard16384>().Build();

        TickTier_1024(repo, view, ecb);
        TickTier_4096(repo, view, ecb);
        TickTier_16384(repo, view, ecb);

        TickWorldSingletons(repo, view, ecb);
    }
    // ...
}
```

This is the engine's idiomatic pattern. The first `Execute` call pays the query-build cost; every subsequent call uses the cached query reference. No lifecycle callback required.

Same pattern applies to `BlueprintMaintenanceSystem`:

```csharp
public sealed class BlueprintMaintenanceSystem : IEcsModuleSystem, IProfiledSystem
{
    private IEntityQuery? _queryUpgrade1024to4096;
    private IEntityQuery? _queryUpgrade4096to16384;

    public void Execute(ISimulationView view)
    {
        var repo = (EntityRepository)view;

        _queryUpgrade1024to4096 ??= repo.Query()
            .With<BlueprintBlackboard1024>()
            .With<BlueprintBlackboard4096>()
            .Build();
        _queryUpgrade4096to16384 ??= repo.Query()
            .With<BlueprintBlackboard4096>()
            .With<BlueprintBlackboard16384>()
            .Build();

        UpgradeTier_1024_to_4096(repo, _queryUpgrade1024to4096);
        UpgradeTier_4096_to_16384(repo, _queryUpgrade4096to16384);
    }
}
```

### Q-12.4 — World-singleton init: lazy, inside `TickWorldSingletons`

Originally Runtime DD §8.5 proposed a separate `InitializeWorldSingletonBlueprints` call wired into engine boot code. **More robust pattern**: handle the init lazily inside `BlueprintTickSystem.TickWorldSingletons`. Since the system already walks the registry's world-singleton list every frame, just check if the corresponding singleton component exists; if not, allocate it and call `InitDefault`.

**Action:** Replace the `EnsureWorldSingletonAttached` design (Runtime DD §8.4) and the explicit boot-time call (§8.5) with a self-contained lazy-init inside `TickWorldSingletons`.

#### Updated §8.4 — Lazy singleton attach in `TickWorldSingletons`

```csharp
private unsafe void TickWorldSingletons(
    EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb)
{
    foreach (var (blueprintId, tier) in _registry.GetAllWorldSingletons())
    {
        if (!_registry.TryGetById(blueprintId, out var def)) continue;

        switch (tier)
        {
            case BlackboardTier.B1024:
                EnsureAndTickSingleton<BlueprintBlackboard1024>(
                    repo, view, ecb, blueprintId, def,
                    BlueprintBlackboard1024.TotalSize,
                    (byte)BlueprintBlackboard1024.MaxSlots);
                break;
            case BlackboardTier.B4096:
                EnsureAndTickSingleton<BlueprintBlackboard4096>(
                    repo, view, ecb, blueprintId, def,
                    BlueprintBlackboard4096.TotalSize,
                    (byte)BlueprintBlackboard4096.MaxSlots);
                break;
            case BlackboardTier.B16384:
                EnsureAndTickSingleton<BlueprintBlackboard16384>(
                    repo, view, ecb, blueprintId, def,
                    BlueprintBlackboard16384.TotalSize,
                    (byte)BlueprintBlackboard16384.MaxSlots);
                break;
        }
    }
}

private unsafe void EnsureAndTickSingleton<TBB>(
    EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb,
    int blueprintId, BlueprintDefinition def, int totalSize, byte maxSlots)
    where TBB : unmanaged
{
    // Lazy attach — first encounter creates the singleton + allocates the slot
    if (!repo.HasSingleton<TBB>())
        repo.SetSingletonUnmanaged<TBB>(default);

    ref var bb = ref repo.GetSingleton<TBB>();
    ref byte memoryRef = ref Unsafe.As<TBB, byte>(ref bb);
    byte* memory = (byte*)Unsafe.AsPointer(ref memoryRef);

    // Initialize header if not yet done
    ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
    if (header.MagicAndVersion != 0x42504257)
        BlueprintBlackboardPartitions.Initialize(memory, totalSize, maxSlots);

    // Attach slot if not yet attached
    if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
    {
        if (!BlueprintBlackboardPartitions.TryAttach(
                memory, blueprintId, def.StateSize, def.StructureHash, out payloadOffset))
            return;  // Tier capacity exhausted — shouldn't happen with Slice 1 constraints

        if (def.InitDefault is not null)
        {
            var initSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref memoryRef, payloadOffset),
                def.StateSize);
            def.InitDefault(initSpan);
        }
    }

    // Locate slot for reconciliation
    byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);
    int slotIndex = FindSlotIndex(slotTable, header.SlotCount, blueprintId);
    ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
        slotTable + slotIndex * BlueprintBlackboardPartitions.SlotEntrySize);

    // Reload reconciliation
    if (slot.StructureHash != def.StructureHash)
    {
        BlueprintBlackboardPartitions.ResetSlot(memory, slotIndex, def.StructureHash);
        if (def.InitDefault is not null)
        {
            var resetSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref memoryRef, slot.PayloadOffset),
                slot.PayloadSize);
            def.InitDefault(resetSpan);
        }
    }

    // Tick
    if (def.Tick is not null)
    {
        var tickSpan = MemoryMarshal.CreateSpan(
            ref Unsafe.Add(ref memoryRef, slot.PayloadOffset),
            slot.PayloadSize);
        def.Tick(tickSpan, view, ecb, Entity.Null,
                 view.Time, view.DeltaTime, slot.InstanceVersion);
    }
}
```

**Implications:**

- `BlueprintRegistry.EnsureWorldSingletonAttached` (Runtime DD §8.4) is no longer needed. **Remove this method** from the registry's public surface — it's now a private concern inside `BlueprintTickSystem`.
- `BlueprintRegistry.InitializeWorldSingletonBlueprints` (Runtime DD §8.5) is also no longer needed. **Remove from the public surface and from any engine boot code.**
- Slice 1 init flow simplifies: nothing extra to call from engine boot. The first frame after a `BlueprintRegistry.CommitStaging` with world-singleton entries auto-attaches and auto-initializes them.
- After hot reload that adds a new world-singleton: same flow. The new entry appears in `GetAllWorldSingletons()`, the next tick sees no slot for it, attaches and inits.
- After hot reload that removes a world-singleton: the entry no longer appears in `GetAllWorldSingletons()`, so it's not ticked. The slot remains in the singleton component but stays dormant. **Slice 2 may add explicit detach-on-removal**; Slice 1 accepts the dormant slot.

This pattern is also more consistent with the §9 lazy reconciliation philosophy: "pay-as-you-go inside the tick" applies uniformly to per-entity and world-singleton cases.

---

## Hot-path corrections

### Correction 1 — Pre-materialized world-singleton list eliminates the per-frame allocation

Runtime DD §10.2 noted that `_registry.GetAllWorldSingletons().ToList()` allocates a small list per frame and described it as Slice 1's acceptable cost. **Eliminate this entirely** by pre-materializing the list inside the registry snapshot.

**Action:** Update `BlueprintRegistry`'s `Snapshot` to hold a cached materialized list. Construct it once at `CommitStaging` time.

#### Updated `BlueprintRegistry.Snapshot`

```csharp
private sealed class Snapshot
{
    public Dictionary<int, BlueprintDefinition> ById { get; init; }
        = new Dictionary<int, BlueprintDefinition>();
    public Dictionary<string, int> ByName { get; init; }
        = new Dictionary<string, int>(StringComparer.Ordinal);
    public Dictionary<int, BlackboardTier> WorldSingletons { get; init; }
        = new Dictionary<int, BlackboardTier>();

    // Pre-materialized list — built once at snapshot construction, returned by
    // GetAllWorldSingletons() with zero per-call allocation.
    public IReadOnlyList<(int BlueprintId, BlackboardTier Tier)> WorldSingletonList { get; init; }
        = Array.Empty<(int, BlackboardTier)>();
}
```

#### Updated `CommitStaging`

```csharp
public void CommitStaging(BlueprintRegistryStaging staging)
{
    // Pre-materialize the world-singleton list for zero-alloc hot-path enumeration.
    var singletonList = staging.WorldSingletons
        .Select(kv => (kv.Key, kv.Value))
        .ToList()
        .AsReadOnly();

    var next = new Snapshot
    {
        ById               = staging.Definitions.ToDictionary(kv => kv.Key, kv => kv.Value),
        ByName             = staging.Definitions.ToDictionary(
            kv => kv.Value.Name, kv => kv.Key, StringComparer.Ordinal),
        WorldSingletons    = staging.WorldSingletons.ToDictionary(kv => kv.Key, kv => kv.Value),
        WorldSingletonList = singletonList,
    };

    Interlocked.Exchange(ref _current, next);
    OnRegistryChanged?.Invoke();
}
```

#### Updated `GetAllWorldSingletons`

```csharp
public IReadOnlyList<(int BlueprintId, BlackboardTier Tier)> GetAllWorldSingletons()
{
    return _current.WorldSingletonList;  // single field read, no allocation
}
```

**Type change:** the return type narrows from `IEnumerable<...>` to `IReadOnlyList<...>`. Callers (`BlueprintTickSystem.TickWorldSingletons`) iterate with a `foreach` either way; the list-typed return allows a struct enumerator for the iteration with no boxing.

**Allocation budget:** the per-frame `GC.GetAllocatedBytesForCurrentThread` budget in Runtime DD §10.3 (`AllocationFreeTests`) tightens from `64` bytes to **`0` bytes** for the steady-state frames. Allocations only happen at `CommitStaging` (rare; hot-reload boundary), not in `Execute`.

### Correction 2 — `MemoryMarshal.CreateSpan` replaces `fixed` + `Span` constructor

Runtime DD §6.3 and §10.5 used `fixed (byte* memory = bb.Memory)` to pin chunk memory before creating spans. While correct, this emits GC pinning instructions on every tick — unnecessary, because the engine's Simulation phase already guarantees no structural mutation during execution.

**Action:** Replace `fixed`-based span construction with `MemoryMarshal.CreateSpan` over a `ref byte`. This is the engine's preferred zero-overhead idiom.

#### Updated §6.3 — TickTier_1024

```csharp
private unsafe void TickTier_1024(EntityRepository repo, ISimulationView view, IEntityCommandBuffer ecb)
{
    foreach (var entity in _query1024!)
    {
        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        ref byte memoryRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory = (byte*)Unsafe.AsPointer(ref memoryRef);

        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != 0x42504257) continue;

        int slotCount = header.SlotCount;
        byte* slotTable = memory + sizeof(BlueprintBlackboardHeader);

        for (int i = 0; i < slotCount; i++)
        {
            ref var slot = ref Unsafe.AsRef<BlueprintSlotEntry>(
                slotTable + i * BlueprintBlackboardPartitions.SlotEntrySize);

            if (!_registry.TryGetById(slot.BlueprintId, out var def)) continue;

            // Reload reconciliation
            if (slot.StructureHash != def.StructureHash)
            {
                BlueprintBlackboardPartitions.ResetSlot(memory, i, def.StructureHash);
                if (def.InitDefault is not null)
                {
                    var initSpan = MemoryMarshal.CreateSpan(
                        ref Unsafe.Add(ref memoryRef, slot.PayloadOffset),
                        slot.PayloadSize);
                    def.InitDefault(initSpan);
                }
            }

            // Tick
            if (def.Tick is not null)
            {
                var tickSpan = MemoryMarshal.CreateSpan(
                    ref Unsafe.Add(ref memoryRef, slot.PayloadOffset),
                    slot.PayloadSize);
                def.Tick(tickSpan, view, ecb, entity,
                         view.Time, view.DeltaTime, slot.InstanceVersion);
            }
        }
    }
}
```

Key idiom: `MemoryMarshal.CreateSpan(ref Unsafe.Add(ref memoryRef, offset), length)`.

- `memoryRef` is a managed `ref byte` to the start of the component's bytes — held by the `ref var bb` of the chunk memory.
- `Unsafe.Add(ref memoryRef, offset)` produces a `ref byte` at the desired payload offset.
- `MemoryMarshal.CreateSpan` wraps that ref into a `Span<byte>` of the desired length.

No pin is taken. The GC won't move the chunk during Simulation phase by engine convention; the span's validity holds for the duration of the iteration.

#### Why this is safe

The `ref var bb = ref repo.GetComponentRW<T>(entity)` returns a managed `ref` into chunk memory. The GC tracks this `ref` and *would* track movements through it if it moved the chunk — but the engine guarantees no chunk movement during Simulation phase systems (no structural mutations until ECB playback at Sync). So creating a `Span<byte>` from this `ref` is safe.

`fixed` pinning would have been belt-and-braces against a threading or GC model we don't have. The engine team has confirmed that `MemoryMarshal.CreateSpan` over a `ref` is the canonical pattern used throughout engine code.

The `byte* memory = (byte*)Unsafe.AsPointer(ref memoryRef);` is still needed for the `Unsafe.AsRef<BlueprintSlotEntry>(slotTable + i * SlotEntrySize)` reads — those use byte-pointer arithmetic for offset computation. The pointer is short-lived within one iteration; same safety argument applies.

#### Updated §10.5 — Pinning safety note

Strike the entire §10.5 "Pinning safety" subsection. Replace with:

> **§10.5 — Span construction over `ref byte`**
>
> The runtime uses `MemoryMarshal.CreateSpan(ref Unsafe.Add(ref memoryRef, offset), length)` to project Span<byte> views into chunk memory. This is the engine's zero-overhead pattern — no GC pinning instructions are emitted, and the span's validity is guaranteed by the engine's Simulation-phase no-structural-mutation invariant. Engine systems (e.g. `CarKinematicsSystem`, `MovingEntitySystem`) use the same idiom.

### Patches summary

| Patch | Affects | Change |
|---|---|---|
| Q-12.1 | §6.2 + §12.1 | Strike "if sub-phase exists, prefer that" hedging; names confirmed |
| Q-12.2 | §6.3 + §12.2 | Strike "ugly cast" commentary; `(EntityRepository)view` is canonical |
| Q-12.3 | §6.4 + §12.3 | Replace `OnAttach` with `??=` lazy init in `Execute` |
| Q-12.4 | §8.4-§8.5 + §12.4 | Drop `EnsureWorldSingletonAttached` + `InitializeWorldSingletonBlueprints`; lazy inside `TickWorldSingletons` |
| Hot path 1 | §2.2 + §10.2 + §10.3 | Pre-materialize `WorldSingletonList` in snapshot; allocation budget → 0 |
| Hot path 2 | §6.3 + §10.5 | `MemoryMarshal.CreateSpan` over `ref byte` instead of `fixed` + `Span` ctor |

### Effect on the implementation

Slice 1 implementation simplifies:

- Engine boot code does **not** need to call any "initialize Blueprints" hook. World-singletons auto-attach lazily.
- The Runtime DD §10.3 allocation test budget tightens from 64 bytes to 0 bytes per frame in steady state.
- All tier-tick implementations use the same `MemoryMarshal.CreateSpan` pattern (no `fixed` blocks).
- `BlueprintRegistry.Snapshot` gains one cached field (`WorldSingletonList`); `CommitStaging` materializes it once per commit.

These are wins on every axis: less code, zero allocations, no boot wiring.

---

## What remains open in §12

- **Q-12.5** — Profile granularity: Slice 1 reports system-level only; per-Blueprint timing is editor-side. No decision needed; deferred to Slice 2 if telemetry shows a gap.
- **Q-12.6** — `IReloadLogSink` injection: deferred to Editor DD (the editor owns the logger; engine DI wiring is an Editor DD concern).

All structural questions resolved. The Runtime DD plus this patches doc is the implementable specification for M8, M9, M10.

---

*End of Runtime DD inline patches. Next document: Test Harness Detailed Design.*
