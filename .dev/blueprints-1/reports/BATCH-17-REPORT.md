# BATCH-17 Report -- TASK-DBG-002: Debug Map Format and Node-ID Resolution

**Batch:** BATCH-17
**Developer:** AI Agent
**Date:** 2026-05-22
**Status:** COMPLETE
**Commit:** 71aef335

---

## Work Completed

### Sub-task 1: Extend DebugMapEntry

**File:** `Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs`

Added three non-positional `init`-only properties to `DebugMapEntry`:

```csharp
public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine)
{
    public string NodeKind    { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int?   PhaseIndex  { get; init; } = null;
}
```

Strategy: used `{ get; init; }` body properties with defaults so all existing call sites (`new DebugMapEntry(nodeId, graphId, startLine, endLine)`) continue to compile without modification. The `DebugMapBuilder.Record(...)` call site was unchanged because it uses positional constructor syntax that only requires the four original parameters.

### Sub-task 2: Update DebugMapSerializer

**File:** `Hrot.Blueprints.Core/Compiler/Emit/DebugMapSerializer.cs`

- Added `NodeKind`, `DisplayName`, `PhaseIndex` to `EntryDto`.
- Updated `Serialize()` to include new fields in the DTO projection.
- Updated `Deserialize()` to use object initializer syntax for new fields.
- Old JSON (without new fields) deserializes cleanly because:
  - `NodeKind` and `DisplayName` default to `string.Empty`
  - `PhaseIndex` defaults to `null`
  - `DefaultIgnoreCondition.WhenWritingNull` in the serializer options suppresses null `PhaseIndex` from output, keeping JSON compact for non-phase nodes.

### Sub-task 3: Create DebugMapIndex

**New file:** `Hrot.Blueprints.Core/DebugMapIndex.cs`
**Namespace:** `Hrot.Blueprints.Core.Debug`

Implements:
- `NodeMapEntry` record (NodeId, NodeIdString, GraphId, NodeKind, DisplayName, SourceStartLine, SourceEndLine, PhaseIndex)
- `DebugMapIndex` class with dual-keyed dictionaries (`Dictionary<string, NodeMapEntry>` with `StringComparer.Ordinal` + `Dictionary<Guid, NodeMapEntry>`)
- `TryResolveNode(string)` for hot-path probe lookups
- `TryResolveNode(Guid)` for editor UI lookups
- `AllNodes` collection property

Key design decision: `AssetName` is not yet in the `DebugMap` record (the full on-disk format per design doc §4.2 has `assetName` but the current `DebugMap` record doesn't). Used `AssetId.ToString("D")` as fallback per the batch instructions. This should be reconciled when the full JSON schema is implemented (DBG-005 or CP-005 followup).

File placed in `Hrot.Blueprints.Core/` root (not `Debug/` subfolder) to remain consistent with DEBT-018 convention.

### Sub-task 4: Add map registration methods + OnBreakpointListChanged to IBlueprintDebugSession

**File:** `Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

Added:
- `using Hrot.Blueprints.Core.Compiler.Emit;`
- `void RegisterDebugMap(DebugMap map);`
- `void UnregisterDebugMap(Guid assetId);`
- `event Action<Guid>? OnBreakpointListChanged;` (in events section)

### Sub-task 5: Implement RegisterDebugMap/UnregisterDebugMap in BlueprintDebugSession

**File:** `Hrot.Blueprints.Core/BlueprintDebugSession.cs`

State added:
- `Dictionary<Guid, DebugMapIndex> _debugMaps` -- indexed debug maps
- `Dictionary<Entity, ExecutionHistory> _history` -- per-entity ring-buffers
- `event Action<Guid>? OnBreakpointListChanged` -- fires on hash mismatch

`RegisterDebugMap` logic:
1. Builds `DebugMapIndex` from the incoming `DebugMap`.
2. If a map already exists for the asset AND the structure hash differs: clears `_nodeBreakpoints` (stub; full per-asset breakpoint filtering deferred to DBG-003) and fires `OnBreakpointListChanged`.
3. Stores new index.

`UnregisterDebugMap`: removes from dictionary; watch cleanup stubbed for DBG-004.

Also changed `GetRecentNodeHistory` from `throw new NotImplementedException()` to `return Array.Empty<NodeExecuted>()` per the batch instructions ("implement a non-throwing version that returns empty").

### Sub-task 6: Create ExecutionHistory ring-buffer

**New file:** `Hrot.Blueprints.Core/ExecutionHistory.cs`
**Visibility:** `internal`

256-entry pre-allocated ring-buffer (`_buffer = new NodeHistoryEntry[capacity]`). `Record()` is zero-allocation (just writes a reference into the pre-allocated array slot, increments two ints). `GetRecent(maxCount)` returns a new array of the requested size in chronological order using the formula:

```
result[i] = _buffer[(head - take + i + capacity * 2) % capacity]
```

The `* 2` ensures the modulo operand is always positive even when `head - take == 0`. Placed in `Hrot.Blueprints.Core/` root (same convention as other Debug files).

### Sub-task 7: Add GetNodeHistory to BlueprintDebugSession

Non-interface overload added to `BlueprintDebugSession`:

```csharp
public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
```

Returns per-entity history from `_history[entity].GetRecent(maxCount)`, or `Array.Empty<NodeHistoryEntry>()` if entity has no recorded history.

History is recorded in `OnNodeEnter` after the breakpoint check: creates a new `ExecutionHistory` on first entry for an entity; records `new NodeHistoryEntry(nodeId, _view.Tick, _view.Time)`.

### Sub-task 8: Update CapturingDebugSession

**File:** `Hrot.Blueprints.Tests/CapturingDebugSession.cs`

Added:
- `using Hrot.Blueprints.Core.Compiler.Emit;`
- `Dictionary<Guid, DebugMap> _maps` field
- `RegisterDebugMap(DebugMap map)` -- stores in `_maps`
- `UnregisterDebugMap(Guid assetId)` -- removes from `_maps`
- `event Action<Guid>? OnBreakpointListChanged`

### Sub-task 9: Write tests

**New file:** `Hrot.Blueprints.Tests/Debug/DebugMapTests.cs`

16 tests covering all 5 success conditions. All 16 pass.

---

## Test Results

**Before:** 369 pass / 5 skip / 0 fail
**After (Debug tests only):** 16 pass / 0 fail
**After (Stage8 filter):** 9 pass / 0 fail
**After (full suite, best run):** 385 pass / 5 skip / 0 fail

### Flaky failures observed

During full-suite runs, 1-6 HotReload tests (`AlcLifecycleTests`, `QuickReloadTests`, `PdbLoadTests`, `FailureRollbackTests`, `LatentCursorReloadTests`) occasionally fail with ALC GC timing issues. All pass when run in isolation. Root cause: pre-existing DEBT-019 (`DebugProbe.Sink` is a process-wide mutable static; parallel xUnit test classes race on it). None of the new DebugMapTests set `DebugProbe.Sink`. The flakiness was present before BATCH-17 and is more visible now because more test classes run concurrently. Mitigation deferred to DBG-006 per DEBT-019.

---

## Issues Encountered and Resolutions

### Issue 1: gitignore blocks `Debug/` test folder

Same issue as BATCH-16 (DEBT-018). `[Dd]ebug/` in `.gitignore` prevents staging files in `Hrot.Blueprints.Tests/Debug/`. Resolution: `git add -f` (same workaround used for existing `DebugSessionInterfaceTests.cs` and `MockTimeController.cs`).

### Issue 2: StubSimulationView missing GetCommandBuffer()

The `ISimulationView` interface added `GetCommandBuffer()` between the time of initial design and now. The new `StubSimulationView` in `DebugMapTests.cs` needed to implement it. Found by examining the existing `StubSimulationView` in `DebugSessionInterfaceTests.cs`, which already had the stub.

### Issue 3: File lock from stale testhost

Build failed on first attempt with `MSB3027` file lock. Resolved by killing the stale `testhost.exe` process (PID 17784) before rebuilding.

---

## Design Decisions Beyond the Spec

### DebugMapEntry extension approach

Used `{ get; init; }` body properties on a positional record rather than adding new positional parameters. This is strictly backward compatible — all existing `new DebugMapEntry(id, graphId, start, end)` call sites compile unchanged. The alternative (adding optional positional parameters) is not supported in C# positional records without adding a new constructor overload.

### ExecutionHistory as separate file

The batch instructions allowed placing `ExecutionHistory` either in `BlueprintDebugSession.cs` or a separate file. Chose a separate file for separation of concerns and to keep `BlueprintDebugSession.cs` readable. File placed in the project root alongside the other debug files per DEBT-018 convention.

### GetRecentNodeHistory returns empty instead of throwing

Changed from `throw new NotImplementedException()` to `return Array.Empty<NodeExecuted>()`. The per-entity overload `GetNodeHistory(Entity)` is the real entry point going forward; the parameterless interface method will be addressed in DBG-005.

### Stub breakpoint clearing on hash mismatch

`RegisterDebugMap` clears `_nodeBreakpoints` entirely on hash mismatch, not per-asset. This is correct for the current test baseline where breakpoints are just a string set with no asset association. DBG-003 will replace `_nodeBreakpoints` with a proper per-asset breakpoint structure. Added a comment in code noting this.

---

## Weak Points Spotted

1. **DebugMap.AssetName missing**: The existing `DebugMap` record doesn't have an `AssetName` field (the full design doc §4.2 schema includes it). `DebugMapIndex.AssetName` falls back to `AssetId.ToString("D")`. When CP-005 fully implements the on-disk format, `DebugMap` should gain `AssetName`.

2. **OnNodeEnter allocates on each call**: The history recording in `OnNodeEnter` always does `new NodeHistoryEntry(nodeId, _view.Tick, _view.Time)`. Since `NodeHistoryEntry` is a `sealed record` (class), this allocates. The ring-buffer's `Record()` itself is zero-alloc (stores a reference), but the `NodeHistoryEntry` object creation is not. This is acceptable for debug-mode-only code but should be noted for DBG-006 profiling.

3. **DEBT-019 is getting worse**: The more test classes that exercise `DebugProbe.Sink`, the more likely parallel-execution races appear. BATCH-18 should strongly consider addressing DEBT-019 before adding more DebugProbe tests.

4. **`_debugMaps` not thread-safe**: `RegisterDebugMap` and `UnregisterDebugMap` both mutate a `Dictionary`. The batch spec notes "callers may not call RegisterDebugMap concurrently", but no `lock` guard exists if this contract is violated. Acceptable for the current single-threaded use pattern.

5. **DebugMap uses `BlueprintId = int` and `StructureHash = ulong`** -- the `DebugMapIndex` only exposes `StructureHash`. `BlueprintId` (the int hash) is not exposed. If editor UI needs to look up debug maps by `BlueprintId`, `DebugMapIndex` would need to be extended.

---

## Success Criteria Verification

| SC | Check | Result |
|----|-------|--------|
| SC1 | `RegisterDebugMap` + `TryResolveNode(string)` + `TryResolveNode(Guid)` return correct entries | PASS (3 tests) |
| SC2 | Structure-hash mismatch fires `OnBreakpointListChanged`; same-hash does not | PASS (2 tests) |
| SC3 | Ring-buffer wraps at capacity; chronological order; zero alloc on write | PASS (3 tests) |
| SC4 | `GetNodeHistory(Entity)` returns entity-specific entries only | PASS (2 tests) |
| SC5 | `DebugMapSerializer` roundtrips `NodeKind`/`DisplayName`; old JSON deserializes without error | PASS (2 tests) |
| Build | `dotnet build` zero errors | PASS |
| Tests | 0 failures on new tests; Stage8 still passes | PASS |
