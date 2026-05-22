# BATCH-17: TASK-DBG-002 -- Debug Map Format and Node-ID Resolution

**Batch Number:** BATCH-17
**Tasks:** TASK-DBG-002
**Phase:** 5 -- Debug Protocol
**Estimated Effort:** 2-3 days
**Priority:** HIGH
**Dependencies:** BATCH-16 (DBG-001 baseline: `BlueprintDebugSession` skeleton, `IBlueprintDebugSession` interface, `MockTimeController` in place)

---

## 0. Onboarding

### Required Reading (IN ORDER)

1. `.dev/blueprints-1/reviews/BATCH-16-REVIEW.md` -- current state, new DEBT-018/019/020.
2. `.dev/blueprints-1/TASK-DETAIL.md` §DBG-002 -- full scope and success conditions.
3. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` §4 (Debug map format) and §5 (Node-ID resolution and structure-hash safety) -- primary design reference.
4. `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md` -- Patch 1 and Patch 2 (both already applied in BATCH-16; read for context).
5. `.dev/blueprints-1/DEBT-TRACKER.md` -- review DEBT-018, DEBT-019, DEBT-020.

### Source Code Locations

- `BlueprintDebugSession.cs` (extend): `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`
- `IBlueprintDebugSession.cs` (extend): `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`
- Existing `DebugMap` types (READ BEFORE TOUCHING): `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs` and `DebugMapSerializer.cs`
- Test project: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`
- New test file to create: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugMapTests.cs`

### Report Submission

Submit to: `.dev/blueprints-1/reports/BATCH-17-REPORT.md`

If questions arise: `.dev/blueprints-1/questions/BATCH-17-QUESTIONS.md`

---

## 1. Context and Key Design Decisions

### Existing DebugMap vs. Design Doc

**Read first:** `Hrot.Blueprints.Core.Compiler.Emit.DebugMap` already exists (created in CP-005). Its current shape:

```csharp
// Hrot.Blueprints.Core.Compiler.Emit
public sealed record DebugMap
{
    public Guid  AssetId       { get; init; }
    public int   BlueprintId   { get; init; }
    public ulong StructureHash { get; init; }
    public IReadOnlyList<DebugMapEntry> Entries { get; init; }
}

public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine);
```

The design doc §4 requires richer entries with `NodeKind`, `DisplayName`, and `PhaseIndex`. The session's hot-path lookup (from §4.5) also needs a `DebugMapIndex` wrapper that indexes by both string and Guid keys.

**Your task:** Reconcile the existing minimal `DebugMap` with the design doc's richer model. The strategy is:

1. **Extend `DebugMapEntry`** to add `NodeKind`, `DisplayName`, and `PhaseIndex?` fields. Keep existing constructor shape intact (add new parameters with defaults so existing code still compiles). Update `DebugMapBuilder.Record(...)` and `DebugMapSerializer` to include the new fields.
2. **Create `DebugMapIndex`** in `Hrot.Blueprints.Core.Debug` (same folder as `BlueprintDebugSession.cs` -- see DEBT-018). This is the runtime lookup wrapper that the session holds. It wraps a `DebugMap` and pre-indexes entries.
3. **Add `RegisterDebugMap` / `UnregisterDebugMap`** to `IBlueprintDebugSession` and implement them in `BlueprintDebugSession`.
4. **Implement `ExecutionHistory` ring-buffer** and `GetNodeHistory`.

---

## 2. Task Details

### 2.1 Extend DebugMapEntry

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/DebugMapBuilder.cs`

Add `NodeKind`, `DisplayName`, `PhaseIndex?` to `DebugMapEntry`. The existing constructor is a positional record -- add optional fields carefully to maintain backward compat with all existing call sites (grep for `new DebugMapEntry(` before changing).

Updated record (example approach using non-positional additions):
```csharp
public sealed record DebugMapEntry(Guid NodeId, Guid GraphId, int StartLine, int EndLine)
{
    public string  NodeKind    { get; init; } = string.Empty;
    public string  DisplayName { get; init; } = string.Empty;
    public int?    PhaseIndex  { get; init; } = null;
}
```

Update `DebugMapBuilder.Record(...)` and `DebugMapSerializer` (both Serialize and Deserialize paths) to round-trip the new fields. The serializer DTO already has `NodeId, GraphId, StartLine, EndLine` -- add `NodeKind`, `DisplayName`, `PhaseIndex` to `EntryDto` and the mapping. Fields missing from old JSON should deserialize to their defaults (empty string / null).

**Check existing tests pass** after extending: `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "Stage8" -v minimal`.

### 2.2 Create DebugMapIndex

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/DebugMapIndex.cs`
**Namespace:** `Hrot.Blueprints.Core.Debug`

See design doc §4.5 for `DebugMapIndex`, `NodeMapEntry`, `PinMapEntry` (stub) and `GraphMapEntry` (stub). Implement:

```csharp
public sealed record NodeMapEntry(
    Guid   NodeId,
    string NodeIdString,
    Guid   GraphId,
    string NodeKind,
    string DisplayName,
    int    SourceStartLine,
    int    SourceEndLine,
    int?   PhaseIndex);

public sealed class DebugMapIndex
{
    public Guid   AssetId       { get; }
    public string AssetName     { get; }  // use DebugMap.AssetId.ToString("D") if AssetName not yet in DebugMap
    public ulong  StructureHash { get; }

    // populated from DebugMap.Entries
    private readonly Dictionary<string, NodeMapEntry> _nodesByString; // ordinal key
    private readonly Dictionary<Guid, NodeMapEntry>   _nodesByGuid;

    public DebugMapIndex(DebugMap map) { /* index entries */ }

    public NodeMapEntry? TryResolveNode(string nodeIdString) => ...;
    public NodeMapEntry? TryResolveNode(Guid nodeId) => ...;
    public IReadOnlyCollection<NodeMapEntry> AllNodes { get; }
}
```

The string key for each entry must use `entry.NodeId.ToString("D")` (lowercase, hyphenated) to match what the compiler emits in `DebugProbe.NodeEnter(self, nodeIdString)` calls. See Debug Protocol DD §5.1 for the exact format requirement.

### 2.3 Add RegisterDebugMap / UnregisterDebugMap to IBlueprintDebugSession

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

Add to the interface (in the `-- Map registration --` region, after the Lifecycle section):
```csharp
// -- Map registration --
void RegisterDebugMap(DebugMap map);
void UnregisterDebugMap(Guid assetId);
```

`DebugMap` lives in `Hrot.Blueprints.Core.Compiler.Emit` -- add the using statement.

**Update CapturingDebugSession** (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/CapturingDebugSession.cs`) to stub these (store maps in a dictionary or just no-op for the test double).

### 2.4 Implement RegisterDebugMap / UnregisterDebugMap in BlueprintDebugSession

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`

State:
```csharp
private readonly Dictionary<Guid, DebugMapIndex> _debugMaps = new();
```

`RegisterDebugMap(DebugMap map)`:
1. Build a `new DebugMapIndex(map)`.
2. If a map already exists for `map.AssetId` AND `existingIndex.StructureHash != map.StructureHash`:
   - Clear all breakpoints for that asset (iterate `_nodeBreakpoints` -- though currently it's a raw string set; refine as needed for the hash-check). Fire `OnBreakpointHit` is NOT fired here; the correct event is `OnBreakpointListChanged` -- see §2.5 below for adding that event.
   - Mark all watches for that asset as stale (stub for now, watches implemented in DBG-004).
3. Store `_debugMaps[map.AssetId] = index`.

`UnregisterDebugMap(Guid assetId)`:
1. Remove from `_debugMaps`.
2. Clear stale watches (stub).

### 2.5 Add OnBreakpointListChanged event

The design doc §5.3 specifies that on structure-hash mismatch, `OnBreakpointListChanged` fires. Add this to `IBlueprintDebugSession`:
```csharp
event Action<Guid>? OnBreakpointListChanged;  // Guid = assetId whose BP list changed
```

Add it to `BlueprintDebugSession` and `CapturingDebugSession`.

### 2.6 ExecutionHistory ring-buffer and GetNodeHistory

**New type:** `ExecutionHistory` (internal class, in `BlueprintDebugSession.cs` or a separate file in the same location)

Per design doc §2.3: per-entity ring-buffer, capacity 256 entries (configurable via `BlueprintTestFixtureOptions` or a constant), zero allocation on write (pre-allocated array, index wrapping).

```csharp
internal sealed class ExecutionHistory
{
    private readonly NodeHistoryEntry[] _buffer;
    private int _head;
    private int _count;

    public ExecutionHistory(int capacity = 256) { _buffer = new NodeHistoryEntry[capacity]; }

    public void Record(NodeHistoryEntry entry)
    {
        _buffer[_head % _buffer.Length] = entry;
        _head++;
        if (_count < _buffer.Length) _count++;
    }

    // Returns entries in chronological order (oldest first).
    public IReadOnlyList<NodeHistoryEntry> GetRecent(int maxCount)
    {
        var take = Math.Min(maxCount, _count);
        var result = new NodeHistoryEntry[take];
        var start = _head - take;
        for (int i = 0; i < take; i++)
            result[i] = _buffer[(start + i + _buffer.Length * 2) % _buffer.Length];
        return result;
    }
}
```

**In BlueprintDebugSession:**
```csharp
private readonly Dictionary<Entity, ExecutionHistory> _history = new();
```

**In `OnNodeEnter`:** After the breakpoint check, record the entry:
```csharp
if (!_history.TryGetValue(self, out var hist))
    _history[self] = hist = new ExecutionHistory();
hist.Record(new NodeHistoryEntry(nodeId, tick: 0u, simTime: 0f));
// tick and simTime: use _view.Tick and _view.Time if available (ISimulationView has Tick and Time properties)
```

**Implement `GetRecentNodeHistory`:**
```csharp
public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
    => throw new NotImplementedException(); // will fill per-entity in DBG-005; stub that returns empty
```

Wait -- `GetRecentNodeHistory` as currently defined doesn't take an Entity parameter. That's the DBG-005 concern. For now, implement a non-throwing version that returns empty; the per-entity version is deferred to DBG-005 per the design doc §9. Add a non-interface overload:
```csharp
public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
{
    if (!_history.TryGetValue(entity, out var hist)) return Array.Empty<NodeHistoryEntry>();
    return hist.GetRecent(maxCount);
}
```

---

## 3. Verification

After implementation, run:

```powershell
# Verify Stage8 tests still pass (DebugMap extension must not break serialization)
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "Stage8" -v minimal

# Run all debug tests
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Debug" -v minimal

# Full suite
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 failures. The Stage8 serialization tests must pass with the extended entry fields.

---

## 4. Commit

After all tests pass:

```powershell
cd d:\WORK\IOS-IG-SimHost-FDP
git add .
git commit -m "feat(blueprints): BATCH-17 TASK-DBG-002 debug map index and node-ID resolution

- Extend DebugMapEntry with NodeKind, DisplayName, PhaseIndex fields
- DebugMapSerializer: round-trips new entry fields; old JSON deserializes with defaults
- DebugMapIndex: dual string+Guid keyed runtime index for O(1) session lookup
- BlueprintDebugSession: RegisterDebugMap/UnregisterDebugMap, structure-hash safety, OnBreakpointListChanged event
- ExecutionHistory: 256-entry pre-allocated ring-buffer, zero alloc on write
- GetNodeHistory(Entity) non-interface overload returning per-entity history
- Tests: RegisterDebugMap + TryResolveNode SC1-SC5; ring-buffer wrap/overflow SC3; stale-map SC2

Baseline: 369 pass / 5 skip / 0 fail -> target: N+ pass / 5 skip / 0 fail"
```

> Check `git status` for FDP submodule changes before committing top-level. If any FDP files were modified, commit FDP submodule first.

---

## 5. Tests Required

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugMapTests.cs` with tests covering the TASK-DBG-002 success conditions:

**SC1 -- RegisterDebugMap + node resolution:**
- Build a `DebugMap` with 2+ entries (different NodeIds, GraphIds), call `session.RegisterDebugMap(map)`.
- Call `index.TryResolveNode(nodeIdString)` where `nodeIdString == nodeGuid.ToString("D")` -- assert returned `NodeMapEntry.NodeId == expected`.
- Call `index.TryResolveNode(nodeGuid)` -- same result.
- Call `index.TryResolveNode("unknown-id")` -- returns null.

**SC2 -- Structure-hash mismatch fires OnBreakpointListChanged:**
- Register map v1 (hash 0x1111...), then register map v2 for same assetId with different hash.
- Assert `OnBreakpointListChanged` fired with the correct assetId.
- Register map v3 for same assetId with SAME hash -- assert `OnBreakpointListChanged` NOT fired.

**SC3 -- Ring-buffer wraps at capacity:**
- Create `ExecutionHistory(capacity: 4)`.
- Record 6 entries (nodeIds "n1".."n6").
- Call `GetRecent(100)` -- assert 4 entries returned (ring is full), in chronological order: "n3", "n4", "n5", "n6".
- Zero allocation on `Record`: measure with `GC.GetAllocatedBytesForCurrentThread()` after warm-up.

**SC4 -- GetNodeHistory entity isolation:**
- Simulate `OnNodeEnter` calls for two distinct entities E1 and E2 with different node IDs.
- Call `session.GetNodeHistory(E1, 100)` -- assert only E1's entries returned.

**SC5 -- DebugMapSerializer roundtrip with new fields:**
- Create `DebugMapEntry` with non-empty `NodeKind` and `DisplayName`.
- Serialize with `DebugMapSerializer.Serialize(map)` and deserialize with `Deserialize(json)`.
- Assert `NodeKind` and `DisplayName` round-trip correctly.
- Assert old JSON (without new fields) deserializes with default empty strings (no exception).

Minimum test count: 12-15 tests.

---

## 6. Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "RegisterDebugMap doesn't throw" without asserting what was stored.
- **REQUIRED:** Tests that verify actual lookup behavior -- resolve a known NodeId, get back the correct `NodeMapEntry` values.
- **REQUIRED:** Ring-buffer wrap test must verify both capacity enforcement AND chronological order.
- **REQUIRED:** The structure-hash mismatch test must verify the event fired AND the stale state (not just that it didn't throw).
- **REQUIRED:** Serializer roundtrip test must assert actual field values, not just that JSON is non-null.

**❗ DO NOT STOP EARLY**
Complete all tasks, fix all compilation errors and test failures, then write the report. Do not ask for permission to run tests or proceed with obvious steps.

---

## 7. Mandatory Task Progression

1. Read existing `DebugMap` types in `Compiler/Emit/` before writing anything.
2. Extend `DebugMapEntry` + update `DebugMapBuilder` + `DebugMapSerializer`.
3. Verify Stage8 tests still pass.
4. Create `DebugMapIndex.cs`.
5. Add `RegisterDebugMap/UnregisterDebugMap` + `OnBreakpointListChanged` to interface + implementations.
6. Implement `ExecutionHistory` ring-buffer + `GetNodeHistory`.
7. Write all tests in `DebugMapTests.cs`.
8. Full suite green -- 0 failures.
9. Commit.
10. Write report.

---

## 8. Report

Submit to `.dev/blueprints-1/reports/BATCH-17-REPORT.md`. Required sections:
- Work completed (each sub-task above)
- Test results (before / after)
- Issues encountered and how you resolved them
- Design decisions made beyond the spec (e.g., how you reconciled existing DebugMap with the richer format)
- Weak points spotted in existing code

---

## Success Criteria Summary

| SC | Task | Check |
|----|------|-------|
| SC1 | DBG-002 | `RegisterDebugMap` + `TryResolveNode(string)` + `TryResolveNode(Guid)` return correct entries |
| SC2 | DBG-002 | Structure-hash mismatch fires `OnBreakpointListChanged`; same-hash does not |
| SC3 | DBG-002 | Ring-buffer wraps at capacity; `GetRecent` returns chronologically ordered entries; zero alloc on write |
| SC4 | DBG-002 | `GetNodeHistory(Entity)` returns entity-specific entries only |
| SC5 | DBG-002 | `DebugMapSerializer` roundtrips `NodeKind`/`DisplayName`; old JSON deserializes without error |
| Build | All | `dotnet build` zero errors |
| Tests | All | 0 failures (Stage8 tests pass; new Debug tests pass) |
