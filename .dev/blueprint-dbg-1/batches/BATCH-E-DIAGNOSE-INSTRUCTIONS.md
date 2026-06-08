# BATCH-E-DIAGNOSE: End-to-end breakpoint test + diagnostic logging

**Depends on:** BATCH-E + E-FIX  
**Estimated Effort:** 1 hour  
**Priority:** CRITICAL

---

## 🚨 EXECUTION DIRECTIVE

**Do NOT ask questions or permission. Edit, build, test, report.**

---

## Context

Breakpoints show on canvas but don't pause. The full chain (`OnNodeEnter` → `HandleBreakpointHit` → `DataBreakpointManager.OnExternalHit` → `OnHit` → `RequestPause`) looks correct in code, but existing tests bypass the `DataBreakpointManager` by never calling `SetDataBreakpointManager`.

This batch adds a diagnostic test + tracing so we can see where the chain breaks.

---

## Task 1: Add end-to-end test with real DataBreakpointManager

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/BreakpointTests.cs` (APPEND)

Add this test method to the existing `BreakpointTests` class:

```csharp
[Fact]
public void Breakpoint_Fires_Through_DataBreakpointManager()
{
    var tc      = new MockTimeController();
    var session = MakeSession(tc);
    
    // Wire the real DataBreakpointManager (simulating production).
    var preTickSnapshot = new Fdp.Core.EntityRepository();
    // Register the Entity component so preTickSnapshot can hold entities.
    preTickSnapshot.RegisterComponent<Fdp.Core.Components.EntityOwner>();
    var snapshotProvider = new Hrot.Blueprints.Editor.Debug.DebugSnapshotProvider(preTickSnapshot);
    var editSvc = new Hrot.Diagnostics.Breakpoints.ComponentEditServiceBuilder().Build();
    var bpManager = new Hrot.Diagnostics.Breakpoints.DataBreakpointManager(
        session.World, preTickSnapshot, snapshotProvider,
        tc, // Use the mock time controller
        new Hrot.Diagnostics.Breakpoints.PredicateCompiler(editSvc, null),
        null);
    session.SetDataBreakpointManager(bpManager);
    
    // Set a breakpoint.
    session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
    
    // Fire the probe.
    ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
    
    // Assert pause was requested via the DataBreakpointManager chain.
    Assert.Equal(1, tc.PauseRequestCount);
}
```

Notes:
- `session.World` — check `MakeSession` returns a session with accessible `World` property. If not, use `fixture.World` from a `BlueprintTestFixture`.
- The `preTickSnapshot` needs basic component registrations. If compilation fails, check what components `DataBreakpointManager` expects.
- `MockTimeController` already has `PauseRequestCount` — verify it implements `IEngineDebugTimeController`.
- Add required `using` statements.

---

## Task 2: Add diagnostic trace to BlueprintDebugSession.OnNodeEnter

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` (UPDATE)

In `OnNodeEnter`, after the entity filter check (line 88), add temporary diagnostic output:

```csharp
public void OnNodeEnter(Entity self, string nodeId)
{
    if (_entityFilter.HasValue && self != _entityFilter.Value) return;

    // TEMP DIAGNOSTIC: log every probe to verify they're reaching the session.
    System.Console.WriteLine($"[BP-DEBUG] OnNodeEnter: entity={self.Id} nodeId={nodeId} hasBreakpoint={_bpByNodeString.ContainsKey(nodeId)} bpCount={_bpByNodeString.Count} mgr={_dataBreakpointManager != null} paused={_isPaused}");
```

This writes to stdout, visible in test output and in the editor's console.

---

## Task 3: Add diagnostic trace to DataBreakpointManager.OnExternalHit

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` (UPDATE)

At the start of `OnExternalHit`, add:

```csharp
public void OnExternalHit(string tag, Entity entity)
{
    System.Console.WriteLine($"[BP-DEBUG] OnExternalHit: tag={tag} hasRegistration={_externalHitPredicates.ContainsKey(tag)} totalPredicates={_externalHitPredicates.Count}");
    if (_externalHitPredicates.TryGetValue(tag, out var registrations))
    {
```

---

## Build and test

```
dotnet build IOS-IG-SimHost.sln -c Debug
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug --no-build --filter "BreakpointTests"
```

Fix any compilation errors. The new test should pass (proving the chain works in tests).

---

## Write report

Write to `.dev/blueprint-dbg-1/reports/BATCH-E-DIAGNOSE-REPORT.md`:
- What was added
- Build result
- Test result for BreakpointTests (specifically the new `Breakpoint_Fires_Through_DataBreakpointManager` test)
- Console output from the diagnostic traces
