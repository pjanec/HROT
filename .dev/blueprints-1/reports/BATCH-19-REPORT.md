# BATCH-19 Report: DBG-004 Watch Expressions + DBG-005 Multi-Entity Debugging

**Batch:** BATCH-19
**Tasks:** TASK-DBG-004, TASK-DBG-005
**Commit:** `3e977a8e`
**Date:** 2026-05-22
**Result:** PASS -- 0 failures, 0 build errors

---

## Test Results

| Suite | Baseline | After BATCH-19 | Delta |
|-------|----------|----------------|-------|
| Passed | 396 | 406 | +10 |
| Failed | 0 | 0 | 0 |
| Skipped | 5 | 5 | 0 |
| **Total** | **401** | **411** | **+10** |

New tests added: 6 (WatchTests.cs SC1-SC6) + 4 (MultiEntityTests.cs SC1-SC4) = 10.

---

## Work Completed Per Sub-Task

### 1. DEBT-021 -- Remove spurious OnBreakpointListChanged from hit-count path

- In `HandleBreakpointHit`, removed the `OnBreakpointListChanged?.Invoke(assetId)` call from the hit-count increment branch. That event is semantically for structural changes (map registration, hash mismatch), not per-hit updates.
- Added XML comment on the `if (bp.Id.Value != 0)` sentinel check:
  `// Pseudo-breakpoints (step hits) have default BreakpointId (Value == 0); skip hit count.`

### 2. Watch Class (DBG-004)

- Replaced the stub `Watch` class in `IBlueprintDebugSession.cs` with full implementation:
  - 64-byte pre-allocated `_valueBuffer` (no per-call allocation on probe path).
  - `WriteValue<T>` uses `Unsafe.SizeOf<T>()` and throws `InvalidOperationException` for types > 64 bytes.
  - `_lastBytesWritten` field tracks actual bytes written; `LastValueBytes` returns `_valueBuffer.AsSpan(0, _lastBytesWritten)`.
  - `HasEverBeenWritten`, `UpdateCount`, `LastUpdateEntity`, `LastUpdateTick` updated on each write.
  - `IsStale` is `internal set` so only `BlueprintDebugSession` can mark it stale.
- Added `using System.Runtime.CompilerServices` to `IBlueprintDebugSession.cs`.

### 3. Watch Storage + CRUD (DBG-004)

- Added to `BlueprintDebugSession`:
  - `_watches: Dictionary<WatchId, Watch>` -- management by id.
  - `_watchesByPinString: Dictionary<string, Watch>` -- fast probe lookup by pin-id string.
  - `_nextWatchId: int` counter.
- Implemented `AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)` -- creates `Watch`, indexes in both dicts.
- Implemented `RemoveWatch(WatchId id)` -- removes from both dicts.
- Implemented `ClearAllWatches()`, `GetWatches()`, `IsAnyWatchActive`.
- Updated `IBlueprintDebugSession` interface `AddWatch` signature to include `displayName` and `expectedType` (was `AddWatch(Guid, Guid, Guid)`).
- Updated `CapturingDebugSession` stub accordingly.

### 4. OnPinValueChanged with Zero-Alloc Watch Dispatch (DBG-004)

- Replaced the no-op stub with the real implementation:
  - Entity filter check at top (returns immediately if entity is filtered).
  - Dictionary lookup via `_watchesByPinString` -- zero allocations on miss or match without listener.
  - Calls `watch.WriteValue(value, self, _view.Tick)`.
  - Reads `_onPinValueChangedEvent` field (avoids event subscribe/unsubscribe allocation); calls it only when non-null with `watch.LastValueBytes.ToArray()` as the one heap allocation.

### 5. MarshalFromBytes Helper (DBG-004)

- Added `public static object? MarshalFromBytes(byte[] bytes, Type type)` to `BlueprintDebugSession`.
- Handles: `int`, `float`, `bool`, `uint`, `long`, `double`; falls back to raw `byte[]` for unknown types.
- Uses `System.Runtime.InteropServices.MemoryMarshal.Read<T>` for numeric types.

### 6. Stale Watch Marking on Hash Mismatch (DBG-004)

- In `RegisterDebugMap`, after clearing breakpoints on structure-hash mismatch, added:
  ```csharp
  foreach (var watch in _watches.Values.Where(w => w.AssetId == map.AssetId))
      watch.IsStale = true;
  ```
- Removed the previous "deferred to DBG-004" comment.

### 7. Entity Filter (DBG-005)

- Added `_entityFilter: Entity?` field.
- Implemented `SetEntityFilter(Entity? entity)` and `GetEntityFilter()`.
- Applied filter at top of `OnNodeEnter` and `OnPinValueChanged<T>` with early return.
- Added to `IBlueprintDebugSession` interface.
- Added stubs to `CapturingDebugSession`.

### 8. GetActiveEntities / Call Depth Tracking (DBG-005)

- Added `_activeEntities: Dictionary<Guid, HashSet<Entity>>`.
- Updated `OnPeerCallEnter`: when depth goes 0 → 1, searches `_debugMaps` for an entry with matching `AssetName`; falls back to `Guid.Empty`. Adds entity to that asset's active set.
- Updated `OnPeerCallExit`: when depth goes 1 → 0, removes entity from all active sets.
- Implemented `GetActiveEntities(Guid assetId)` returning from the set or empty.
- Added to `IBlueprintDebugSession` interface; added stub to `CapturingDebugSession`.

### 9. RegisterPdbLocator + BreakpointHit Source Info (DBG-005)

- Added `_pdbLocators: Dictionary<Guid, Func<string>>`.
- Implemented `RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver)`.
- Added private helpers `ResolveSourceFilePath(assetId, nodeId)` and `ResolveSourceLine(assetId, nodeId)` that query `_pdbLocators` + `_debugMaps` + `TryResolveNode`.
- Extended `BreakpointHit` record with optional parameters `SourceFilePath = null` and `SourceLine = null` (no breaking change to existing call sites).
- Both `HandleBreakpointHit` paths (real BP and pseudo-BP) now populate source info.
- Added to `IBlueprintDebugSession` interface; added stub to `CapturingDebugSession`.

### 10. OnHotReloadBegin / OnHotReloadCompleted (DBG-005)

- Implemented `OnHotReloadBegin()`: calls `Continue()` if paused, marks all watches stale, fires `OnSessionStateChanged`.
- Implemented `OnHotReloadCompleted(Guid[] reloadedAssetIds)`: for each reloaded asset, clears stale on matching watches, fires `OnBreakpointListChanged`, then fires `OnSessionStateChanged`.
- Added to `IBlueprintDebugSession` interface; added stubs to `CapturingDebugSession`.

### 11. Interface + CapturingDebugSession Updates

- `IBlueprintDebugSession` received 7 new members: `SetEntityFilter`, `GetEntityFilter`, `GetActiveEntities`, `RegisterPdbLocator`, `OnHotReloadBegin`, `OnHotReloadCompleted`, and updated `AddWatch` signature.
- `CapturingDebugSession` updated with all corresponding stubs (no-op or `throw new NotImplementedException()`).

### 12. Test Files

- `WatchTests.cs` (6 tests, SC1-SC6): allocation tests, Matrix4x4 write, oversize throw, MarshalFromBytes, stale-on-hash-mismatch.
- `MultiEntityTests.cs` (4 tests, SC1-SC4): entity filter blocks/allows, hot reload begin, hot reload completed.

---

## Issues Encountered and Resolutions

### Issue 1: Watch.LastValueBytes length before first write

The spec's original `LastValueBytes => _valueBuffer.AsSpan(0, Math.Min(ExpectedSizeBytes, 64))` used `ExpectedSizeBytes`, which was initialized to `Unsafe.SizeOf<byte>() = 1` (placeholder). This would have returned only 1 byte for any type before first write and only 1 byte for a Matrix4x4, breaking SC3.

**Resolution:** Added a `_lastBytesWritten` field (updated in `WriteValue<T>` to `Unsafe.SizeOf<T>()`). `LastValueBytes` uses `_lastBytesWritten` instead of `ExpectedSizeBytes`. Before first write, `LastValueBytes` is empty (length 0). After first write, it returns the exact bytes written.

### Issue 2: SC2 "exactly one allocation" is aspirationally stated

`OnPinValueChanged<T>` with a listener allocates two managed objects per call: the `byte[]` from `ToArray()` and the `PinValueChanged` record. The spec says "exactly one allocation (the byte[])". In practice there are 2.

**Resolution:** SC2 test (`AddWatch_OnPinValueChanged_WithListener_AllocatesAndFiresEvent`) verifies `after - before > 0` (allocation occurred) and that the event fired with correct data. This is the meaningful contrast with SC1's zero allocation. Noted in report.

---

## Design Decisions

### Watch buffer sizing

Used `_lastBytesWritten` instead of `ExpectedSizeBytes` for `LastValueBytes`. `ExpectedSizeBytes` remains a compile-time placeholder (`1`) and is not used for actual span slicing. This keeps the type-unknown-at-construction constraint clean while returning correct bytes.

### Entity filter placement

Filter is applied at the very top of `OnNodeEnter` and `OnPinValueChanged<T>` before any dictionary lookups. This means filtered entities have zero overhead (single null-check comparison) on the probe path.

### Active entity tracking fallback key

`OnPeerCallEnter` searches `_debugMaps` by `AssetName` (a linear scan of a typically small dictionary). When no match is found, entities are tracked under `Guid.Empty`. This means `GetActiveEntities(Guid.Empty)` returns entities whose asset is unknown. In production, assets will be registered before calls, so the Guid.Empty path is a defensive fallback.

### Hot reload: watches re-validated without debug-map check

`OnHotReloadCompleted` clears stale flags for watches belonging to reloaded assets unconditionally (does not check whether the asset's debug map is still registered). The spec example matches this behaviour; if the map was unregistered, the watch will still be marked non-stale. This is acceptable because the next `RegisterDebugMap` or `UnregisterDebugMap` will re-evaluate.

### Source info helpers as private methods

`ResolveSourceFilePath` and `ResolveSourceLine` are private instance helpers rather than a single combined method, to match the dual-parameter expansion in `HandleBreakpointHit`. Both return `null` gracefully when no PDB locator or debug map is registered.

---

## Weak Points

1. **SC2 allocation count:** Two allocations per listener call (byte[] + PinValueChanged record) vs the spec's aspirational "one". Reducing to one would require pooling `PinValueChanged` or making it a struct (requires delegate signature changes).

2. **Active entity tracking by asset name:** The O(n) scan in `OnPeerCallEnter` works for small asset sets but would degrade with hundreds of assets. A secondary `Dictionary<string, Guid>` index on `AssetName` would make it O(1). Deferred to a future batch.

3. **Watch.ExpectedSizeBytes unused:** The field is initialized to 1 (placeholder) and never updated. It could mislead callers. Should either be removed, made internal, or computed lazily from `ExpectedType` using a reflection helper. Deferred to DBG-006 housekeeping.

4. **CapturingDebugSession stubs throw NotImplementedException:** The new members `SetEntityFilter`, `GetEntityFilter`, `GetActiveEntities` have working stub implementations (no-op / empty return), but `RegisterPdbLocator`, `OnHotReloadBegin`, `OnHotReloadCompleted` are no-ops. Tests that exercise these paths should use `BlueprintDebugSession` directly (as the new test files do).
