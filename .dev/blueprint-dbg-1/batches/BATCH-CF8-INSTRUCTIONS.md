# BATCH-CF8: Persist & Restore Debug Session

**Batch Number:** BATCH-CF8  
**Tasks:** CF-8 (Persist & restore debug session)  
**Phase:** Corrective Features (CF)  
**Estimated Effort:** 8-12 hours  
**Priority:** HIGH  
**Dependencies:** CF-7-rev (auto-instrumentation)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Implement debug session persistence: save node breakpoints + data breakpoints (with their JIT-compiled conditions as DTOs) + watches to a user-local gitignored file. On editor restart, restore them → CF-7-rev auto-instruments → breakpoints are active without manual Compile. Stale entries (deleted nodes) are retained but disabled per BPF-003.

### Required Reading (IN ORDER)
1. **Design Addendum:** `.dev/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md` — §7 (Session persistence), §5 (Storage & lifecycle)
2. **Task Detail:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch CF-8 section
3. **Existing code to extend:**
   - `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs` — serialize pattern to generalize
   - `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs` — DBM Breakpoint record
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` — session Breakpoint + Watch types
   - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — session implementation

### Source Code Location
- **Persistence:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/` — generalize WatchPersistence
- **Session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
- **Editor wiring:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- **Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/`

### Report Submission
`.dev/blueprint-dbg-1/reports/BATCH-CF8-REPORT.md`

### Zoo Operating Rules (SAME AS CF-7-rev)
- Do NOT delete, skip, weaken, or change existing test assertions
- Do NOT regenerate golden snapshots
- Report full failing-test set by name before and after
- Editor CLOSED during build
- Gate: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors
- `Hrot.Blueprints.Tests` → 7 pre-existing failures, 0 new

---

## 🎯 Batch Objectives

1. Create `DebugSessionPersistence` — generalize `WatchPersistence` to save/load all debug state
2. Save: node breakpoints (session) + data breakpoints (DBM) + watches (session) → `.debug/bpsession.json`
3. Wire save triggers: debounced on change, immediate on app close
4. Wire restore on editor startup: load file, restore breakpoints/watches, trigger CF-7-rev instrumentation
5. Handle stale entries: nodes deleted after save → retained as disabled

---

## Architecture Decisions (from addendum §7, architect-confirmed)

- **File:** `.debug/bpsession.json` at repo root (already gitignored by CF-7-rev)
- **What's saved:**
  - Node breakpoints: `(assetId, graphId, authoredNodeId, enabled)` — authored node id, NOT probe id
  - Data breakpoints: `(SearchPredicateDto Condition, DisplayName, SourceElementId, Enabled, IsWatch)`
  - Watches: `(assetId, graphId, pinId, displayName, expectedTypeAssemblyQualifiedName)`
- **Conditions saved as DTOs** — never serialize the compiled delegate; recompile via `PredicateCompiler` on load
- **Save triggers:** on change (debounced 500ms) + on editor/asset close
- **Restore:** load file → populate DBM + session → CF-7-rev trigger instruments each asset
- **Stale-but-retained:** if a saved node no longer exists, keep as `IsStale=true`, don't drop
- **Filter:** DBM breakpoints with standalone `ExternalHitTagPredicateDto` are NOT saved separately (they're node breakpoints recreated on restore via `session.SetBreakpoint`)

---

## ✅ Tasks

### Task 1: Create DebugSessionPersistence DTO and save/load logic

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DebugSessionPersistence.cs` (NEW)

Generalize `WatchPersistence` (which only saves `IsWatch` breakpoints) into a persistence class that saves the full debug session.

**DTOs:**

```csharp
public sealed class DebugSessionFile
{
    public List<NodeBreakpointEntry> NodeBreakpoints { get; set; } = new();
    public List<DataBreakpointEntry> DataBreakpoints { get; set; } = new();
    public List<WatchEntry> Watches { get; set; } = new();
}

public sealed class NodeBreakpointEntry
{
    public Guid AssetId { get; set; }
    public Guid GraphId { get; set; }
    public Guid NodeId { get; set; }  // authored node id
    public bool Enabled { get; set; } = true;
}

// DataBreakpointEntry stores the DBM's breakpoint fields needed for restore.
// Condition is the polymorphic SearchPredicateDto — already serializable
// via [JsonPolymorphic] + [JsonDerivedType] attributes.
public sealed class DataBreakpointEntry
{
    public SearchPredicateDto? Condition { get; set; }
    public string DisplayName { get; set; } = "";
    public Guid? SourceElementId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsWatch { get; set; }
    // FilterEntity (Entity?) is NOT persisted — entity references are runtime-only.
}

// Extends the existing WatchEntry concept with full identity for restore.
public sealed class WatchEntry
{
    public Guid AssetId { get; set; }
    public Guid GraphId { get; set; }
    public Guid PinId { get; set; }
    public string DisplayName { get; set; } = "";
    public string ExpectedTypeName { get; set; } = ""; // AssemblyQualifiedName for Type.GetType()
}
```

**Save method:**
```csharp
public static void Save(
    IReadOnlyList<Hrot.Blueprints.Core.Debug.Breakpoint> nodeBreakpoints,
    IReadOnlyList<Hrot.Blueprints.Core.Debug.Watch> watches,
    IReadOnlyList<Breakpoint> dbmBreakpoints,
    string path)
```

- Collect node breakpoints → `NodeBreakpointEntry` list (AssetId, GraphId, NodeId, Enabled)
- Collect watches → `WatchEntry` list (AssetId, GraphId, PinId, DisplayName, `ExpectedType.AssemblyQualifiedName`)
- Collect DBM breakpoints → `DataBreakpointEntry` list for those where `Condition is not ExternalHitTagPredicateDto` (filter out session-forwarded node breakpoints; they're already saved as NodeBreakpointEntry)
- Serialize to JSON with `WriteIndented = true, IncludeFields = true`
- Write to file

**Load method:**
```csharp
public static DebugSessionFile? TryLoad(string path)
```
- If file doesn't exist → return null
- Deserialize → return `DebugSessionFile`
- On any error → return null (don't throw)

**Location:** Save this alongside existing `WatchPersistence.cs` in the same namespace `Hrot.Diagnostics.Breakpoints`. The existing `WatchPersistence` should be marked `[Obsolete]` with a comment pointing to `DebugSessionPersistence`. Do NOT delete it yet (it may have other callers).

### Task 2: Expose save/restore integration points on BlueprintDebugSession

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Add methods to support persistence without exposing internal state:

1. **`GetSerializableBreakpoints()`** — returns the session breakpoints in a form suitable for save:
   ```csharp
   // Already exists: GetBreakpoints() returns IReadOnlyList<Breakpoint>
   // Breakpoint record has: Id, AssetId, GraphId, NodeId (authored), HitCount, Enabled, ...
   // We just call GetBreakpoints() — the persistence layer maps to NodeBreakpointEntry.
   ```

   No new method needed — `GetBreakpoints()` already returns what we need.

2. **`RestoreBreakpoints(IReadOnlyList<NodeBreakpointEntry> entries)`** — restores node breakpoints from persisted data:
   ```csharp
   public void RestoreNodeBreakpoints(IReadOnlyList</* NodeBreakpointEntry equivalent */> entries)
   {
       foreach (var e in entries)
       {
           SetBreakpoint(e.AssetId, e.GraphId, e.NodeId);
           // If not enabled, disable after setting
           if (!e.Enabled)
           {
               var bps = GetBreakpoints();
               var bp = bps.Last(); // the one we just set
               var updated = bp with { Enabled = false };
               // ... update internal state ...
           }
       }
   }
   ```

   Actually, `Breakpoint` record in the session doesn't expose `Enabled` as settable after construction. Let me check... `Breakpoint` has `Enabled` as a required constructor parameter. The record is immutable with `{ get; init; }`. So we can create a disabled breakpoint or use `with { Enabled = false }`.

   But `SetBreakpoint` always creates enabled breakpoints. We need a way to set disabled ones. Options:
   - Add optional `enabled` parameter to `SetBreakpoint`: `SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId, bool enabled = true)`
   - Add a separate method to toggle enabled state
   
   **Simpler approach:** Add `enabled` parameter to `SetBreakpoint`:
   ```csharp
   public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId, bool enabled = true)
   ```
   When `enabled = false`, don't forward to `_dataBreakpointManager` (disabled breakpoints should not trigger pause). Adjust the internal state accordingly.

3. **`RestoreWatches(IReadOnlyList<WatchEntry> entries)`** — restores watches from persisted data:
   ```csharp
   public void RestoreWatches(IReadOnlyList</* WatchEntry */> entries)
   {
       foreach (var e in entries)
       {
           var expectedType = Type.GetType(e.ExpectedTypeName);
           if (expectedType == null) continue; // type not available → skip
           AddWatch(e.AssetId, e.GraphId, e.PinId, e.DisplayName, expectedType);
       }
   }
   ```

### Task 3: Wire save/restore in EditorSubsystem

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

**Save path:** `<repo-root>/.debug/bpsession.json`

The repo root can be resolved the same way as in `EditorSubsystem` initialization:
```csharp
// Find repo root by walking up from BaseDirectory looking for IOS-IG-SimHost.sln
private string? ResolveRepoRoot()
{
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}
```

Then session path = `Path.Combine(repoRoot, ".debug", "bpsession.json")`.

**Save trigger — debounced on change:**
- Subscribe to `_blueprintDebugSession.OnBreakpointListChanged` and `OnSessionStateChanged`
- Debounce 500ms: each event resets a timer; when timer fires, save
- Use `System.Timers.Timer` or a simple approach with `Task.Delay`

Simplest approach (add to EditorSubsystem):
```csharp
private void ScheduleDebugSessionSave()
{
    _debugSessionSaveCts?.Cancel();
    _debugSessionSaveCts = new CancellationTokenSource();
    var token = _debugSessionSaveCts.Token;
    Task.Delay(500, token).ContinueWith(_ =>
    {
        if (!token.IsCancellationRequested)
            SaveDebugSession();
    }, TaskScheduler.Default);
}

private void SaveDebugSession()
{
    var path = GetDebugSessionPath();
    if (path == null) return;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    
    var nodeBps = _blueprintDebugSession?.GetBreakpoints();
    var watches = _blueprintDebugSession?.GetWatches();
    var dbmBps = _bpManager?.AllBreakpoints;
    
    if (nodeBps == null && watches == null && dbmBps == null) return;
    
    DebugSessionPersistence.Save(
        nodeBps ?? Array.Empty<...>(),
        watches ?? Array.Empty<...>(),
        dbmBps ?? Array.Empty<...>(),
        path);
}
```

**Save trigger — on close:**
- In the editor shutdown path, call `SaveDebugSession()` directly
- Find the shutdown method (e.g., `Shutdown()`, `Dispose()`, or the `_host.Closed` event handler)

Note: `Hrot.Diagnostics.Breakpoints.Breakpoint` (DBM type) vs `Hrot.Blueprints.Core.Debug.Breakpoint` (session type) — these are different types with the same name. Use fully-qualified names or aliases to disambiguate.

**Restore on startup:**
- After creating `_blueprintDebugSession` and wiring the CF-7-rev callback, load the session file:
```csharp
var sessionPath = GetDebugSessionPath();
if (sessionPath != null)
{
    var file = DebugSessionPersistence.TryLoad(sessionPath);
    if (file != null)
    {
        // Restore data breakpoints first (into DBM)
        if (file.DataBreakpoints.Count > 0 && _bpManager != null)
        {
            foreach (var entry in file.DataBreakpoints)
            {
                if (entry.Condition != null)
                {
                    _bpManager.AddBreakpoint(
                        entry.Condition,
                        displayName: entry.DisplayName,
                        sourceElementId: entry.SourceElementId);
                    // Note: IsWatch flag needs to be set after AddBreakpoint
                    // (AddBreakpoint doesn't accept IsWatch parameter)
                }
            }
        }
        
        // Restore node breakpoints (triggers CF-7-rev instrumentation)
        if (file.NodeBreakpoints.Count > 0 && _blueprintDebugSession != null)
        {
            // Need a method on session to restore (see Task 2)
            _blueprintDebugSession.RestoreNodeBreakpoints(file.NodeBreakpoints);
        }
        
        // Restore watches (triggers CF-7-rev Trace instrumentation)
        if (file.Watches.Count > 0 && _blueprintDebugSession != null)
        {
            _blueprintDebugSession.RestoreWatches(file.Watches);
        }
    }
}
```

**Important:** The restore must happen AFTER the CF-7-rev callback is wired, so that restoring breakpoints triggers instrumentation.

### Task 4: Handle Enabled flag in breakpoint restore

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Extend `SetBreakpoint` to accept an optional `enabled` parameter:

```csharp
public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId, bool enabled = true)
```

When `enabled == false`:
- Still create the breakpoint record with `Enabled = false`
- Still add to `_bpByNodeString` lookup (so the marker can be drawn)
- Do NOT forward to `_dataBreakpointManager` (disabled breakpoints should not fire)
- Do NOT fire the instrumentation callback (a disabled breakpoint shouldn't trigger a reload — wait, actually it SHOULD trigger instrumentation so it becomes active when enabled. Hmm.)

Actually, for restore: a disabled breakpoint was set by the user, then disabled. On restore, we want:
- The marker shown (greyed out)
- No pause on hit
- But the DebugMap must exist (so the marker can be placed on the right node)

If we don't trigger instrumentation for disabled breakpoints, the DebugMap won't exist and the marker won't show. Better to trigger instrumentation anyway — it's cheap (just QuickReload), and the breakpoint will be ready when the user enables it.

So: always trigger instrumentation (same as enabled case), just create the breakpoint with `Enabled = false` and skip the DBM forwarding.

### Task 5: Add `IsWatch` support to DataBreakpointEntry restore

The `AddBreakpoint` method on DBM doesn't set `IsWatch`. After adding a breakpoint, if `IsWatch` is true, we need to update it. The DBM's `Breakpoint` record is immutable (`record` with `{ get; init; }`), so we can't directly set `IsWatch` after creation.

Check if the DBM has a method to update breakpoint properties (like `SetEnabled`). If it has `SetEnabled`, it might have something similar for `IsWatch`. If not, we may need to expose a way to set `IsWatch` on the DBM.

**Check:** `DataBreakpointManager` has `SetEnabled(BreakpointId id, bool enabled)` at line 310. There is no `SetIsWatch`. For watches restored from the session file, the `IsWatch` flag must be set. 

Options:
- Add `SetIsWatch(BreakpointId id, bool isWatch)` to DBM (minimal change)
- Or filter watches to only those with `IsWatch == true` and use a different path

For the watch entries in the session file, they're already separated from data breakpoints (they're in the `Watches` list). The `IsWatch` flag on data breakpoints is for DBM breakpoints that also function as watches. On restore, if a data breakpoint entry has `IsWatch = true`, set the flag.

**Solution:** Add `SetIsWatch(BreakpointId id, bool isWatch)` to `DataBreakpointManager`:
```csharp
public void SetIsWatch(BreakpointId id, bool isWatch)
{
    if (_breakpoints.TryGetValue(id, out var bp))
        _breakpoints[id] = bp with { IsWatch = isWatch };
}
```

### Task 6: Stale handling for missing nodes on restore

When restoring node breakpoints, a saved node might no longer exist in the current graph (user deleted it). The `SetBreakpoint` call will create a breakpoint with a `NodeId` that has no corresponding entry in any DebugMap. When a DebugMap eventually registers (via manual Compile or CF-7-rev), the `ReResolveBreakpointsForAsset` method in `RegisterDebugMap` will try to resolve the authored node id through `BreakpointTargets`. If the node doesn't exist, it won't find a match and will skip it (the `continue` for "authored node is not in BreakpointTargets → leave as-is").

For a truly missing node (deleted from the graph), the breakpoint will never resolve. It should be marked `IsStale = true` so the UI shows it as disabled with a warning. 

In `RegisterDebugMap` → `ReResolveBreakpointsForAsset`: when an authored node ID is NOT found in `BreakpointTargets`, the breakpoint should be marked stale instead of being left as-is:

```csharp
if (index.BreakpointTargets.TryGetValue(authoredNodeId, out var blockProbeId))
    newProbeId = blockProbeId.ToString("D");
else
{
    // Node no longer exists — mark stale.
    var stale = bp with { IsStale = true };
    _breakpoints[bp.Id] = stale;
    ReplaceInBpList(oldProbeId, bp, stale);
    continue;
}
```

This needs to be added to the `ReResolveBreakpointsForAsset` method (Task 3 from CF-7-rev). Update it to handle the "node not found" case.

### Task 7: Handle `AddBreakpoint` IsWatch for DBM restore

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` (UPDATE)

Add `SetIsWatch` method (see Task 5). This is a small addition.

After restoring a data breakpoint that has `IsWatch = true`:
```csharp
var bpId = _bpManager.AddBreakpoint(entry.Condition, displayName: entry.DisplayName, 
                                     sourceElementId: entry.SourceElementId);
if (entry.IsWatch)
    _bpManager.SetIsWatch(bpId, true);
```

---

## 🧪 Testing Requirements

**Minimum 8 tests in a new file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF8_SessionPersistenceTests.cs`

### Test 1: Round-trip — node breakpoints only
- Create session with callback
- Set 2 breakpoints on different nodes
- Save using `DebugSessionPersistence.Save(...)` to a temp file
- Load via `TryLoad`
- Assert: 2 node breakpoints with correct AssetId, GraphId, NodeId, Enabled

### Test 2: Round-trip — data breakpoint with condition
- Create DBM breakpoint with `BlueprintVariablePredicateDto`
- Save + load
- Assert: condition round-trips with correct variable name/type

### Test 3: Round-trip — watches
- Create watch on a pin
- Save + load
- Assert: AssetId, GraphId, PinId, DisplayName, ExpectedTypeName all match

### Test 4: Serialization excludes ExternalHitTagPredicateDto
- Create DBM breakpoint with standalone `ExternalHitTagPredicateDto` (simulating a session-forwarded node breakpoint)
- Save → load
- Assert: this breakpoint is NOT in the `DataBreakpoints` list (it's filtered out)

### Test 5: Save file is valid JSON, matches schema
- Save a full session (node bp + data bp + watch)
- Parse the JSON file
- Assert: file contains NodeBreakpoints, DataBreakpoints, Watches arrays
- Assert: each entry has the required fields

### Test 6: TryLoad returns null for missing file
- Call `TryLoad` with non-existent path
- Assert: returns null (not exception)

### Test 7: TryLoad returns null for malformed file
- Write garbage JSON to temp file
- Assert: returns null (not exception)

### Test 8: Restore → CF-7-rev integration
- Save a session with a node breakpoint for Count4's Delay node
- Load the file
- Call `RestoreNodeBreakpoints`
- Verify: CF-7-rev callback was invoked (instrumentation triggered)
- Verify: breakpoint is in the session's `GetBreakpoints()`

---

## 🎯 Success Criteria

- [ ] Build 0 errors
- [ ] `Hrot.Blueprints.Tests` → 7 pre-existing, 0 new
- [ ] All 8 CF8 tests pass
- [ ] `DebugSessionPersistence` generalizes `WatchPersistence` (save ALL breakpoints, not just watches)
- [ ] Save triggers on breakpoint/watch change (debounced) + on close
- [ ] Restore triggers CF-7-rev instrumentation for each restored asset
- [ ] `ExternalHitTagPredicateDto`-only DBM breakpoints excluded from save
- [ ] Disabled breakpoint restore: breakpoint entered as disabled, not forwarded to DBM
- [ ] Missing node on restore → stale (via `ReResolveBreakpointsForAsset` update)
- [ ] `SetIsWatch` added to DBM

---

## ⚠️ Common Pitfalls

- **Type ambiguity:** `Hrot.Diagnostics.Breakpoints.Breakpoint` (DBM) vs `Hrot.Blueprints.Core.Debug.Breakpoint` (session) — two different `Breakpoint` types. Use fully-qualified names.
- **`Watch.ExpectedType` is `System.Type`** — not serializable. Store `ExpectedType.AssemblyQualifiedName` as string.
- **`Entity? FilterEntity`** — not persisted. Entity references are runtime-only.
- **`ExternalHitTagPredicateDto` filtering** — DBM breakpoints created by `session.SetBreakpoint` forwarding must NOT be saved as data breakpoints (they're recreated on node breakpoint restore).
- **Save during CF-7-rev callback** — debounce ensures the save doesn't fire during QuickReload's rapid DebugMap registration/re-resolution sequence.
- **`BPCompilerMode` alias** — already defined in `BlueprintDebugSession.cs` as `using BPCompilerMode = Hrot.Blueprints.Core.Compiler.CompilerMode;`

---

## 📚 Reference Materials
- **Existing WatchPersistence:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/WatchPersistence.cs`
- **DBM Breakpoint type:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointTypes.cs`
- **Session Breakpoint type:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs:58-76`
- **SearchPredicateDto:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`
- **CF-7-rev tests (pattern):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs`
- **Design Addendum §7:** `.dev/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md`
