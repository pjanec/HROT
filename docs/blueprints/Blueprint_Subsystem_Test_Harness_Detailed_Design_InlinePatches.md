# Blueprint Subsystem — Test Harness Detailed Design — Inline Patches

> **Status:** Patches to `Blueprint_Subsystem_Test_Harness_Detailed_Design.md` from architect's review.
> **Effect:** Three simplifications (use native `FdpEventBus` double-buffering, ECB playback at end of `TickFrame`, use `EntityRepository.UnmanagedHandle` directly) plus four §12 open-question resolutions.
> **Reads alongside:** the main Test Harness DD; nothing in the main doc is invalidated, only refined.

---

## Patch 1 — Use native `FdpEventBus` instead of `_pendingEvents` (corrects §3.7)

### The problem

The main DD §3.7 specified that `MockSimulationView` would snapshot `_pendingEvents` into immutable lists at `BeginTick` and return them from `ReadEvents<T>` for the duration of the tick, to satisfy QV-3 (stable event stream).

The architect's correction: **the engine's `EntityRepository` already owns an `FdpEventBus` that natively implements double-buffering.** Wrapping the repo and then reimplementing event stability on top of it duplicates the bus's job and risks subtle drift from production semantics.

### The fix

`MockSimulationView.ReadEvents<T>` delegates directly to `_repo.Bus.Read<T>()`. The fixture's `TickFrame` calls `_repo.Bus.SwapBuffers()` at the right point to advance the frame's event buffer.

#### Updated `MockSimulationView.ReadEvents`

```csharp
// Was (per main DD §3.3):
private readonly Dictionary<Type, object> _eventStreamsByType = new();
internal void BeginTick(IReadOnlyDictionary<Type, IReadOnlyList<object>> publishedEvents) { ... }
public IReadOnlyList<T> ReadEvents<T>() where T : unmanaged
{
    if (_eventStreamsByType.TryGetValue(typeof(T), out var list))
        return (IReadOnlyList<T>)list;
    return Array.Empty<T>();
}

// Now:
public IReadOnlyList<T> ReadEvents<T>() where T : unmanaged
    => _repo.Bus.Read<T>();
```

Drop the `_eventStreamsByType` field entirely. Drop `BeginTick`. The mock view becomes a pure read-only projection with zero per-frame state.

#### Updated `BlueprintTestFixture.TickFrame` for event handling

```csharp
public void TickFrame(float deltaTime)
{
    // 1. Advance the event bus to make events published last frame readable this frame
    _repo.Bus.SwapBuffers();

    // 2. Advance time
    View.AdvanceTime(deltaTime);

    // 3. Execute systems
    TickSystem.Execute(View);
    foreach (var auxSystem in _auxSimulationSystems)
        auxSystem.Execute(View);
    MaintenanceSystem.Execute(View);

    // 4. ECB playback — see Patch 2
    Ecb.Playback(_repo);

    // 5. Test-only mid-tick hook
    _tickActions?.Invoke(View, Ecb);
}
```

#### Updated event publication for tests

The test-only `PublishEventForNextTick<T>` helper goes away — tests use the real bus directly via the ECB (production code path):

```csharp
// Was:
fixture.PublishEventForNextTick(new HitEvent { Target = entity, Damage = 30, ... });
fixture.TickFrame(0.016f);

// Now:
fixture.Ecb.PublishEvent(new HitEvent { Target = entity, Damage = 30, ... });
fixture.TickFrame(0.016f);
```

The ECB's `PublishEvent` op writes to the bus during playback (at end of `TickFrame` per Patch 2). The next `TickFrame`'s `SwapBuffers` flips the buffer so the event is readable.

For tests that want to inject an event into the *current* tick (no playback delay), there's still a direct-bus path:

```csharp
// For test setup before any TickFrame, or for events that should be readable immediately:
fixture.World.Bus.Publish(new HitEvent { ... });
fixture.World.Bus.SwapBuffers();    // flip buffer so it's readable
fixture.TickFrame(0.016f);
```

This is the same pattern production scenario-load code uses for seeding events at startup.

#### Updated contract test (§8.3 — `ReadEvents_SameListThroughoutTick`)

The test still verifies stability within a tick, but now the stability is guaranteed by `FdpEventBus`'s double-buffering, not by mock-side snapshots:

```csharp
[Fact]
public void ReadEvents_SameListThroughoutTick()
{
    using var fixture = new BlueprintTestFixture();

    // Publish two events directly to the bus and swap so they're readable
    fixture.World.Bus.Publish(new TestEvent { Value = 1 });
    fixture.World.Bus.Publish(new TestEvent { Value = 2 });
    fixture.World.Bus.SwapBuffers();

    IReadOnlyList<TestEvent>? firstRead = null;
    IReadOnlyList<TestEvent>? secondRead = null;
    int sizeAfterEcbPublish = -1;

    fixture.RegisterTickAction((view, ecb) =>
    {
        firstRead = view.ReadEvents<TestEvent>();
        secondRead = view.ReadEvents<TestEvent>();

        // Publishing via ECB during tick: queued for next frame, not visible now
        ecb.PublishEvent(new TestEvent { Value = 3 });
        sizeAfterEcbPublish = view.ReadEvents<TestEvent>().Count;
    });

    fixture.TickFrame(0.016f);

    Assert.NotNull(firstRead);
    Assert.NotNull(secondRead);
    Assert.Equal(2, firstRead.Count);
    Assert.Equal(2, sizeAfterEcbPublish);   // ECB publish doesn't appear this tick
}
```

### Why this is a strict improvement

| Before | After |
|---|---|
| Mock-side state field `_eventStreamsByType` | None — bus owns the buffer |
| Mock-side snapshot copy on every `BeginTick` | Zero copy — direct bus read |
| Risk of mock drifting from production bus semantics | Same `Read<T>` call as production |
| Two event-publication paths (mock-only + ECB) | One — both production and tests use the bus |

---

## Patch 2 — ECB playback at end of `TickFrame` (corrects §4.8)

### The problem

The main DD §4.8 placed ECB playback at the *start* of `TickFrame`. The architect's correction: in production, ECB plays back in the `Sync` phase at the *end* of the frame's system execution. Tests should mirror this — assertions written immediately after `TickFrame(dt)` should see the finalised state without needing a manual `Playback()` call.

### The fix

Move `Ecb.Playback(_repo)` to the end of `TickFrame`, after `BeforeSync` runs. Per the engine's phase order: `Input → Simulation → PostSimulation → BeforeSync → Sync` — and Sync is where ECB plays back. The `BlueprintMaintenanceSystem` lives in `BeforeSync` (per Runtime DD §7.3), so the ECB playback happens after maintenance completes.

#### Updated `TickFrame` (combining Patch 1 + Patch 2)

```csharp
public void TickFrame(float deltaTime)
{
    // 1. Advance event bus so events published last frame become readable this frame
    _repo.Bus.SwapBuffers();

    // 2. Advance time
    View.AdvanceTime(deltaTime);

    // 3. Simulation phase
    TickSystem.Execute(View);
    foreach (var auxSystem in _auxSimulationSystems)
        auxSystem.Execute(View);

    // 4. BeforeSync phase
    MaintenanceSystem.Execute(View);

    // 5. Sync phase: ECB playback (structural mutations + queued events apply)
    Ecb.Playback(_repo);

    // 6. Mid-tick inspection hook (after everything settled)
    _tickActions?.Invoke(View, Ecb);
}
```

#### Implication for test assertions

```csharp
// Test code now reads naturally — no manual Playback hack:
var e = fixture.World.CreateEntity();
fixture.World.AddComponent(e, new BlueprintBlackboard1024());
fixture.AttachBlueprint(asset, e);

// Issue ECB ops
fixture.Ecb.AddComponent(e, new SomeFlag { Active = true });

fixture.TickFrame(0.016f);

// After TickFrame returns, all ECB ops have been played back
Assert.True(fixture.World.HasComponent<SomeFlag>(e));
Assert.True(fixture.View.GetComponentRO<SomeFlag>(e).Active);
```

This matches production: a system that runs in Simulation and writes to ECB sees its writes applied at end of frame.

#### Implication for "IsAlive mid-frame" contract test

The test in §8.3 (`IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback`) still works correctly. The "before playback" check happens between issuing the ECB op and `TickFrame`:

```csharp
[Fact]
public void IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback()
{
    using var fixture = new BlueprintTestFixture();
    var e = fixture.World.CreateEntity();

    fixture.Ecb.DestroyEntity(e);
    Assert.True(fixture.View.IsAlive(e),
        "Before TickFrame's ECB playback, entity must still be alive");

    fixture.TickFrame(0.016f);
    Assert.False(fixture.View.IsAlive(e),
        "After TickFrame's ECB playback, entity is destroyed");
}
```

#### Implication for "AddComponent defers" contract test

Same pattern — the assertion before `TickFrame` checks deferral, after `TickFrame` checks application:

```csharp
[Fact]
public void AddComponent_DefersUntilPlayback()
{
    using var fixture = new BlueprintTestFixture();
    var e = fixture.Ecb.CreateEntity();

    fixture.Ecb.AddComponent(e, new TestComponent { Value = 7 });
    Assert.False(fixture.View.HasComponent<TestComponent>(e));   // before TickFrame

    fixture.TickFrame(0.016f);

    Assert.True(fixture.View.HasComponent<TestComponent>(e));
    Assert.Equal(7, fixture.View.GetComponentRO<TestComponent>(e).Value);
}
```

### Engine phase parity table

After this patch, the test fixture's `TickFrame` mirrors the engine's frame structure exactly:

| Phase | Engine | Test fixture |
|---|---|---|
| Input | `InputSystems` execute | (n/a; tests inject inputs directly) |
| (Event flip) | `Bus.SwapBuffers` at frame boundary | `_repo.Bus.SwapBuffers()` first |
| Simulation | All `[UpdateInPhase(Simulation)]` systems | `TickSystem.Execute` + aux systems |
| PostSimulation | `[UpdateInPhase(PostSimulation)]` systems | (n/a in Slice 1) |
| BeforeSync | `[UpdateInPhase(BeforeSync)]` systems | `MaintenanceSystem.Execute` |
| Sync | ECB playback | `Ecb.Playback(_repo)` |

Tests written against this fixture behave identically to systems running in the real engine.

---

## Patch 3 — Use `EntityRepository.UnmanagedHandle` (corrects §12.3 and §6.x InvokeHsmAction)

### The problem

The main DD §12.3 (Open Question for HsmKernelBridge construction) anticipated allocating a `GCHandle` for the `EntityRepository` inside test helpers, then freeing it after.

The architect's correction: **the `EntityRepository` already allocates a permanent `GCHandle` to itself at construction time and exposes it via `UnmanagedHandle`.** Test code uses that directly.

### The fix

#### Updated `InvokeHsmAction` helper

```csharp
public unsafe void InvokeHsmAction(BlueprintAsset asset, Entity entity)
{
    int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
    if (!Registry.TryGetById(blueprintId, out var def))
        throw new InvalidOperationException($"Blueprint '{asset.Name}' not registered.");

    // No GCHandle.Alloc / Free needed — repo already exposes its own handle
    var bridge = new HsmKernelBridge
    {
        Self         = entity,
        WorldHandle  = _repo.UnmanagedHandle,
        TraceContext = null,
    };

    // Pin the bridge for the duration of the call (it's on our stack but we need
    // its address as void* for the unmanaged function-pointer dispatch)
    var commandWriter = default(HsmCommandWriter);

    // Locate the HsmAction thunk via reflection on the loaded assembly
    var method = ResolveHsmActionMethod(asset);
    var fnPtr = method.MethodHandle.GetFunctionPointer();

    // Get pointer to the Params struct in BehaviorParameters
    ref var bb = ref _repo.GetComponentRW<BrainBlackboard>(entity);
    fixed (void* paramsPtr = bb.BehaviorParameters)
    {
        // Call the unmanaged thunk
        ((delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)fnPtr)(
            paramsPtr, &bridge, &commandWriter);
    }

    // No GCHandle.Free — UnmanagedHandle stays alive for the repo's lifetime
}
```

#### Updated `InvokeHsmGuard` helper

```csharp
public unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)
{
    int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
    if (!Registry.TryGetById(blueprintId, out var def))
        throw new InvalidOperationException($"Blueprint '{asset.Name}' not registered.");

    var bridge = new HsmKernelBridge
    {
        Self         = entity,
        WorldHandle  = _repo.UnmanagedHandle,
        TraceContext = null,
    };

    var method = ResolveHsmGuardMethod(asset);
    var fnPtr = method.MethodHandle.GetFunctionPointer();

    ref var bb = ref _repo.GetComponentRW<BrainBlackboard>(entity);
    fixed (void* paramsPtr = bb.BehaviorParameters)
    {
        return ((delegate* unmanaged<void*, void*, ushort, bool>)fnPtr)(
            paramsPtr, &bridge, eventId);
    }
}
```

### Why this is safe

`EntityRepository.UnmanagedHandle` is a `nint` (or `IntPtr`) that wraps a `GCHandle.Alloc(this)` from the repo's constructor. The repo holds onto it for its entire lifetime, freeing it in `Dispose`. Test code that grabs it lives strictly within the repo's lifetime (the fixture owns the repo, the test code uses the fixture), so the handle is always valid.

The shape mirrors what production HSM dispatch does — the kernel bridges its world reference through this same `UnmanagedHandle`. Tests using the same handle exercise the exact same code path as production.

### Net effect

| Before | After |
|---|---|
| `GCHandle.Alloc(_repo)` in every InvokeHsm call | `_repo.UnmanagedHandle` (zero-cost field read) |
| `GCHandle.Free` cleanup in every Invoke (try/finally) | No cleanup needed |
| One GCHandle per test invocation | One GCHandle for the repo's lifetime |
| Test-only path that doesn't match production | Same path production uses |

---

## Resolutions to §12 open questions

### Q-12.1 — `BehaviorRegistry` instantiation: yes, lightweight

The real `BehaviorRegistry` from `Fdp.Toolkits` is a lightweight data-container that maps behavior IDs to `BehaviorDefinition` records. No tick-system dependency. Test fixtures instantiate it directly:

```csharp
public sealed class BlueprintTestFixture : IDisposable
{
    public BehaviorRegistry BehaviorRegistry { get; } = new BehaviorRegistry();
    public HsmActionDispatcher HsmDispatcher { get; } = HsmActionDispatcher.Instance;   // singleton per engine convention

    // ... in CompileAndLoad, registrar invocation supplies these as needed:
    private void InvokeRegistrarMethod(MethodInfo method, BlueprintRegistryStaging staging)
    {
        var parameters = method.GetParameters();
        var args = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            args[i] = parameters[i].ParameterType switch
            {
                var t when t == typeof(BlueprintRegistry)     => Registry,
                var t when t == typeof(BehaviorRegistry)      => BehaviorRegistry,
                var t when t == typeof(HsmActionDispatcher)   => HsmDispatcher,
                _ => throw new InvalidOperationException(
                    $"Unknown registrar parameter type: {parameters[i].ParameterType}")
            };
        }
        method.Invoke(null, args);
    }
}
```

The `HsmActionDispatcher` is a singleton per engine convention. Test fixtures share the singleton across tests. **Important**: after each test, the fixture's `Dispose` should call `HsmDispatcher.ClearAll()` to remove the test's registered function pointers — otherwise stale pointers from a previous test's ALC leak into the next test's pointer-dispatch table.

```csharp
public void Dispose()
{
    HsmDispatcher.ClearAll();   // clear stale function pointers from this test's ALC
    // ... rest of dispose (ALC unload, GC reclaim verify)
}
```

### Q-12.2 — `BTreeContext` construction: stack-constructible

`BTreeContext` is a simple value struct holding `EntityRepository World` and `Entity Self` plus a few small fields. Tests construct directly on the stack:

```csharp
public NodeStatus InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)
{
    var ctx = new BTreeContext
    {
        World = _repo,
        Self  = entity,
        Time  = View.Time,
        // ... any other small fields ...
    };
    ref var bb = ref _repo.GetComponentRW<BrainBlackboard>(entity);
    ref var state = ref _repo.GetComponentRW<BehaviorTreeState>(entity);

    var thunk = ResolveBTreeTickMethod(asset);
    return thunk(ref bb, ref state, ref ctx, paramIndex);
}
```

No mock `BehaviorRegistry`-side wrapping needed. The test passes the context to the thunk directly.

### Q-12.3 — `HsmKernelBridge` construction: resolved by Patch 3

Use `_repo.UnmanagedHandle` directly. See Patch 3 above for the full helper implementations.

### Q-12.4 — `MockDispatcherSystem<TChannel>` base class: recommended

The architect endorses the base-class pattern from main DD §12.4. Add it to the test harness as a first-class helper:

```csharp
namespace Hrot.Blueprints.Tests.MockSystems;

/// <summary>
/// Base class for test-only dispatcher systems that read channel commands
/// authored by Blueprints and stub out the Status field.
/// Used to write linear end-to-end tests for AiPrimitive Blueprints that
/// issue commands (e.g., MoveToAndFire).
/// </summary>
public abstract class MockDispatcherSystem<TChannel> : IEcsModuleSystem, IProfiledSystem
    where TChannel : unmanaged
{
    public string ProfileName => $"Mock{typeof(TChannel).Name}Dispatcher";

    protected EntityRepository? Repo { get; private set; }
    private IEntityQuery? _query;

    public void Execute(ISimulationView view)
    {
        Repo = (EntityRepository)view;
        _query ??= Repo.Query().With<TChannel>().Build();

        foreach (var entity in _query)
        {
            ref var channel = ref Repo.GetComponentRW<TChannel>(entity);
            HandleChannel(ref channel, entity, view);
        }
    }

    /// <summary>
    /// Subclasses implement the test-specific dispatcher behavior — typically
    /// reading the ActiveAction field, deciding the new Status, and writing it back.
    /// </summary>
    protected abstract void HandleChannel(ref TChannel channel, Entity entity, ISimulationView view);
}
```

#### Example use — `MockLocomotionDispatcher`

```csharp
public sealed class MockLocomotionDispatcher : MockDispatcherSystem<LocomotionChannel>
{
    public Func<LocomotionChannel, NodeStatus> NextStatus { get; set; } = _ => NodeStatus.Success;
    public int InvokeCount { get; private set; }
    public int LastObservedActionInstanceId { get; private set; }

    protected override void HandleChannel(ref LocomotionChannel channel, Entity entity, ISimulationView view)
    {
        if (channel.ActiveAction != 0)
        {
            InvokeCount++;
            LastObservedActionInstanceId = channel.ActionInstanceId;
            channel.Status = NextStatus(channel);
        }
    }
}
```

Test usage:

```csharp
var dispatcher = new MockLocomotionDispatcher
{
    NextStatus = ch => ch.ActionInstanceId >= 2 ? NodeStatus.Success : NodeStatus.Running
};
fixture.AddSimulationSystem(dispatcher);
```

The test now has direct control over the dispatcher's response per-channel without writing boilerplate each time.

A similar `MockWeaponDispatcher` and `MockInteractionDispatcher` complete the set; together they cover all Slice 1 channel-command demos.

---

## Patches summary

| Patch | Affects | Change |
|---|---|---|
| 1: Use `FdpEventBus` natively | §3.3 + §3.7 + §4.10 + §8.3 | Drop `_eventStreamsByType` / `BeginTick`. `ReadEvents` delegates to `_repo.Bus.Read<T>`. Fixture calls `_repo.Bus.SwapBuffers()` at start of `TickFrame`. |
| 2: ECB playback at end of `TickFrame` | §4.8 + §5.3 + assertion examples throughout | Move `Ecb.Playback(_repo)` to end. Test assertions after `TickFrame` see finalised state without manual playback. |
| 3: Use `EntityRepository.UnmanagedHandle` | §12.3 + `InvokeHsmAction` / `InvokeHsmGuard` helpers | Drop `GCHandle.Alloc/Free`. Use `_repo.UnmanagedHandle` directly. |
| Q-12.1 resolved | §12.1 + fixture init | Real `BehaviorRegistry` and `HsmActionDispatcher` (singleton) usable; `Dispose` calls `HsmDispatcher.ClearAll()`. |
| Q-12.2 resolved | §12.2 + `InvokeBTreeAction` helper | `BTreeContext` is stack-constructible; no wrapper needed. |
| Q-12.3 resolved | covered by Patch 3 | — |
| Q-12.4 resolved | §12.4 → new section in `MockSystems/` | `MockDispatcherSystem<TChannel>` base class added. |

### Effect on the implementation

Slice 1 implementation simplifies further:

- `MockSimulationView` loses two helper methods (`BeginTick`, internal `_eventStreamsByType` field) — becomes a pure read-only projection.
- `BlueprintTestFixture` loses `PublishEventForNextTick` / `SnapshotPendingEvents` / `_pendingEvents` — tests use the bus directly.
- `InvokeHsmAction` / `InvokeHsmGuard` lose `GCHandle.Alloc/Free` plumbing.
- `Dispose` gains one line: `HsmDispatcher.ClearAll()`.
- New `MockSystems/` folder with `MockDispatcherSystem<T>` base + three concrete dispatchers (`Locomotion`, `Weapon`, `Interaction`).

Net change: ~80 lines removed, ~50 lines added. Strictly less code, exact production parity, no test-only event-bus shadow.

---

## What remains open in §12

- **Q-12.5** — Cross-test scenario reuse: deferred. Slice 1 default is per-test duplication; if patterns emerge that justify a helper, add `BlueprintTestScenarioBuilder` in a future revision.

All structural questions resolved. The Test Harness DD plus this patches doc is the implementable specification for M2.

---

*End of Test Harness DD inline patches. Next document: Hot Reload Detailed Design.*
