# BATCH-CF6: Real Stepping via Temporary Breakpoints

**Batch Number:** BATCH-CF6  
**Tasks:** CF-6 (Real stepping)  
**Phase:** Corrective Features (CF)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** CF-4 (BreakpointTargets), CF-5 (step buttons)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Implement real stepping: when the user clicks Step Over/Into/Out, compute the next exec successor(s) from the graph, set invisible one-shot temporary breakpoints on them, suppress user breakpoints during the step pass, resume (not single-tick), and on hit pause + clear temporaries. Replace the current `_stepMode` tick-matching pseudo-step.

### Required Reading
1. **Design Addendum:** `.dev/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md` — §6 (Stepping)
2. **Task Detail:** `.dev/blueprint-dbg-1/TASK-DETAIL.md` — Batch CF-6 section
3. **Current step code:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — StepOver/Into/Out + OnNodeEnter step matching
4. **Graph model:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` — Graph, Node, Pin, Link
5. **Compiler exec traversal (reference):** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` — GetSingleExecSuccessor, GetWhenExecSuccessor, GetBranchSuccessors

### Source Code Location
- **Session:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
- **Graph traverser:** New utility or extension method
- **Tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/`

### Report Submission
`.dev/blueprint-dbg-1/reports/BATCH-CF6-REPORT.md`

### Zoo Operating Rules
- Do NOT delete, skip, or weaken existing test assertions
- Do NOT regenerate golden snapshots
- Report full failing-test set by name before and after
- Editor CLOSED during build
- Gate: build 0 errors, Blueprints 7/0-new

---

## 🎯 Batch Objectives

1. Create `ExecSuccessors` utility — compute next exec node IDs from a `Graph`
2. Register graph structure in session alongside DebugMap
3. Add temp breakpoint API (invisible, one-shot, auto-clear)
4. Rewrite Step methods to use temp BPs (not `_stepMode` tick-matching)
5. Suppress user BPs during step pass; restore on hit
6. Replace `_stepMode` in `OnNodeEnter` with temp BP mechanism

---

## Design

### Step model (from addendum §6)

1. When paused at node X, compute X's **immediate exec successor(s)** by following exec-output links in the graph
2. Set **invisible one-shot temporary breakpoints** on those successors (translated via `BreakpointTargets`)
3. **Suppress user breakpoints** for the step pass — only honor temps
4. **Resume** (not single-tick) — run until a temp target fires
5. On temp hit: **pause, clear all temps, restore user BPs**

Slice-1: all three step buttons converge to "next exec node" (cross-peer stepping out of scope).

### Why not `_stepMode` tick-matching?

The current approach (step one tick, re-match in `OnNodeEnter` on first probed node) doesn't advance the cursor to the next node — it re-pauses at the loop entry because the graph re-executes from entry each tick. Temp breakpoints on actual successors fix this.

---

## ✅ Tasks

### Task 1: Create ExecSuccessors utility

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/ExecSuccessors.cs` (NEW)

Compute next exec node IDs from a graph. Mirror the compiler's `GetSingleExecSuccessor` / `GetBranchSuccessors` pattern.

```csharp
public static class ExecSuccessors
{
    /// <summary>
    /// Returns all immediate exec successor node IDs for a given node.
    /// Follows all exec-output pins through links in the graph.
    /// Multi-successor nodes (Branch, When, Sequence) return multiple IDs.
    /// Terminal nodes (Return) return empty.
    /// </summary>
    public static IReadOnlyList<Guid> GetSuccessors(Graph graph, Guid nodeId)
    {
        // 1. Find the node in the graph
        // 2. Get all exec-output pins (p.IsExec && p.Direction == "Out")
        // 3. For each exec-output pin, find links (l.FromNodeId == nodeId && l.FromPinId == pinId)
        // 4. Return the list of ToNodeIds
    }
}
```

Note: `Pin.Direction` is stored as a string: "Out" for output, "In" for input (or similar). Check the actual values in `.bp.json` / `GraphTypes.cs`.

Reference: `Stage5_Schedule.cs` lines 1503-1511 (`GetSingleExecSuccessor`), 1513-1519+ (`GetBranchSuccessors`).

### Task 2: Register graph structure in session

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Add graph storage and registration:

```csharp
// Graph structure for stepping: graphId → Graph
private readonly Dictionary<Guid, Graph> _graphs = new();

public void RegisterGraph(Graph graph)
{
    _graphs[graph.Id] = graph;
}
```

~~Wire it from `EditorSubsystem` when a blueprint is opened.~~ Actually, `RegisterDebugMap` already gets called with the asset info. The graph can be registered alongside it.

Better: add an overload or companion method to `RegisterGraphForAsset(Guid assetId, Graph graph)`.

Actually simplest: `BlueprintDocumentFactory.Build` already has the `Graph`. Register it on the session at that point. Or register via `EditorSubsystem` when calling `Build`.

**Implementation:** In `EditorSubsystem`, find where `BlueprintDocumentFactory.Build` is called (or where the session receives the asset) and also call `session.RegisterGraph(graph)` for each graph.

Search for: `BlueprintDocumentFactory.Build` in EditorSubsystem — find the debugSession parameter being passed. The `bpAsset.Graphs` is available there. Register all graphs.

### Task 3: Add temporary breakpoint mechanism to session

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Temp breakpoints are one-shot, invisible breakpoints used for stepping:

```csharp
// Temporary breakpoints for stepping. Keyed by probe-id string.
// Cleared on hit or on Continue(). Not exposed via GetBreakpoints().
private readonly Dictionary<string, List<Breakpoint>> _tempBreakpoints = new(StringComparer.Ordinal);
```

**API:**

```csharp
/// <summary>
/// Sets one-shot temporary breakpoints for stepping. These are invisible
/// (not in GetBreakpoints), not forwarded to DBM, and auto-cleared on first hit.
/// Suppresses user breakpoints while temps are active.
/// </summary>
public void SetTemporaryBreakpoints(IEnumerable<BreakpointTarget> targets)
{
    ClearTemporaryBreakpoints();
    foreach (var t in targets)
    {
        // Translate authored node id → block-probe id via BreakpointTargets
        string probeId = ResolveProbeId(t.AssetId, t.NodeId);
        var bp = new Breakpoint(default, t.AssetId, t.GraphId, t.NodeId.ToString("D"), 0, true)
        {
            ProbeNodeId = probeId,
        };
        if (!_tempBreakpoints.TryGetValue(probeId, out var list))
            _tempBreakpoints[probeId] = list = new List<Breakpoint>();
        list.Add(bp);
    }
}

private string ResolveProbeId(Guid assetId, Guid authoredNodeId)
{
    if (_debugMaps.TryGetValue(assetId, out var idx) &&
        idx.BreakpointTargets.TryGetValue(authoredNodeId, out var blockProbeId))
        return blockProbeId.ToString("D");
    return authoredNodeId.ToString("D"); // fallback
}

private void ClearTemporaryBreakpoints()
{
    _tempBreakpoints.Clear();
}

public bool HasTemporaryBreakpoints => _tempBreakpoints.Count > 0;

/// <summary>
/// A step target: an authored node to set a temporary breakpoint on.
/// </summary>
public readonly record struct BreakpointTarget(Guid AssetId, Guid GraphId, Guid NodeId);
```

### Task 4: Rewrite OnNodeEnter to handle temp BPs + suppression

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Modify `OnNodeEnter` — after the exec history recording and BEFORE the user breakpoint check:

```csharp
// Check temporary breakpoints FIRST. When stepping, suppress user breakpoints.
if (_tempBreakpoints.Count > 0)
{
    if (_tempBreakpoints.TryGetValue(nodeId, out var tempList))
    {
        var tempBp = tempList[0]; // first matching temp
        if (!_isPaused)
        {
            ClearTemporaryBreakpoints(); // auto-clear ALL temps on first hit
            HandleBreakpointHit(self, tempBp, nodeId);
        }
    }
    // When temps are active, skip user breakpoint matching entirely.
    // Continue to step-mode check below (step-mode is being replaced but
    // keep for backward compat until fully removed).
}
else
{
    // Original user breakpoint matching code goes here (existing logic)
    if (_bpByNodeString.TryGetValue(nodeId, out var bpList)) { ... }
}

// Remove _stepMode matching — replaced by temp BP mechanism
// Keep the step-mode block for backward compat but it's dead code now
// (StepOver/Into/Out no longer set _stepMode)
```

**Important:** The user BP suppression only happens when temp breakpoints are active. When no temps exist, user BPs work normally. Temps are cleared on first hit (one-shot).

### Task 5: Rewrite Step methods to use temp BPs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

Replace the current StepOver/Into/Out implementation:

```csharp
public void StepOver() => Step();
public void StepInto() => Step();
public void StepOut() => Step();

/// <summary>
/// Slice-1 stepping: all three step commands converge to "step to next exec node."
/// Computes successors of the currently-paused node, sets temporary breakpoints
/// on them, suppresses user breakpoints, and resumes. On hitting a temp target,
/// pauses and auto-clears temps. Cross-peer-call stepping is deferred.
/// </summary>
private void Step()
{
    if (!_isPaused || _pausedAt == null)
        return;
    
    var pausedNodeId = _pausedAt.NodeId;
    var assetId = _pausedAt.AssetId;
    var graphId = _pausedAt.GraphId;
    
    // Find the graph structure
    if (!_graphs.TryGetValue(graphId, out var graph))
    {
        // No graph registered — fall back to single-tick step
        _timeController.RequestStepOneTick();
        return;
    }
    
    // Parse the authored node ID from the paused breakpoint
    if (!Guid.TryParse(pausedNodeId, out var authoredNodeId))
    {
        _timeController.RequestStepOneTick();
        return;
    }
    
    // Compute next exec successors
    var successors = ExecSuccessors.GetSuccessors(graph, authoredNodeId);
    if (successors.Count == 0)
    {
        // Terminal node — nothing to step to. Just resume.
        Continue();
        return;
    }
    
    // Set temporary breakpoints on all successors
    var targets = successors.Select(s => new BreakpointTarget(assetId, graphId, s));
    SetTemporaryBreakpoints(targets);
    
    // Resume (not single-tick) — temp BPs handle the pause
    _isPaused = false;
    _pausedAt = null;
    _pausedOnEntity = null;
    _stepMode = StepMode.None;
    _firedBreakpointsThisTick.Clear();
    _timeController.RequestResume();
    OnSessionStateChanged?.Invoke();
}
```

Also clear temps on `Continue()`:
```csharp
public void Continue()
{
    // ... existing code ...
    ClearTemporaryBreakpoints(); // discard any leftover temps
    // ... existing code ...
}
```

### Task 6: Register graphs from EditorSubsystem

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

In `BlueprintDocumentFactory.Build` call or wherever the session receives the asset, register the graphs:

Find where `Build` is called with `debugSession: _blueprintDebugSession`. After that call, register graphs:
```csharp
foreach (var graph in bpAsset.Graphs)
    _blueprintDebugSession?.RegisterGraph(graph);
```

Additionally, register graphs during restore or when DebugMap is registered (so graphs are available for stepping on restored breakpoints).

Better location: in `RegisterDebugMap` — when a debug map is registered, the graph is typically available in the editor. But the session doesn't know about graphs at that point.

Simplest: register graphs when opening a blueprint document. Find the `Build` call in EditorSubsystem and add graph registration.

---

## 🧪 Testing Requirements

**Minimum 6 tests** in: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF6_SteppingTests.cs`

### Test 1: ExecSuccessors — single successor (linear chain)
Build a simple 3-node linear graph (Entry→A→B with exec links). Verify `GetSuccessors(graph, Entry.Id)` returns [A.Id] and `GetSuccessors(graph, A.Id)` returns [B.Id].

### Test 2: ExecSuccessors — terminal node returns empty
Verify `GetSuccessors(graph, ReturnNode.Id)` returns empty list.

### Test 3: Temp breakpoints hit and auto-clear
Set temp breakpoint on a node, simulate `OnNodeEnter` with matching probe id, verify:
- `HandleBreakpointHit` was called (session pauses)
- Temps were cleared (`HasTemporaryBreakpoints == false`)

### Test 4: User BPs suppressed when temps active
Set a regular breakpoint AND a temp breakpoint on different nodes. Simulate hitting the regular breakpoint's node. Verify:
- Regular BP is NOT honored (no pause from user BP)
- Hitting the temp BP node DOES pause
- After temp hit, user BPs are restored (next hit on user BP node pauses)

### Test 5: Step from a node with known successors
Set up session with graph, pause at node A. Call `Step()`. Verify:
- Temp breakpoints set on A's successors
- User BPs suppressed
- Session resumes (RequestResume called)
- Simulating successor hit → pauses + temps cleared

### Test 6: Continue clears leftover temps
Set temp breakpoints. Call `Continue()`. Verify temps are cleared.

### Test 7 (optional): Step on terminal node resumes
Pause at Return node. Call `Step()`. Verify `Continue()` is called (resume without temp BPs).

---

## 🎯 Success Criteria

- [ ] Build 0 errors
- [ ] Hrot.Blueprints.Tests → 7 pre-existing, 0 new
- [ ] All 6+ CF6 tests pass
- [ ] ExecSuccessors correctly follows exec wires
- [ ] Temp BPs are invisible (not in GetBreakpoints)
- [ ] Temp BPs are one-shot (auto-clear on hit)
- [ ] User BPs suppressed during step pass
- [ ] Step computes correct successors
- [ ] Step resumes (not single-tick)
- [ ] Continue clears leftover temps
- [ ] Graph registered in session for stepping

---

## ⚠️ Common Pitfalls

- **Pin.Direction values:** Check the actual string values used ("Out"/"In" or "Output"/"Input") in `.bp.json` files and `GraphTypes.cs`.
- **BreakpointTargets translation:** Temp BPs must be translated through `BreakpointTargets` (authored→block-probe), same as regular BPs.
- **Don't remove `_stepMode` entirely:** Keep the step-mode fields but mark as legacy. They may still be used by the step buttons during the CF-5 integration.
- **Graph registration timing:** Graphs must be registered before stepping is attempted. Register them when the blueprint is opened or compiled.
- **Count4 graph structure:** For testing with Count4: EventEntry→SetVariable→Add(FunctionCall)→Sequence→Delay (latent)→Return. Sequence has a single exec successor (Delay). Delay is latent (execution suspends).
