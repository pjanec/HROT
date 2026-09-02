# BATCH-CF7rev: Auto In-Memory Instrumentation on Demand

**Batch Number:** BATCH-CF7rev  
**Tasks:** CF-7-rev (Auto in-memory instrumentation on demand)  
**Phase:** Corrective Features (CF)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** CF-4 (exec-only, block-granular breakpoints with BreakpointTargets)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Implement auto-instrumentation: when the first breakpoint or watch is set on an asset (or a session is restored), the editor transparently does an in-memory Debug/Trace Quick Reload so probes fire without the user manually clicking Compile. The MSBuild generator stays Release; production artifacts are untouched.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Design Addendum:** `.dev/_DONE/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md` — §4 (Instrumentation model), §2 (Node identity), §5 (Storage & lifecycle)
3. **Task Detail:** `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md` — Batch CF-7-rev section
4. **Architect Answers:** See §"Architect decisions (verified)" below — all 4 answers are RESOLVED.

### Source Code Location
- **Primary Work Area:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
- **Editor Wiring:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- **Asset Model:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` (AssetMetadata)
- **Quick Reload:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`
- **Test Project:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`

### Report Submission
**When done, submit your report to:** `.dev/_DONE/blueprint-dbg-1/reports/BATCH-CF7rev-REPORT.md`

### Zoo Operating Rules
- **Do NOT** delete, skip, weaken, or change the assertions of any existing test to make the suite pass.
- **Do NOT** set `BLUEPRINT_REGENERATE_SNAPSHOTS` or regenerate golden snapshots.
- The report must include the **full failing-test set by name** before and after.
- Editor must be CLOSED during build (DLL locks).
- Gate: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors.
- `Hrot.Blueprints.Tests` → **7 pre-existing failures, 0 new**.
- `Hrot.Editor.AiShared.Tests`; `EditorSubsystemBoot` 10/10.

---

## Architect Decisions (VERIFIED — do not deviate)

### Q1: Callback placement → `Func<Guid, CompilerMode, Task>?` on BlueprintDebugSession
The session gets a `Func<Guid, CompilerMode, Task>?` callback. `EditorSubsystem` wires it. The session invokes it fire-and-forget from `SetBreakpoint`/`AddWatch` when no DebugMap exists for the asset. **DO NOT** make the session depend on `QuickReloadService` or `IBlueprintCompiler` — it stays decoupled.

### Q2: RegisterDebugMap MUST re-resolve tentative breakpoints
When `RegisterDebugMap` is called, after storing the map it must iterate all breakpoints for that AssetId and re-resolve their `ProbeNodeId` from `BreakpointTargets`. Currently tentative breakpoints have `ProbeNodeId = NodeId` (fallback). After the map arrives, this must be updated to the block-probe id. Without this, auto-instrumentation is useless — the breakpoint would never match the runtime probe.

### Q3: One project-scoped file → `.debug/bpsession.json` (CF-8, but designed here)
File location is settled. For CF-7-rev this is relevant only for the restore path (when CF-8 lands). The callback wire-up should be designed to also serve the CF-8 restore flow.

### Q4: SetBreakpoint MUST pass sourceElementId to DataBreakpointManager
When forwarding to `_dataBreakpointManager.AddBreakpoint(...)`, pass `sourceElementId: nodeId` (the authored node GUID). Currently omitted. This is needed for CF-8 restore to work. Fix it in this batch.

---

## 🎯 Batch Objectives

1. Add `OnInstrumentationRequested` callback to `BlueprintDebugSession`  
2. Invoke callback from `SetBreakpoint` and `AddWatch` when asset has no DebugMap  
3. Re-resolve tentative breakpoints' `ProbeNodeId` in `RegisterDebugMap`  
4. Wire callback in `EditorSubsystem` to trigger QuickReload with the right `CompilerMode`  
5. Fix `SetBreakpoint` to pass `sourceElementId` to `DataBreakpointManager`

---

## ✅ Tasks

### Task 1: Add instrumentation callback to BlueprintDebugSession

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

**Description:** Add a `Func<Guid, CompilerMode, Task>?` callback field and a setter method.

**Requirements:**
1. Add private field: `private Func<Guid, CompilerMode, Task>? _onInstrumentationRequested;`
2. Add public setter:
   ```csharp
   public void SetInstrumentationCallback(Func<Guid, CompilerMode, Task>? callback)
   {
       _onInstrumentationRequested = callback;
   }
   ```
   Do NOT add this to `IBlueprintDebugSession` — it's an implementation detail of the concrete session. Tests can set it directly on `BlueprintDebugSession` or via a cast.

### Task 2: Invoke callback from SetBreakpoint and AddWatch

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

**Description:** When a breakpoint or watch is set on an asset that has no DebugMap registered, fire-and-forget the instrumentation callback.

**Requirements:**

1. **In `SetBreakpoint`:** Near the top, before the breakpoint record is created:
   ```csharp
   // Auto-instrumentation: if no DebugMap yet for this asset, request a Debug compile.
   if (!_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
   {
       _ = _onInstrumentationRequested.Invoke(assetId, CompilerMode.Debug);
   }
   ```
   Note: `CompilerMode.Debug` is in namespace `Hrot.Blueprints.Core.Compiler`. Add the using if needed.

2. **In `AddWatch`:** Same pattern, but request `CompilerMode.Trace`:
   ```csharp
   if (!_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
   {
       _ = _onInstrumentationRequested.Invoke(assetId, CompilerMode.Trace);
   }
   ```
   Use `_ = ...` to fire-and-forget (discard the Task). The callback implementation handles errors internally.

3. **Mode selection logic:** If the asset already has a DebugMap (already instrumented in Debug mode) and a watch is added, the callback will request Trace. The instrumentation service (EditorSubsystem) handles upgrading the mode. If a breakpoint is added to an asset already in Trace mode, the callback requests Debug but the service should NOT downgrade from Trace to Debug — but the session doesn't worry about this; it just fires the request. The EditorSubsystem callback implementation decides whether a reload is actually needed.

### Task 3: Re-resolve breakpoints in RegisterDebugMap

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

**Description:** After storing the new DebugMap, re-resolve all breakpoints for that asset through `BreakpointTargets`, updating their `ProbeNodeId` and the `_bpByNodeString` lookup.

**Requirements:**

1. Extract a helper method: `private void ReResolveBreakpointsForAsset(Guid assetId, DebugMapIndex index)`

2. The method logic:
   ```csharp
   private void ReResolveBreakpointsForAsset(Guid assetId, DebugMapIndex index)
   {
       var assetBps = _breakpoints.Values
           .Where(b => b.AssetId == assetId)
           .ToList();
       
       foreach (var bp in assetBps)
       {
           // Parse the authored node id from bp.NodeId
           if (!Guid.TryParse(bp.NodeId, out var authoredNodeId))
               continue;
           
           // Determine the correct probe id
           string newProbeId;
           if (index.BreakpointTargets.TryGetValue(authoredNodeId, out var blockProbeId))
               newProbeId = blockProbeId.ToString("D");
           else
               continue; // authored node is not in BreakpointTargets → leave as-is
           
           string oldProbeId = string.IsNullOrEmpty(bp.ProbeNodeId) ? bp.NodeId : bp.ProbeNodeId;
           
           // No change needed
           if (oldProbeId == newProbeId)
               continue;
           
           // Remove from old probe-keyed lookup
           if (_bpByNodeString.TryGetValue(oldProbeId, out var list))
           {
               list.Remove(bp);
               if (list.Count == 0)
                   _bpByNodeString.Remove(oldProbeId);
           }
           
           // Update the breakpoint record
           var updated = bp with { ProbeNodeId = newProbeId, IsStale = false };
           _breakpoints[bp.Id] = updated;
           
           // Add to new probe-keyed lookup
           if (!_bpByNodeString.TryGetValue(newProbeId, out var newList))
               _bpByNodeString[newProbeId] = newList = new List<Breakpoint>();
           newList.Add(updated);
           
           // Re-forward to DataBreakpointManager with the correct probe id
           if (_dataBreakpointManager != null && _mgrBpIds.TryGetValue(bp.Id, out var mgrId))
           {
               _dataBreakpointManager.Remove(mgrId);
               _mgrBpIds.Remove(bp.Id);
               
               var newMgrId = _dataBreakpointManager.AddBreakpoint(
                   new ExternalHitTagPredicateDto { Tag = newProbeId },
                   displayName: $"Blueprint node {bp.NodeId}",
                   sourceElementId: authoredNodeId);
               _mgrBpIds[updated.Id] = newMgrId;
           }
       }
   }
   ```

3. Call `ReResolveBreakpointsForAsset(map.AssetId, index)` at the end of `RegisterDebugMap`, AFTER `_debugMaps[map.AssetId] = index;` and AFTER the existing structure-hash-staleness check.

4. **Important:** The re-resolution must happen even when there was NO previous DebugMap (first registration). The existing staleness check only runs when there's an existing map with different hash. The re-resolution should always run for the newly-registered asset.

### Task 4: Fix SetBreakpoint to pass sourceElementId to DataBreakpointManager

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

**Description:** Update the `_dataBreakpointManager.AddBreakpoint(...)` call in `SetBreakpoint` to pass `sourceElementId: nodeId`.

**Requirements:**
Change this (currently ~line 267-272):
```csharp
var mgrId = _dataBreakpointManager.AddBreakpoint(
    new ExternalHitTagPredicateDto { Tag = probeIdStr },
    displayName: $"Blueprint node {nodeIdStr}");
```
To:
```csharp
var mgrId = _dataBreakpointManager.AddBreakpoint(
    new ExternalHitTagPredicateDto { Tag = probeIdStr },
    displayName: $"Blueprint node {nodeIdStr}",
    sourceElementId: nodeId);  // authored node GUID — needed for CF-8 persistence
```

### Task 5: Wire callback in EditorSubsystem

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

**Description:** After creating `BlueprintDebugSession`, wire the instrumentation callback that resolves the asset by ID and triggers QuickReload.

**Requirements:**

1. **Store `QuickReloadService` as a field** (or capture it in a closure). Currently it's a local variable at ~line 2484. You need it to be accessible from the callback. Options:
   - Store as a field: `private QuickReloadService? _blueprintQuickReloadService;`
   - Or capture the catalog and reload service in the callback closure

2. **Also store the `IAssetCatalog`** (`qrsCatalog` at ~line 2470) — needed to resolve asset by ID. It's of type `FileSystemAssetCatalog` but store as `IAssetCatalog`.

3. **Create and wire the callback.** After creating `_blueprintDebugSession` and `quickReloadService`:
   ```csharp
   _blueprintDebugSession.SetInstrumentationCallback(async (assetId, mode) =>
   {
       try
       {
           // Find the asset file by ID
           string? filePath = null;
           foreach (var entry in _blueprintAssetCatalog.EnumerateAll())
           {
               if (entry.AssetId == assetId)
               {
                   filePath = entry.FilePath;
                   break;
               }
           }
           
           if (filePath == null)
           {
               _hotReloadSource?.LogWarning(
                   $"Auto-instrumentation: asset {assetId} not found in catalog.");
               return;
           }
           
           // Load the asset
           var json = File.ReadAllText(filePath);
           var options = new JsonSerializerOptions
           {
               IncludeFields = true,
               PropertyNameCaseInsensitive = true,
           };
           var asset = JsonSerializer.Deserialize<BlueprintAsset>(json, options);
           if (asset == null) return;
           
           // Set the compiler mode
           asset.EditorMetadata.CompilerMode = mode;
           
           // Trigger Quick Reload
           await _blueprintQuickReloadService.TriggerAsync(asset);
       }
       catch (Exception ex)
       {
           _hotReloadSource?.LogError(
               $"Auto-instrumentation failed for asset {assetId}: {ex.Message}");
       }
   });
   ```

4. **Add necessary usings:** `System.Text.Json`, `Hrot.Blueprints.Core.Assets`, `Hrot.Blueprints.Core.Compiler`, `Hrot.Blueprints.Editor.Reload`.

5. **Store the QuickReloadService and catalog as fields** on EditorSubsystem:
   ```csharp
   private QuickReloadService? _blueprintQuickReloadService;
   private IAssetCatalog? _blueprintAssetCatalog;
   ```

6. Assign them where they're created (around lines 2470-2490):
   ```csharp
   _blueprintAssetCatalog = qrsCatalog;
   // ... create quickReloadService ...
   _blueprintQuickReloadService = quickReloadService;
   ```

### Task 6: Add `.debug/` to `.gitignore` (preparation for CF-8)

**File:** `.gitignore` (UPDATE)

**Description:** Add the `.debug/` directory entry so the future session file is not committed.

**Add this line** at the end of `.gitignore`:
```
# Blueprint debug session files (user-local, not committed)
.debug/
```

---

## 🧪 Testing Requirements

**Minimum 8 tests across 2 test files.** Tests must verify ACTUAL BEHAVIOR, not just compilation.

### Test File 1: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_InstrumentationTests.cs`

These tests work with the real `BlueprintDebugSession` (no EditorSubsystem needed):

1. **`SetBreakpoint_NoDebugMap_InvokesCallback_WithDebugMode`**  
   Create session, set callback that captures (assetId, mode). Call `SetBreakpoint(testAssetId, graphId, nodeId)`. Assert callback was invoked with `(testAssetId, CompilerMode.Debug)`.

2. **`SetBreakpoint_HasDebugMap_DoesNotInvokeCallback`**  
   Register a (minimal) DebugMap for the asset. Call `SetBreakpoint`. Assert callback NOT invoked.

3. **`AddWatch_NoDebugMap_InvokesCallback_WithTraceMode`**  
   Create session, set callback. Call `AddWatch(...)`. Assert callback invoked with `CompilerMode.Trace`.

4. **`RegisterDebugMap_ReResolves_TentativeProbeNodeId`**  
   - Create session WITHOUT callback (or callback that does nothing).  
   - Call `SetBreakpoint(assetId, graphId, authoredNodeId)` — breakpoint gets `ProbeNodeId = authoredNodeId.ToString("D")` (fallback).  
   - Build a DebugMap with a DebugMapIndex that has `BreakpointTargets[authoredNodeId] = differentBlockProbeId`.  
   - Call `RegisterDebugMap(map)`.  
   - Assert that after registration, `GetBreakpoints().Single().ProbeNodeId == differentBlockProbeId.ToString("D")`.  
   - Assert that the `_bpByNodeString` lookup (tested indirectly via `OnNodeEnter`) fires for `differentBlockProbeId`, not the authored id.

5. **`RegisterDebugMap_ReResolves_UpdatesDataBreakpointManager`**  
   Same setup but also set a `DataBreakpointManager` on the session (use a real or mock manager). Verify that after re-resolution, the manager breakpoint's `ExternalHitTagPredicateDto.Tag` matches the new `ProbeNodeId`.

### Test File 2: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs`

These tests verify the full flow with the compiler (use existing test infrastructure from `CF2_AuthoredIdProbeTests` / `BreakpointTests`):

6. **`SetBreakpoint_TriggersAutoInstrument_ThenPauses`**  
   - Load `Count4.bp.json`, compile it in Release mode (no probes).  
   - Create a session with callback that re-compiles in Debug mode.  
   - Simulate: set breakpoint on Delay `0b561966` → callback fires → recompile in Debug → session.RegisterDebugMap → breakpoint re-resolved.  
   - Drive one tick → assert `PauseRequestCount >= 1`.  
   - This is the golden end-to-end test.

7. **`BreakpointSetBeforeCompile_BecomesActive_AfterMapRegisters`**  
   - Create a session with NO callback (simulating: callback hasn't fired yet).  
   - Set breakpoint on an authored node id.  
   - Verify `GetBreakpoints().Single().ProbeNodeId == authoredNodeId.ToString("D")` (tentative).  
   - Now register a DebugMap with `BreakpointTargets[authoredNodeId] = differentProbeId`.  
   - Verify `GetBreakpoints().Single().ProbeNodeId == differentProbeId.ToString("D")` (re-resolved).  
   - Verify `IsNodeBreakpointable(assetId, graphId, authoredNodeId) == true` (found in BreakpointTargets).

8. **`ModeSelection_DebugForBreakpoints_TraceForWatches`**  
   - Create session with callback that captures the mode.  
   - Call `SetBreakpoint` → assert captured mode = Debug.  
   - Call `AddWatch` (on same asset, no map yet) → assert captured mode = Trace.  
   - The second call should still invoke the callback even though Debug was already requested, because no map is registered yet.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- Tests must verify ACTUAL callback invocation with correct parameters.
- Tests must verify ProbeNodeId changes after RegisterDebugMap — not just that the method doesn't crash.
- End-to-end test must verify the breakpoint actually pauses the sim after auto-instrumentation.
- **NOT ACCEPTABLE:** Tests that only check "callback was set" or "method was called."

**❗ CODE QUALITY EXPECTATIONS**
- The `ReResolveBreakpointsForAsset` method must handle the DataBreakpointManager re-registration atomically (remove old, add new).
- The callback invocation must be fire-and-forget (do NOT block the calling thread on `SetBreakpoint`).
- All new code must follow the existing naming and comment conventions of `BlueprintDebugSession.cs`.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors
- [ ] `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **7 pre-existing failures, 0 new**
- [ ] All 8 tests pass
- [ ] Callback is invoked from `SetBreakpoint`/`AddWatch` when no DebugMap exists
- [ ] `RegisterDebugMap` re-resolves tentative breakpoints' `ProbeNodeId`
- [ ] `SetBreakpoint` passes `sourceElementId` to `DataBreakpointManager`
- [ ] `.debug/` added to `.gitignore`
- [ ] Production/Release build path unchanged (generator still hardcodes Release — verify by grep)
- [ ] Report submitted with full failure-set diff

---

## 📝 Implementation Notes

### The `ReplaceInBpList` helper
`BlueprintDebugSession` has a private helper `ReplaceInBpList(string probeId, Breakpoint old, Breakpoint replacement)` — use it when updating breakpoints in `_bpByNodeString` lists. If it doesn't exist, check the current code. If moving between keys (which `ReResolveBreakpointsForAsset` does), you'll need to remove from old key and add to new key manually.

### Testing with DebugMapIndex
`DebugMapIndex` constructor takes a `DebugMap`. To create a synthetic one for tests:
```csharp
var map = new DebugMap
{
    AssetId = testAssetId,
    AssetName = "Test",
    Entries = new List<DebugMapEntry>(),
    BreakpointTargets = new Dictionary<Guid, Guid>
    {
        { authoredNodeId, blockProbeId }
    },
    StateLayout = new StateLayout(),
};
var index = new DebugMapIndex(map);
```
Then `session.RegisterDebugMap(map)` — note: `RegisterDebugMap` takes `DebugMap`, not `DebugMapIndex`. It creates the index internally.

### Full existing failure set (7 pre-existing)
- `AiPrimitiveEmitGolden_MoveToAndFire_GeneratedSource_Snapshot`
- `AiPrimitiveEmitGolden_HasVisibleTarget_GeneratedSource_Snapshot`
- `LibraryEmitGolden_GeneratedSource_Snapshot`
- `LibraryMathEmitGolden_MoveToAndFire_GeneratedSource_Snapshot`
- `ConditionSummaryAttachmentTests.EqsResult`
- `AllocationFreeTests` (flaky-adjacent)
- (and one more — confirm by running the suite before starting)

---

## 📚 Reference Materials
- **Task Detail:** `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md` — Batch CF-7-rev section
- **Design Addendum:** `.dev/_DONE/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md` — §4 (Instrumentation model)
- **Debug Session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
- **Quick Reload:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs`
- **Existing Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs`, `CF2_AuthoredIdProbeTests.cs`, `BlueprintDebugSessionLifecycleTests.cs`
- **Task Tracker:** `.dev/_DONE/blueprint-dbg-1/TASK-TRACKER.md`
