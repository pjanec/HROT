# BATCH-19: TASK-DBG-004 + TASK-DBG-005 -- Watch Expressions + Multi-Entity Debugging

**Batch Number:** BATCH-19
**Tasks:** TASK-DBG-004, TASK-DBG-005
**Phase:** 5 -- Debug Protocol
**Estimated Effort:** 4-5 days combined
**Priority:** HIGH
**Dependencies:** BATCH-18 (breakpoints + step semantics in place; `BlueprintDebugSession` has working `_isPaused`, `_currentCallDepth`, `RegisterDebugMap`)

---

## 0. Onboarding

### Required Reading (IN ORDER)

1. `.dev/blueprints-1/reviews/BATCH-18-REVIEW.md` -- current state and P3 issues.
2. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-004 -- Watch Expressions and Pin-Value Snapshotting scope.
3. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-005 -- Multi-Entity Debugging scope.
4. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §8 (Watches), §9 (Multi-entity), §10 (PDB integration), §11 (Hot reload interaction).
5. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md` -- Patch 2 (PinValueChanged byte buffer) already applied in BATCH-16.
6. `.dev/blueprints-1/DEBT-TRACKER.md` -- DEBT-021.

### Source Code Locations

- `BlueprintDebugSession.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`
- `IBlueprintDebugSession.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- `CapturingDebugSession.cs`: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/CapturingDebugSession.cs`
- New test files in: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/`

### Report Submission

Submit to: `.dev/blueprints-1/reports/BATCH-19-REPORT.md`

---

## 1. DEBT-021 Minor Fix (while editing BlueprintDebugSession)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`

In `HandleBreakpointHit`, the code fires `OnBreakpointListChanged` after hit-count update. This was intended for hit-count notifications but `OnBreakpointListChanged` is semantically for structural changes. Remove the `OnBreakpointListChanged` fire from hit-count updates (or leave it -- editor uses a different rendering path). Add a comment on the `if (bp.Id.Value != 0)` sentinel check:
```csharp
// Pseudo-breakpoints (step hits) have default BreakpointId (Value == 0); skip hit count.
```

---

## 2. TASK-DBG-004: Watch Expressions and Pin-Value Snapshotting

See `TASK-DETAIL.md §DBG-004` for full scope and success conditions.

**Design references:**
- Debug Protocol DD §8 for watch expressions
- Inline Patches Patch 2 for `PinValueChanged<T>` byte-buffer approach (already applied to `IBlueprintProbeSink`)

### 2.1 Update the Watch class

The current `Watch` stub class in `IBlueprintDebugSession.cs` must be fully implemented per the TASK-DETAIL spec. Replace it:

```csharp
public sealed class Watch
{
    private readonly byte[] _valueBuffer;  // 64-byte pre-allocated buffer

    public WatchId    Id              { get; }
    public Guid       AssetId         { get; }
    public Guid       GraphId         { get; }
    public Guid       PinId           { get; }
    public string     PinIdString     { get; }
    public string     DisplayName     { get; }
    public Type       ExpectedType    { get; }
    public int        ExpectedSizeBytes { get; }

    public ReadOnlySpan<byte> LastValueBytes => _valueBuffer.AsSpan(0, Math.Min(ExpectedSizeBytes, 64));
    public Entity   LastUpdateEntity { get; private set; }
    public uint     LastUpdateTick   { get; private set; }
    public int      UpdateCount      { get; private set; }
    public bool     HasEverBeenWritten { get; private set; }
    public bool     IsStale          { get; internal set; }

    public Watch(WatchId id, Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)
    {
        Id               = id;
        AssetId          = assetId;
        GraphId          = graphId;
        PinId            = pinId;
        PinIdString      = pinId.ToString("D");
        DisplayName      = displayName;
        ExpectedType     = expectedType;
        ExpectedSizeBytes = System.Runtime.CompilerServices.Unsafe.SizeOf<byte>(); // placeholder; see below
        _valueBuffer     = new byte[64];
    }

    internal void WriteValue<T>(T value, Entity self, uint tick) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        if (size > 64)
            throw new InvalidOperationException(
                $"Watch value type {typeof(T).Name} is {size} bytes; exceeds 64-byte buffer.");
        System.Runtime.CompilerServices.Unsafe.WriteUnaligned(
            ref _valueBuffer[0], value);
        LastUpdateEntity   = self;
        LastUpdateTick     = tick;
        UpdateCount++;
        HasEverBeenWritten = true;
    }
}
```

Note: `ExpectedSizeBytes` should be set to `Unsafe.SizeOf<T>()` but `T` is not known at construction time (only `Type` is). Store `ExpectedType` and compute size lazily, OR require caller to pass size. For simplicity, let `WriteValue<T>` use `Unsafe.SizeOf<T>()` directly -- it's always called with the correct `T`.

### 2.2 Implement AddWatch / RemoveWatch / GetWatches on BlueprintDebugSession

State to add:
```csharp
private readonly Dictionary<WatchId, Watch>   _watches           = new();
private readonly Dictionary<string, Watch>    _watchesByPinString = new(StringComparer.Ordinal); // keyed by pinId.ToString("D")
private int _nextWatchId = 1;
```

**`AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) -> WatchId`**
- **Note:** Current interface has `AddWatch(Guid assetId, Guid graphId, Guid pinId)` without `displayName` / `expectedType`. Check if the interface signature needs updating. Per TASK-DETAIL §DBG-004, the full signature is `AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)`. Update `IBlueprintDebugSession` interface to match if it doesn't.
- Creates a `Watch` with new `WatchId(_nextWatchId++)`.
- Stores in both dicts: `_watches[id]` and `_watchesByPinString[pinId.ToString("D")]`.
- Returns the `WatchId`.

**`RemoveWatch(WatchId id)`**: removes from both dicts; fires `OnWatchStale?.Invoke(id)` if added later.

**`ClearAllWatches()`**: clears both dicts.

**`GetWatches()`**: returns `_watches.Values.ToList().AsReadOnly()`.

**`IsAnyWatchActive`**: `_watches.Count > 0`.

### 2.3 Implement OnPinValueChanged with watch dispatch

Full implementation of `OnPinValueChanged<T>`:

```csharp
public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged
{
    if (!_watchesByPinString.TryGetValue(pinId, out var watch))
        return;  // no watch for this pin -- zero allocation path

    watch.WriteValue(value, self, _view.Tick);

    // Fire event only if there are listeners (avoid ToArray() allocation when no listeners).
    var evt = _pinValueChangedHandlers;
    if (evt != null)
    {
        evt.Invoke(new PinValueChanged(
            self,
            pinId,
            watch.LastValueBytes.ToArray(),    // 1 allocation only when listener present
            watch.ExpectedType,
            _view.Tick));
    }
}
```

Add the backing field for the event:
```csharp
private Action<PinValueChanged>? _pinValueChangedHandlers;
event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
{
    add    => _pinValueChangedHandlers += value;
    remove => _pinValueChangedHandlers -= value;
}
```

### 2.4 Add MarshalFromBytes helper

Static helper for UI/inspection decoding (NOT on the probe path):

```csharp
public static object? MarshalFromBytes(byte[] bytes, Type type)
{
    if (bytes == null || bytes.Length == 0) return null;
    if (type == typeof(int))    return System.Runtime.InteropServices.MemoryMarshal.Read<int>(bytes);
    if (type == typeof(float))  return System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes);
    if (type == typeof(bool))   return bytes[0] != 0;
    if (type == typeof(uint))   return System.Runtime.InteropServices.MemoryMarshal.Read<uint>(bytes);
    if (type == typeof(long))   return System.Runtime.InteropServices.MemoryMarshal.Read<long>(bytes);
    if (type == typeof(double)) return System.Runtime.InteropServices.MemoryMarshal.Read<double>(bytes);
    // Fallback for unrecognized types: return raw bytes as-is
    return bytes;
}
```

Place as a static method on `BlueprintDebugSession` or a new static class `WatchMarshal` in the same file.

### 2.5 Mark watches stale on structure hash mismatch

In `RegisterDebugMap`, after clearing breakpoints on hash mismatch, also mark watches stale:
```csharp
foreach (var watch in _watches.Values.Where(w => w.AssetId == map.AssetId))
    watch.IsStale = true;
// Fire OnWatchStale for each affected watch (stub event, no subscribers yet)
```

---

## 3. TASK-DBG-005: Multi-Entity Debugging, PDB Integration, Hot Reload Interaction

See `TASK-DETAIL.md §DBG-005` for full scope and success conditions.

### 3.1 Entity filter

Add state:
```csharp
private Entity? _entityFilter;
```

Implement:
```csharp
public void SetEntityFilter(Entity? entity) => _entityFilter = entity;
public Entity? GetEntityFilter() => _entityFilter;
```

In `OnNodeEnter`, add the entity filter check at the very top:
```csharp
if (_entityFilter.HasValue && self != _entityFilter.Value) return;
```

In `OnPinValueChanged<T>`, also apply entity filter at top:
```csharp
if (_entityFilter.HasValue && self != _entityFilter.Value) return;
```

**Add to `IBlueprintDebugSession` interface:**
```csharp
void SetEntityFilter(Entity? entity);
Entity? GetEntityFilter();
```

**Update `CapturingDebugSession`** with stub implementations.

### 3.2 GetActiveEntities

The session tracks which entities are currently executing a blueprint via `OnPeerCallEnter` / `Exit`. Extend `_currentCallDepth` tracking to also track a set of "active entities" per asset (entities where call depth > 0).

For simplicity, use a `HashSet<Entity>` per assetId:
```csharp
private readonly Dictionary<Guid, HashSet<Entity>> _activeEntities = new();
```

In `OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)`: when depth goes 0 → 1, add entity to active set. Look up asset by name from `_debugMaps` or use `Guid.Empty` as fallback.

In `OnPeerCallExit(Entity entity)`: when depth goes 1 → 0, remove entity from active set.

Implement `GetActiveEntities(Guid assetId)`:
```csharp
public IReadOnlyList<Entity> GetActiveEntities(Guid assetId)
    => _activeEntities.TryGetValue(assetId, out var set)
        ? set.ToList().AsReadOnly()
        : Array.Empty<Entity>().AsReadOnly();
```

**Add to `IBlueprintDebugSession`:**
```csharp
IReadOnlyList<Entity> GetActiveEntities(Guid assetId);
```

### 3.3 PDB locator (stub)

Per TASK-DBG-005 scope, implement:
```csharp
private readonly Dictionary<Guid, Func<string>> _pdbLocators = new();

public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver)
    => _pdbLocators[assetId] = pdbPathResolver;
```

**Add to `IBlueprintDebugSession`:**
```csharp
void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver);
```

In `HandleBreakpointHit`, populate `BreakpointHit.SourceFilePath` and `BreakpointHit.SourceLine` if:
- A PDB locator is registered for the breakpoint's asset
- A debug map is registered for that asset
- The node entry has a `SourceStartLine`

**Note:** `BreakpointHit` record currently has `Self, NodeId, AssetId, SimulationTime, Tick`. Adding `SourceFilePath` and `SourceLine` requires extending the record. Add them as optional (nullable) members:
```csharp
public sealed record BreakpointHit(
    Entity Self,
    string NodeId,
    Guid AssetId,
    float SimulationTime,
    uint Tick,
    string? SourceFilePath = null,
    int? SourceLine = null);
```

Update all existing `new BreakpointHit(...)` call sites with the new optional parameters.

### 3.4 Hot Reload interaction

Implement per TASK-DBG-005:

```csharp
public void OnHotReloadBegin()
{
    if (_isPaused) Continue();
    // Mark all watches as stale (reload invalidates runtime state)
    foreach (var watch in _watches.Values)
        watch.IsStale = true;
    OnSessionStateChanged?.Invoke();
}

public void OnHotReloadCompleted(Guid[] reloadedAssetIds)
{
    // Re-validate watches: if watch's asset was reloaded and is now in _debugMaps, clear stale flag
    foreach (var assetId in reloadedAssetIds)
    {
        foreach (var watch in _watches.Values.Where(w => w.AssetId == assetId))
            watch.IsStale = false;  // map was reloaded; watch is valid again
        // Fire breakpoint list changed for affected assets
        OnBreakpointListChanged?.Invoke(assetId);
    }
    OnSessionStateChanged?.Invoke();
}
```

**Add to `IBlueprintDebugSession`:**
```csharp
void OnHotReloadBegin();
void OnHotReloadCompleted(Guid[] reloadedAssetIds);
```

---

## 4. Tests Required

### 4.1 WatchTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/WatchTests.cs`:

**SC1: `AddWatch_OnPinValueChanged_NoListener_ZeroAllocation`**:
- Add a watch for a known pin ID.
- Warm up `OnPinValueChanged<int>(E1, pinIdStr, 42)`.
- Measure allocation: must be 0 (no listener = no `ToArray()`).

**SC2: `AddWatch_OnPinValueChanged_WithListener_ExactlyOneAllocation`**:
- Add a watch. Subscribe to `OnPinValueChangedEvent`.
- Warm up. Measure allocation for `OnPinValueChanged<int>` with a listener -- must be exactly `sizeof(byte[])` overhead (one allocation: the `ToArray()`).

**SC3: `Watch_WriteValue_Matrix4x4_StoresCorrectBytes`**:
- Use `System.Numerics.Matrix4x4` (64 bytes). Call `WriteValue<Matrix4x4>(matrix, E1, 0u)`.
- Assert `LastValueBytes.Length == 64`.
- Assert `Watch.HasEverBeenWritten == true`.

**SC4: `Watch_WriteValue_OversizedStruct_ThrowsInvalidOperationException`**:
- Create a dummy struct > 64 bytes. Call `WriteValue`.
- Assert `InvalidOperationException` thrown.

**SC5: `MarshalFromBytes_Int_DecodeCorrectly`**:
- `BitConverter.GetBytes(12345)`, call `MarshalFromBytes(bytes, typeof(int))` -- assert `(int)result == 12345`.

**SC6: `Watch_IsStale_SetOnHashMismatch`**:
- Add a watch for an asset. Register map v1 (hash 0x1111). Register map v2 (hash 0x2222).
- Assert watch `IsStale == true`.

### 4.2 MultiEntityTests.cs

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/MultiEntityTests.cs`:

**SC1: `EntityFilter_Set_SkipsNonMatchingEntity_OnBreakpoint`**:
- Set breakpoint. `SetEntityFilter(E1)`. Call `OnNodeEnter(E2, bpNode)`.
- Assert session `IsPaused == false` (E2 was filtered out).

**SC2: `EntityFilter_Set_PausesMatchingEntity`**:
- Set breakpoint. `SetEntityFilter(E1)`. Call `OnNodeEnter(E1, bpNode)`.
- Assert `IsPaused == true`.

**SC3: `OnHotReloadBegin_WhenPaused_CallsContinue`**:
- Hit a breakpoint (session is paused). Call `OnHotReloadBegin()`.
- Assert `session.IsPaused == false`.
- Assert `MockTimeController.ResumeCount == 1`.

**SC4: `OnHotReloadCompleted_MarksStalWatchesAsValid`**:
- Add watch for AssetIdA. Register map v2 (hash mismatch → watch becomes stale). 
- Call `OnHotReloadCompleted(new[] { AssetIdA })`.
- Assert watch `IsStale == false`.

---

## 5. Verification

```powershell
# Debug tests only
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Debug" -v minimal

# Full suite -- must be 0 failures
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 failures. Total count >= 412 (401 + ~11 new tests).

---

## 6. Mandatory Task Progression

1. Fix DEBT-021 minor (remove `OnBreakpointListChanged` fire from hit-count update path, add comment on pseudo-BP sentinel).
2. Implement `Watch` class (full implementation per §2.1).
3. Implement `AddWatch`, `RemoveWatch`, `ClearAllWatches`, `GetWatches`, `IsAnyWatchActive`.
4. Implement `OnPinValueChanged<T>` with watch dispatch and zero-alloc path.
5. Add `MarshalFromBytes` helper.
6. Mark watches stale on hash mismatch in `RegisterDebugMap`.
7. Add entity filter state + `SetEntityFilter` / `GetEntityFilter` + apply in `OnNodeEnter`/`OnPinValueChanged`.
8. Implement `GetActiveEntities` with tracking in `OnPeerCallEnter/Exit`.
9. Implement `RegisterPdbLocator` + populate `BreakpointHit.SourceFilePath/SourceLine`.
10. Extend `BreakpointHit` record with optional source info; update all call sites.
11. Implement `OnHotReloadBegin` / `OnHotReloadCompleted`.
12. Update `IBlueprintDebugSession` interface with all new members.
13. Update `CapturingDebugSession` with stubs for all new interface members.
14. Write `WatchTests.cs` (6 tests) and `MultiEntityTests.cs` (4 tests).
15. Full suite 0 failures.
16. Commit and write report.

**DO NOT STOP.** Complete all tasks. Fix all compilation errors. Run tests and fix failures before writing the report.

---

## 7. Commit

After all tests pass:

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add .
git commit -m "feat(blueprints): BATCH-19 DBG-004 watch expressions + DBG-005 multi-entity hot reload

- Watch class: 64-byte pre-allocated buffer, WriteValue<T> zero-alloc, InvalidOperationException on oversize
- AddWatch/RemoveWatch/ClearAllWatches/GetWatches: proper WatchId-indexed storage + pin-string lookup
- OnPinValueChanged<T>: zero-alloc no-listener path; one-alloc listener path via watch buffer
- MarshalFromBytes: UI decode helper (primitives + raw fallback)
- RegisterDebugMap: marks watches stale on structure-hash mismatch
- Entity filter: SetEntityFilter/GetEntityFilter; OnNodeEnter/OnPinValueChanged skip filtered entities
- GetActiveEntities: per-asset entity tracking via OnPeerCallEnter/Exit depth events
- RegisterPdbLocator: source-line annotation on BreakpointHit (SourceFilePath, SourceLine)
- OnHotReloadBegin: continues if paused, marks all watches stale
- OnHotReloadCompleted: clears stale flag for reloaded assets
- WatchTests.cs: SC1-SC6 (6 tests)
- MultiEntityTests.cs: SC1-SC4 (4 tests)
- DEBT-021: removed spurious OnBreakpointListChanged from hit-count update path

Baseline: 401 pass / 5 skip / 0 fail -> target: 412+ pass / 5 skip / 0 fail"
```

---

## 8. Report

Submit to `.dev/blueprints-1/reports/BATCH-19-REPORT.md`. Required sections:
- Work completed per sub-task
- Test results
- Issues encountered + resolution
- Design decisions (esp. Watch buffer sizing, entity filter placement, hot reload interaction)
- Weak points spotted

---

## Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **SC1 zero-alloc test**: Use `GC.GetAllocatedBytesForCurrentThread()` with warm-up + `[NoInlining]` helper (same pattern as existing allocation tests). Measure ONLY the `OnPinValueChanged` call, not watch construction.
- **SC2 one-alloc test**: The single allocation is the `byte[]` from `watch.LastValueBytes.ToArray()`. Verify allocation count is exactly one (not zero, not two).
- **SC3 Matrix test**: Assert exact `LastValueBytes.Length == 64`; also assert `HasEverBeenWritten == true` and `UpdateCount == 1`.
- **SC3 Hot reload**: Verify BOTH `_isPaused == false` AND `ResumeCount == 1` (Continue was called).

---

## Success Criteria Summary

| SC | Task | Check |
|----|------|-------|
| SC1 | DBG-004 | `OnPinValueChanged<int>` with no listener: 0 allocations |
| SC2 | DBG-004 | `OnPinValueChanged<int>` with listener: exactly 1 allocation (the byte[]) |
| SC3 | DBG-004 | `WriteValue<Matrix4x4>`: `LastValueBytes.Length == 64`, `HasEverBeenWritten == true` |
| SC4 | DBG-004 | `WriteValue` with > 64 byte struct: `InvalidOperationException` |
| SC5 | DBG-004 | `MarshalFromBytes(bytes, typeof(int))` correctly decodes value |
| SC6 | DBG-004 | Watch marked stale on structure-hash mismatch |
| SC1 | DBG-005 | Entity filter blocks non-matching entity from triggering BP |
| SC2 | DBG-005 | Entity filter allows matching entity to trigger BP |
| SC3 | DBG-005 | `OnHotReloadBegin` while paused: session calls `Continue()`, `IsPaused == false` |
| SC4 | DBG-005 | `OnHotReloadCompleted`: stale watches cleared for reloaded asset |
| Build | All | `dotnet build` zero errors |
| Tests | All | 0 failures full suite |
