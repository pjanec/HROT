# BATCH-40 Report

**Workstream:** breakpoints-1
**Batch:** BATCH-40
**Status:** COMPLETE

---

## Files Modified / Created

| File | Action |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PendingDebugMutation.cs` | Created (new) |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Modified |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PendingMutationTests.cs` | Created (new) |

---

## Build Result

```
Build succeeded.
    0 Error(s)
    0 Warning(s) in BATCH-40 files
```

(5 pre-existing CS0618 warnings in unrelated Hrot.Blueprints.Tests project — not introduced by this batch.)

---

## Test Results

**Total: 45 passed, 0 failed, 0 skipped**

### New tests (PendingMutationTests — 5)

| Test | Result |
|------|--------|
| `Stage_UnmanagedStruct_StoresSizeAndClassification` | PASS |
| `Stage_ManagedRef_StoresClassificationOnly` | PASS |
| `Drain_UnmanagedPayload_PinnedAndCopiedToECB` | PASS |
| `Drain_ManagedPayload_RoutedViaSetManagedRaw` | PASS |
| `Drain_AppliesAtN_Plus_1_BoundaryNotN` | PASS |

### Existing tests (40) — all continued to pass.

---

## Exact Implementations

### `PendingDebugMutation` struct

```csharp
public readonly struct PendingDebugMutation
{
    public readonly Entity Target;
    public readonly int ComponentTypeId;
    public readonly bool IsManaged;
    public readonly object Payload;
    public readonly int SizeBytes;

    public PendingDebugMutation(
        Entity target,
        int componentTypeId,
        bool isManaged,
        object payload,
        int sizeBytes)
    {
        Target          = target;
        ComponentTypeId = componentTypeId;
        IsManaged       = isManaged;
        Payload         = payload;
        SizeBytes       = sizeBytes;
    }
}
```

### `PendingMutationsQueue` test seam (in DataBreakpointManager)

```csharp
/// <summary>
/// Exposes the pending mutation queue for testing.
/// </summary>
internal Queue<PendingDebugMutation> PendingMutationsQueue => _pendingMutations;
```

### `StageMutation`

```csharp
public void StageMutation(Entity entity, Type componentType, object componentValue)
{
    if (componentType == null) throw new ArgumentNullException(nameof(componentType));
    if (componentValue == null) throw new ArgumentNullException(nameof(componentValue));

    int typeId     = ComponentTypeRegistry.GetId(componentType);
    bool isManaged = !componentType.IsValueType;
    int sizeBytes  = isManaged ? 0 : Marshal.SizeOf(componentType);

    _pendingMutations.Enqueue(new PendingDebugMutation(
        entity, typeId, isManaged, componentValue, sizeBytes));
}
```

### `DrainPendingMutations`

```csharp
private unsafe void DrainPendingMutations(EntityRepository repo)
{
    if (_pendingMutations.Count == 0) return;

    var ecb = ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetCommandBuffer();
    while (_pendingMutations.TryDequeue(out var m))
    {
        if (m.IsManaged)
        {
            ecb.SetManagedComponentRaw(m.Target, m.ComponentTypeId, m.Payload);
        }
        else
        {
            var handle = GCHandle.Alloc(
                m.Payload, GCHandleType.Pinned);
            try
            {
                ecb.SetComponentRaw(
                    m.Target, m.ComponentTypeId,
                    (void*)handle.AddrOfPinnedObject(),
                    m.SizeBytes);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
```

### `RequestStep`

```csharp
public void RequestStep()
{
    if (!_isPaused) return;

    // Restore end-of-tick state (clean step -- no resimulation, no event injection).
    _liveRepo.SyncFrom(_postTickSnapshot);

    // Apply staged mutations at the N+1 boundary.
    DrainPendingMutations(_liveRepo);

    _timeController.RequestStepOneTick();
    _isPaused = false;
    _pausedTick = 0;
    // _pendingMutations is empty after drain; no explicit clear needed.

    OnPauseStateChanged?.Invoke(false);
}
```

### `RequestContinue`

```csharp
public void RequestContinue()
{
    if (!_isPaused) return;

    // Restore end-of-tick state before resuming.
    _liveRepo.SyncFrom(_postTickSnapshot);

    // Apply staged mutations at the N+1 boundary.
    DrainPendingMutations(_liveRepo);

    _timeController.RequestResume();
    _isPaused = false;
    _pausedTick = 0;
    // _pendingMutations is empty after drain; no explicit clear needed.

    OnPauseStateChanged?.Invoke(false);
}
```

---

## Issues Encountered

### 1. TemporalStatusBannerTests compatibility

`TemporalStatusBannerTests` calls `manager.StageMutation(new Entity(1,0), typeof(object), new object())`.
`typeof(object)` is a reference type (`IsValueType = false`), so the new implementation takes the
`isManaged = true` branch, skips `Marshal.SizeOf`, and enqueues with `sizeBytes = 0`.
`ComponentTypeRegistry.GetId(typeof(object))` returns -1 (not registered) — no throw, just -1 stored.
The queue increments as before, so the count assertion passes unchanged.

### 2. `ISimulationView` cast for ECB access

`GetCommandBuffer()` is on `ISimulationView`. The cast requires a qualified namespace because
`DataBreakpointManager.cs` does not import `Fdp.ModuleHost.Abstractions` as a top-level `using`.
Used as an inline cast: `((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetCommandBuffer()`.

### 3. ECB thread-local identity

`_perThreadCommandBuffer` is `ThreadLocal<EntityCommandBuffer>` in `EntityRepository`.
Capturing the ECB reference BEFORE `RequestStep()` on the same thread returns the same instance
that `DrainPendingMutations` writes to, so calling `ecb.Playback(liveRepo)` after `RequestStep`
replays the staged mutations correctly. This is the design relied on by the drain tests.

### 4. Managed components and snapshot policy

`EntityLabel` is a mutable class with no `[DataPolicy]` attribute, so it defaults to
`DataPolicy.NoSnapshot`. `SyncFrom` does not copy it between snapshots. This means drain tests for
managed components must rely on the explicit component table registration on `liveRepo` surviving
the `SyncFrom` calls (tables are not removed by SyncFrom — only data for snapshotable types is
overwritten). `SetManagedComponentRaw` on ECB playback sets the value and the component-mask bit
directly, so the mutation is visible immediately after `ecb.Playback(liveRepo)`.
