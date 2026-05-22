# BATCH-27 — Fix review3.md Discrepancies

## Overview

Fix five active discrepancies identified in `.dev/blueprints-1/review3.md` against
the current codebase state. Two are critical architecture fixes (ghost stub, debug
session misplacement + Detach stub), two are functional completions (catalog
population, OnNodeExecuted event), and one is a minor defensive fix
(Watch.WriteValue ref).

Items from review3.md that are already done and must NOT be re-done:
- TASK-HR-002 `SimulateReload` — already fully implemented in `BlueprintTestFixture.cs`
- TASK-HR-003 Hot Reload test suite — already fully implemented (26 tests pass)
- Duplicate AiHotReloadCoordinator — the two classes are in different assemblies
  (`internal` in `Hrot.Editor`, `public` in `FDP/Toolkits`); the FDP/Toolkits version
  is the canonical one used by the Blueprint system and was verified correct in BATCH-26.
  Do NOT touch either coordinator file.

## Design references

- `.dev/blueprints-1/review3.md` — the review this batch addresses
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/` — Core assembly (interfaces, compiler)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` — Editor assembly (concrete services)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/` — Test project (already references both)

## Flaw 1 (P1) — Delete ghost stub `BlueprintCompiler.cs`

**File to delete:**
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintCompiler.cs`

This is the Phase 1 stub class at the root of Core. It throws `NotImplementedException`
and is entirely superseded by the real compiler at
`Hrot.Blueprints.Core/Compiler/BlueprintCompiler.cs`.

Action: Delete the file. Nothing else in the codebase references it (the stub class
is in namespace `Hrot.Blueprints.Core` with no callers).

## Flaw 2 (P2) — Populate the three static catalogs

The three catalog stubs in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/`
return empty lists. This disables Stage-2 validation for graphs that reference channel
commands, engine events, or wait primitives (the validators are explicitly opt-in:
they skip when the catalog returns 0 entries). Populating them enables correct
validation for real graphs.

`Hrot.Blueprints.Core` already has a project reference to `Fdp.Toolkits`, so all
types listed below are accessible without creating circular dependencies.

### `BuiltInEngineEventCatalog.cs`

Replace the empty return with:

```csharp
using Fdp.Core.Events;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Events;

// ...

public IReadOnlyList<EngineEventCatalogEntry> GetEntries() =>
    new List<EngineEventCatalogEntry>
    {
        new("HitEvent",              typeof(HitEvent)),
        new("BehaviorFinishedEvent", typeof(BehaviorFinishedEvent)),
        new("TargetVisibleEvent",    typeof(TargetVisibleEvent)),
    };
```

### `BuiltInChannelCommandCatalog.cs`

Replace the empty return with:

```csharp
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;

// ...

public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() =>
    new List<ChannelCommandCatalogEntry>
    {
        new("Locomotion/MoveTo",          typeof(LocomotionChannel),   NavigationConstants.ActionIdMoveTo,          typeof(int)),
        new("Locomotion/FollowRoute",     typeof(LocomotionChannel),   NavigationConstants.ActionIdFollowRoute,     typeof(int)),
        new("Weapon/AimAndFire",          typeof(WeaponChannel),       CombatConstants.ActionIdAimAndFire,          typeof(int)),
        new("Interaction/OpenDoor",       typeof(InteractionChannel),  BehaviorConstants.ActionIdOpenDoor,          typeof(int)),
        new("Interaction/EjectPassengers",typeof(InteractionChannel),  BehaviorConstants.ActionIdEjectPassengers,   typeof(int)),
    };
```

Note: `typeof(int)` is used as a placeholder for the params type. The concrete
params structs (e.g. `CgfNodes.MoveToLocationParams`) live in `Hrot.AI.Behaviors`
which Core cannot reference without creating a circular dependency. The params type
is not used by the compiler pipeline (Stages 2-8); it is only used by the editor
inspector for rendering parameter values. This is acceptable for now.

### `BuiltInWaitPrimitiveCatalog.cs`

Replace the empty return with:

```csharp
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Navigation;

// ...

public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() =>
    new List<WaitPrimitiveCatalogEntry>
    {
        new("WaitForChannel:Locomotion",            WaitKind.Channel,          typeof(LocomotionChannel)),
        new("WaitForChannel:Weapon",                WaitKind.Channel,          typeof(WeaponChannel)),
        new("WaitForChannel:Interaction",           WaitKind.Channel,          typeof(InteractionChannel)),
        new("WaitForEvent:BehaviorFinishedEvent",   WaitKind.Event,            typeof(BehaviorFinishedEvent)),
        new("WaitForRingBufferResult:Pathfinding",  WaitKind.RingBufferResult, typeof(PathfindingBatchData)),
    };
```

## Flaw 3 (P1) — Move `BlueprintDebugSession` to `Hrot.Blueprints.Editor`

**Current file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`
**Target file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`

Per the Debug Protocol DD §2.3, the concrete session belongs in the Editor assembly.
Core should only contain the interface (`IBlueprintDebugSession`), `DebugProbe`, and
the test helper `CapturingDebugSession`.

**Important**: Keep the namespace `Hrot.Blueprints.Core.Debug` — do NOT change it.
All test files use `using Hrot.Blueprints.Core.Debug;` and the Tests project already
references `Hrot.Blueprints.Editor`, so keeping the namespace makes this a
transparent move.

Do NOT create a `Debug/` subfolder in the Editor project — the codebase-wide
`.gitignore` filters out any folder literally named `Debug`. Place the file directly
in the Editor project root.

Steps:
1. Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
   with the full content of the old file (see Flaw 4 below for `Detach()` and
   Flaw 5 below for `OnNodeExecuted` — incorporate ALL fixes at once in this new file).
2. Delete `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/BlueprintDebugSession.cs`.

## Flaw 4 (P1) — Implement `Detach()`

**Current state:** `public void Detach() => throw new NotImplementedException();`

Implement the teardown logic:

```csharp
public void Detach()
{
    if (_isPaused) Continue();
    DebugProbe.Sink = NullProbeSink.Instance;
    _breakpoints.Clear();
    _bpByNodeString.Clear();
    _watches.Clear();
    _watchesByPinString.Clear();
    _activeEntities.Clear();
    _history.Clear();
    _currentCallDepth.Clear();
    _debugMaps.Clear();
    _pdbLocators.Clear();
    OnSessionStateChanged?.Invoke();
}
```

`NullProbeSink` and `DebugProbe` are in `namespace Hrot.Blueprints.Core.Debug`
(same namespace), so no extra usings are needed.

## Flaw 5 (P2) — Fire `OnNodeExecuted` event and implement `GetRecentNodeHistory`

**Current state in `OnNodeEnter`:** Records to `_history` dict but never fires
`_onNodeExecuted`. The interface method `GetRecentNodeHistory(int maxCount = 100)`
returns `Array.Empty<NodeExecuted>()`.

### Fix `OnNodeEnter`

After `hist.Record(new NodeHistoryEntry(nodeId, _view.Tick, _view.Time));`, add:

```csharp
// Fire OnNodeExecuted so subscribers (e.g., Callstack window) can update the trail.
_onNodeExecuted?.Invoke(new NodeExecuted(self, nodeId, _view.Tick));
```

### Fix `GetRecentNodeHistory(int maxCount = 100)`

Replace the stub `Array.Empty<NodeExecuted>()` return with a real implementation
that aggregates recent history across all tracked entities:

```csharp
public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
{
    var all = new List<NodeExecuted>();
    foreach (var (entity, hist) in _history)
    {
        foreach (var entry in hist.GetRecent(maxCount))
            all.Add(new NodeExecuted(entity, entry.NodeId, entry.Tick));
    }
    // Return the most recent maxCount entries across all entities.
    if (all.Count > maxCount)
        all.Sort((a, b) => b.Tick.CompareTo(a.Tick));
    return all.Count <= maxCount ? all.AsReadOnly() : all.Take(maxCount).ToList().AsReadOnly();
}
```

Check the `NodeExecuted` record definition in `IBlueprintDebugSession.cs` to confirm
the constructor signature before implementing. It likely is `NodeExecuted(Entity Self,
string NodeId, uint Tick)`.

## Flaw 6 (P3) — Fix `Watch.WriteValue<T>` ref

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs`

**Current code:**
```csharp
Unsafe.WriteUnaligned(ref _valueBuffer[0], value);
```

**Fix:**
```csharp
Unsafe.WriteUnaligned(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(_valueBuffer), value);
```

Add `using System.Runtime.InteropServices;` at the top of the file if it isn't
already present.

This acquires the reference to the array's first element more robustly without
going through the indexer, which is more trimming-safe.

## Tests to write

All tests go in `Hrot.Blueprints.Tests` unless otherwise noted.

### T1 — Detach() clears state (`Debug/DebugSessionInterfaceTests.cs`)

Add a test `Detach_ClearsAllStateAndNullsProbe`:
1. Create a `BlueprintDebugSession`.
2. Set `DebugProbe.Sink = session`.
3. Call `session.SetBreakpoint(...)` and `session.AddWatch(...)`.
4. Call `session.Detach()`.
5. Assert: `DebugProbe.Sink == NullProbeSink.Instance`
6. Assert: `session.GetBreakpoints().Count == 0`
7. Assert: `session.GetWatches().Count == 0`
8. Assert: `session.IsPaused == false`

### T2 — Detach() calls Continue() first if paused (`Debug/DebugSessionInterfaceTests.cs`)

1. Create a session with a stub time controller that tracks `RequestResume` calls.
2. Trigger pause via `session.Pause()`.
3. Call `session.Detach()`.
4. Assert: time controller received `RequestResume()` (Continue was called).
5. Assert: `session.IsPaused == false` after Detach.

### T3 — OnNodeExecuted fires on node enter (`Debug/NodeHistoryTests.cs`)

1. Create a session.
2. Subscribe to `IBlueprintDebugSession.OnNodeExecuted`.
3. Call `session.OnNodeEnter(entity, "node-1")`.
4. Assert: event fired once with `NodeId == "node-1"` and `Self == entity`.

### T4 — GetRecentNodeHistory returns aggregated history (`Debug/NodeHistoryTests.cs`)

1. Create a session.
2. Call `OnNodeEnter(e1, "a")`, `OnNodeEnter(e2, "b")`, `OnNodeEnter(e1, "c")`.
3. Call `session.GetRecentNodeHistory(100)`.
4. Assert: result contains 3 entries (all 3 nodes).

### T5 — Catalog entries populated (new file `Compiler/CatalogTests.cs`)

```csharp
[Fact]
public void BuiltInEngineEventCatalog_HasExpectedEntries()
{
    var entries = BuiltInEngineEventCatalog.Instance.GetEntries();
    Assert.True(entries.Count >= 2);
    Assert.Contains(entries, e => e.Name == "HitEvent");
    Assert.Contains(entries, e => e.Name == "BehaviorFinishedEvent");
}

[Fact]
public void BuiltInChannelCommandCatalog_HasLocoAndWeaponEntries()
{
    var entries = BuiltInChannelCommandCatalog.Instance.GetEntries();
    Assert.Contains(entries, e => e.Name == "Locomotion/MoveTo");
    Assert.Contains(entries, e => e.Name == "Weapon/AimAndFire");
}

[Fact]
public void BuiltInWaitPrimitiveCatalog_HasChannelAndEventEntries()
{
    var entries = BuiltInWaitPrimitiveCatalog.Instance.GetEntries();
    Assert.Contains(entries, e => e.Name == "WaitForChannel:Locomotion");
    Assert.Contains(entries, e => e.Name == "WaitForEvent:BehaviorFinishedEvent");
}

[Fact]
public void Stage2_ValidatesChannelCommand_WhenCatalogIsPopulated()
{
    // Build a graph with a ChannelCommandNode referencing an UNKNOWN command.
    // Stage 2 should reject it now that the catalog is non-empty.
    var asset = BlueprintAssetBuilder
        .AiPrimitive("TestAsset")
        .WithGraph("Main", g => g.Entry().ChannelCommand("NonExistent", "UnknownAction").Return())
        .Build();

    var options = new CompileOptions(
        Mode:            CompilerMode.Debug,
        NodeRegistry:    BuiltInNodeRegistry.Instance,
        TypeRegistry:    StaticTypeRegistry.Instance,
        EngineEvents:    BuiltInEngineEventCatalog.Instance,
        ChannelCommands: BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:  BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    var result = new BlueprintCompiler().Compile(asset, options);
    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.BP1401);
}
```

Note: The last test (Stage2 validation with channel commands) requires that the
graph builder support `ChannelCommandNode`. Check if `BlueprintAssetBuilder`
has a `.ChannelCommand(...)` method. If not, build the `BlueprintAsset` directly
with a `ChannelCommandNode`. If `ChannelCommandNode` doesn't exist in the test
builder, write only the first three catalog tests (T5a, T5b, T5c) and skip the
Stage2 integration test.

## Build verification

Run:
```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj
dotnet test  Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --no-build -q
```

Expected: 0 errors, tests pass (count will increase by the new tests).

## Batch report format

File your report at `.dev/blueprints-1/reports/BATCH-27-REPORT.md`.

Include:
- Files created / modified / deleted
- Test results (total passed/failed/skipped)
- Any deviations from these instructions with reasoning
