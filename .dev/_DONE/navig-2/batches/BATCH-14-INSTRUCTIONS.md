# BATCH-14 Instructions — NAV-P10-T0: NavTestHarness + S1 + S7 Integration Tests

## Onboarding

Read these docs first:
- `.dev/navig-2/DD-Tests-Nav.md` §2.3, §5, §6.1 (S1_SimpleCorridor), §6.7 (S7_FailedUnreachable), §7, §8
- `.dev/navig-2/TASK-DETAILS.md` section NAV-P10-T0

## Workspace root
`d:\Work\IOS-IG-SimHost-FDP-2`

## AGENTS.md constraints
- No Unicode in comments/string literals
- Preserve existing comments
- Minimize diffs
- 0 build errors before finishing

---

## Critical context — read these files FIRST

```
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavigationFakesModule.cs     (providers, RegisterProviders)
FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs  (needs LocomotionChannel+NavState)
FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingResultMaterializationSystem.cs  (needs LocomotionChannel)
FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs   (updates SimTransform via crowd)
FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs  (arrival check, MoveCompletedEvent)
FDP/Toolkits/Fdp.Toolkits/Navigation/Modules/NavigationSolverModule.cs   (Tick pattern for solver)
FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationActions.cs                 (MoveToParams struct)
FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContracts.cs               (NavigationConstants)
FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs        (LocomotionChannel)
FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs  (existing world setup)
FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestMaps.cs                 (if exists — find it)
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMap.cs                   (map data structure)
FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs          (how maps/polygons work)
CarKinem.Core (search for NavState, NavAgentProfile, FrustrationTicks)
```

Also search for:
- `struct MoveToParams` definition (in NavigationActions.cs)
- `ActionIdMoveTo`, `ActionIdPlanRoute` etc. in NavigationContracts.cs
- `NavTestMaps` class (may be in `Fdp.Toolkits.Tests/Navigation/NavTestMaps.cs` or similar)
- How `FakeNavmeshProvider` creates test polygons for a "corridor" map

---

## Task — NAV-P10-T0: NavTestHarness

### File to create

`FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`

### NavTestHarness design

The harness is a single-class test utility that wires all navigation systems together in one process without DDS. It is NOT a full `ModuleHostKernel` — just manually sequenced system calls.

```csharp
// Namespace: Fdp.Toolkit.Navigation.Tests
public sealed class NavTestHarness : IDisposable
{
    public EntityRepository Repo { get; }
    public CapturedEventLog EventLog { get; }

    // Direct fake references
    public FakeNavmeshProvider    Navmesh    { get; }
    public FakeDtCrowdProvider    Crowd      { get; }
    public FakeVolumetricPathProvider Volumetric { get; }
    public SharedPathRegistry     Paths      { get; }

    // Test API (may return (IFakeNavmeshProviderTestApi)Navmesh etc.)
    public IFakeNavmeshProviderTestApi NavmeshApi => (IFakeNavmeshProviderTestApi)Navmesh;

    // Factory methods
    public static NavTestHarness LoadMap(NavTestMap map);
    public static NavTestHarness Empty();  // empty providers, no map

    // Tick control
    public void Tick(int count = 1);
    public bool PumpUntil(Func<bool> condition, int maxTicks = 600, string failMessage = null);
    public void PumpFor(int ticks);

    // Entity spawning
    public Entity SpawnInfantry(Vector2 pos);

    // Action convenience methods (write LocomotionChannel + NavigationIntent)
    public void IssueMoveTo(Entity e, Vector2 destination, byte flags = 0, int routeHandle = 0);

    // IDisposable
    public void Dispose();
}
```

### Internal implementation

1. **Constructor** (private, used by factories):
   - Create `EntityRepository` (extend `NavigationTestWorldFactory.Create()` or build inline)
   - Register ALL required components (see list below)
   - Create `NavigationFakesModule(map)` (or empty)
   - Call `module.RegisterProviders(repo)` (registers `INavmeshProvider`)
   - Store `module.Navmesh`, `module.Crowd`, `module.Volumetric`, `module.Paths`
   - Create a shared `TrajectoryPoolManager _pool = new TrajectoryPoolManager()`
   - Instantiate systems:
     - `_bridge = new NavigationIntentBridgeSystem(_pool, module.Crowd)`
     - `_materialize = new PathfindingResultMaterializationSystem()`
     - `_crowdUpdate = new CrowdAgentUpdateSystem(module.Crowd)`
     - `_navExec = new NavigationExecutionSystem()`
     - `_corridorPreview = new CorridorPreviewSystem(module.Paths)` (if it takes a path registry)
   - Create `EventLog = new CapturedEventLog(repo)`

2. **Required components to register** (add to what NavigationTestWorldFactory already registers):
   - `LocomotionChannel` (`GlobalComponentIds.LocomotionChannel`)
   - `ActorCapabilityState` (register if needed by LocomotionDispatcherSystem)
   - `CrowdAgent`, `NavAgentProfile` — already in NavigationTestWorldFactory
   - `NavigationCorridorMuscle`, `NavigationCorridorPreview` — already there
   - `PathfindingBatchData` singleton: `repo.SetSingleton(new PathfindingBatchData())`

   Check the `NavigationTestWorldFactory.cs` — it already registers many of these. Only add what's missing.

3. **`Tick(int count)` implementation**:
   ```csharp
   private void SingleTick()
   {
       const float Dt = 1f / 60f;

       // Flush any pending ECBs before systems run
       // (some systems may need this - check if EntityRepository has Flush/PlaybackCommandBuffers)

       // 1. Intent bridge: translates NavigationIntent + LocomotionChannel -> NavState + PathfindingRequestEvent
       _bridge.Execute(Repo, Dt);

       // 2. Run path solver synchronously (processes PathfindingRequestEvent -> PathfindingResultEvent)
       //    Note: PathfindingResultEvent is published into the CURRENT buffer (readable in same frame)
       //    But the bridge swaps buffers AFTER its execution... check if events need a SwapBuffers call first
       new PathfindingSolverSystem(default, _pool, Navmesh, Volumetric).Execute(Repo, Dt);

       // 3. Swap event buffers so systems can read events published above
       Repo.Bus.SwapBuffers();

       // 4. Materialize path results into NavigationCorridorMuscle
       _materialize.Execute(Repo, Dt);

       // 5. Update crowd agent positions (integrates SimTransform via velocity)
       _crowdUpdate.Execute(Repo, Dt);

       // 6. Check arrival, emit MoveCompletedEvent etc.
       _navExec.Execute(Repo, Dt);

       // 7. Swap buffers again so CapturedEventLog can read events from this tick
       Repo.Bus.SwapBuffers();

       // 8. Capture events from this tick
       EventLog.Capture(Repo);

       // 9. Update GlobalTime
       ref var t = ref Repo.GetSingleton<GlobalTime>();
       t.FrameNumber++;
       Repo.SetSingleton(t);
   }
   ```

   **CRITICAL**: Understand the event bus double-buffering. `SwapBuffers()` moves the "write" buffer to "read". Events published in frame N are readable in frame N+1 after `SwapBuffers()`. You need to call `SwapBuffers()` at the right place so the solver's `PathfindingResultEvent` is visible to `PathfindingResultMaterializationSystem`.

   Look at how existing tests handle this - search for `SwapBuffers` calls in test files.

4. **`PumpUntil` / `PumpFor`**:
   ```csharp
   public bool PumpUntil(Func<bool> condition, int maxTicks = 600, string failMessage = null)
   {
       for (int i = 0; i < maxTicks; i++)
       {
           if (condition()) return true;
           Tick(1);
       }
       Assert.Fail(failMessage ?? $"PumpUntil timed out after {maxTicks} ticks");
       return false;
   }

   public void PumpFor(int ticks) => Tick(ticks);
   ```

5. **`SpawnInfantry(Vector2 pos)`**:
   ```csharp
   public Entity SpawnInfantry(Vector2 pos)
   {
       var entity = Repo.CreateEntity();
       Repo.AddComponent(entity, new SimTransform { Position = new Vector3(pos.X, 0f, pos.Y) });
       Repo.AddComponent(entity, new SimVelocity());
       Repo.AddComponent(entity, new NavigationIntent());
       Repo.AddComponent(entity, new NavigationStatus());
       Repo.AddComponent(entity, new FrustrationTicks());
       Repo.AddComponent(entity, new NavState());
       Repo.AddComponent(entity, new LocomotionChannel());
       Repo.AddComponent(entity, new NavAgentProfile { AgentRadius = 0.4f, AgentHeight = 1.8f });
       // ActorCapabilityState with CanMove = true (if needed)
       if (Repo.IsComponentTypeRegistered<ActorCapabilityState>())
           Repo.AddComponent(entity, new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
       return entity;
   }
   ```

6. **`IssueMoveTo(Entity e, Vector2 destination, byte flags, int routeHandle)`**:

   This is the CRITICAL method. It must write both `LocomotionChannel` and `NavigationIntent`.

   Read `MoveToParams` definition in `NavigationActions.cs` first. Understand how `LocomotionChannel.Params` works.

   ```csharp
   public unsafe void IssueMoveTo(Entity e, Vector2 destination, byte flags = 0, int routeHandle = 0)
   {
       uint instanceId = ++_actionInstanceCounter;

       // Write LocomotionChannel
       ref var ch = ref Repo.GetComponentRW<LocomotionChannel>(e);
       ch.ActiveAction = NavigationConstants.ActionIdMoveTo;
       ch.ActionInstanceId = instanceId;
       ch.Status = NodeStatus.Running; // or default

       var p = new MoveToParams
       {
           Destination = destination,
           Flags = flags,
           RouteHandle = routeHandle,
           Speed = 5.0f,   // default infantry speed
       };
       Unsafe.WriteUnaligned(ref ch.Params[0], p);

       // Write NavigationIntent for NavigationExecutionSystem
       ref var intent = ref Repo.GetComponentRW<NavigationIntent>(e);
       intent.Mode = NavigationMode.DirectPoint;
       intent.FinalDestination = destination;
       intent.IntentId = instanceId;
       intent.ArrivalRadius = 1.5f;
       intent.TargetSpeed = 5.0f;
       intent.Flags = flags;
       intent.RouteHandle = routeHandle;
   }
   ```

   NOTE: Read `MoveToParams` fields carefully - some fields may have different names than what's shown above. The exact field names are in `NavigationActions.cs`.

---

## Task — CapturedEventLog

Create a simple `CapturedEventLog` class in the same file as `NavTestHarness`:

```csharp
public sealed class CapturedEventLog
{
    private readonly List<MoveStartedEvent>     _moveStarted   = new();
    private readonly List<MoveCompletedEvent>   _moveCompleted = new();
    private readonly List<PathReplannedEvent>   _pathReplanned = new();
    private readonly List<MoveBlockedEvent>     _moveBlocked   = new();
    // Add others as needed

    internal void Capture(EntityRepository repo)
    {
        foreach (var evt in repo.Bus.ReadEvents<MoveStartedEvent>())
            _moveStarted.Add(evt);
        foreach (var evt in repo.Bus.ReadEvents<MoveCompletedEvent>())
            _moveCompleted.Add(evt);
        foreach (var evt in repo.Bus.ReadEvents<PathReplannedEvent>())
            _pathReplanned.Add(evt);
        foreach (var evt in repo.Bus.ReadEvents<MoveBlockedEvent>())
            _moveBlocked.Add(evt);
    }

    public bool Has<T>(Entity target) where T : struct { /* impl below */ }
    public T    Get<T>(Entity target) where T : struct { /* throws if missing */ }
    public int  Count<T>() where T : struct { /* total count */ }
    public void Clear()    { /* clear all lists */ }
}
```

**NOTE**: Look at how events are structured. `MoveCompletedEvent` has a `Target` field (entity). `MoveStartedEvent` might NOT have a target entity - check its definition. Adapt the `Has<T>(Entity)` method accordingly.

Look at the event struct definitions in `PathfindingEvents.cs` and the main `NavigationComponents.cs` file.

---

## Task — NavTestMaps

If `NavTestMaps.cs` doesn't exist, create it in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestMaps.cs`.

```csharp
// Namespace: Fdp.Toolkit.Navigation.Tests
public static class NavTestMaps
{
    // Straight 30m corridor. Used by S1_SimpleCorridor.
    public static NavTestMap LoadCorridor()
    {
        // Single polygon: a wide rectangle from (0,0) to (30,5).
        // Create using NavTestMap factory or builder.
        // Read NavTestMap.cs / FakeNavmeshProvider.cs to understand how to create polygon data.
    }

    // L-shaped path. Two polygons meeting at right angle.
    // Used by S2_LBendFollow, S2b_LBendWithCorridorPreview.
    public static NavTestMap LoadLBend()
    {
        // Polygon 1: 20m horizontal  (0,0)-(20,5)
        // Polygon 2: 20m vertical    (20,0)-(25,20)
    }

    // Disconnected graph (no path between start and destination). Used by S7.
    public static NavTestMap LoadStuck()
    {
        // Two separate polygons with no adjacency.
    }
}
```

**Read `NavTestMap.cs` and the existing `NavTestMaps` class (if it exists) to understand the polygon format.** If a `NavTestMaps` class already exists somewhere, check what it already provides.

---

## Task — S1_SimpleCorridor Integration Test

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S1_SimpleCorridorTests.cs`.

```csharp
using System.Numerics;
using Fdp.Toolkit.Navigation.Tests;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    public sealed class S1_SimpleCorridorTests : System.IDisposable
    {
        private NavTestHarness _h;

        public S1_SimpleCorridorTests()
            => _h = NavTestHarness.LoadMap(NavTestMaps.LoadCorridor());

        public void Dispose() => _h.Dispose();

        [Fact]
        public void SimpleCorridor_InfantryMovesToFarEnd_Arrives()
        {
            var entity = _h.SpawnInfantry(Vector2.Zero);
            _h.IssueMoveTo(entity, new Vector2(28f, 0f));

            _h.PumpUntil(
                () => _h.EventLog.Has<MoveCompletedEvent>(entity),
                maxTicks: 600,
                "S1: MoveCompletedEvent never fired within 600 ticks");

            var completed = _h.EventLog.Get<MoveCompletedEvent>(entity);
            Assert.Equal(NavigationResult.Arrived, completed.Reason);
        }
    }
}
```

**NOTE**: This test may be simplified or skipped if the full pipeline doesn't work yet. The primary success criterion for NAV-P10-T0 is that the harness BUILDS and a basic smoke test passes, even if some integration scenarios fail.

---

## Task — S7_FailedUnreachable Integration Test

Create `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S7_FailedUnreachableTests.cs`.

```csharp
public sealed class S7_FailedUnreachableTests : System.IDisposable
{
    private NavTestHarness _h;

    public S7_FailedUnreachableTests()
        => _h = NavTestHarness.LoadMap(NavTestMaps.LoadStuck());

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Stuck_InfantryMovesToDisconnectedDest_ReturnsUnreachable()
    {
        // The fake navmesh has two disconnected polygons.
        // Entity is in polygon 1; destination is in polygon 2.
        var entity = _h.SpawnInfantry(new Vector2(1f, 1f));
        _h.IssueMoveTo(entity, new Vector2(50f, 50f));

        _h.PumpUntil(
            () => _h.EventLog.Has<MoveCompletedEvent>(entity),
            maxTicks: 200,
            "S7: MoveCompletedEvent never fired");

        var completed = _h.EventLog.Get<MoveCompletedEvent>(entity);
        Assert.Equal(NavigationResult.FailedUnreachable, completed.Reason);
    }
}
```

---

## Implementation guidance

### Event bus SwapBuffers timing

The double-buffer pattern means:
- `Publish()` writes to buffer N (write-buffer)
- `SwapBuffers()` makes buffer N readable and starts buffer N+1 as write-buffer
- `ReadEvents<T>()` reads from the current read-buffer

For the harness tick:
1. `SwapBuffers()` before running systems — previous events become readable
2. Systems run and publish new events to write-buffer
3. `SwapBuffers()` at end — new events become readable for `CapturedEventLog.Capture()`

OR:
1. Systems run and publish events
2. `SwapBuffers()` once
3. `CapturedEventLog.Capture()` reads

Look at how existing tests do it (search for `SwapBuffers` in test files).

### PathfindingResultMaterializationSystem and PathfindingBatchData

`PathfindingResultMaterializationSystem` checks `if (!repo.HasSingleton<PathfindingBatchData>()) return;`

Make sure the harness calls `repo.SetSingleton(new PathfindingBatchData())` during setup.

Also register the `PathfindingBatchData` type: check if it's a regular singleton or needs separate registration.

### NavigationExecutionSystem arrival check

`NavigationExecutionSystem` checks `nav.HasArrived` if `NavState` is present. If using fake crowd, the crowd provider moves the entity step-by-step. Once `SimTransform.Position` is within `ArrivalRadius` of `FinalDestination`, check Cartesian distance (when `NavState` is absent) OR check `NavState.HasArrived` (when NavState is present).

For simplicity, you can choose NOT to add `NavState` to harness entities — then the system uses the Cartesian distance check directly.

However `NavigationIntentBridgeSystem` needs `NavState` to map `NavigationMode.DirectPoint` -> `KinematicsMode.Direct` (it queries `.With<NavigationIntent>().With<NavState>()`). So you SHOULD add `NavState`.

If you add `NavState`, then `NavigationExecutionSystem` will check `nav.HasArrived`. Who sets `nav.HasArrived`? The CarKinem `CarKinematicsSystem` or `LinearKinematicsSystem`. For `NavTestHarness`, you should set `NavState.HasArrived = 1` when the entity is within the arrival radius. This can be done in a tiny helper system or inline before `NavigationExecutionSystem.Execute()`.

Actually, looking at the code: `NavigationExecutionSystem` does this check:
```csharp
if (repo.HasComponent<NavState>(entity))
{
    var nav = repo.GetComponent<NavState>(entity);
    arrived = nav.HasArrived != 0;
}
else
{
    // Cartesian check
    var pos2D = new Vector2(tf.Position.X, tf.Position.Y);
    float dist = Vector2.Distance(pos2D, intent.FinalDestination);
    arrived = dist <= intent.ArrivalRadius;
}
```

**Simplest approach**: Do NOT add `NavState` to harness entities. Use the Cartesian distance check. But then `NavigationIntentBridgeSystem` won't find entities in its query (which requires `.With<NavState>()`).

**Resolution**: Check `NavigationIntentBridgeSystem`'s query again. It has two queries:
1. `repo.Query().With<NavigationIntent>().With<NavState>().Build()` — for the legacy mode mapping
2. `repo.Query().With<LocomotionChannel>().Build()` — for the new pipeline

The LocomotionChannel query is what publishes `PathfindingRequestEvent`. The NavState query is for legacy mapping.

**So for NavTestHarness**: add `NavState` to entities for completeness. Set `HasArrived=1` in a simple helper each tick after `CrowdAgentUpdateSystem` if the entity is within radius. OR just use distance check by skipping NavState.

**Recommended**: Add `NavState` but do NOT set `HasArrived` from NavTestHarness. Let `NavigationExecutionSystem` use the fallback Cartesian check by... wait, if `NavState` is present, it uses `nav.HasArrived`. So `HasArrived` must be set somehow.

Let me simplify: **do NOT add `NavState` component to harness Infantry entities**. Then:
- `NavigationIntentBridgeSystem` still runs the `LocomotionChannel` path (it uses a separate query)
- `NavigationExecutionSystem` uses Cartesian distance check
- `CrowdAgentUpdateSystem` integrates position

This should work for S1 and S7.

### How FakeDtCrowdProvider moves entities

Looking at `CrowdAgentUpdateSystem`:
1. Calls `_dtCrowd.Update(deltaTime, view)` — this advances the fake crowd simulation
2. Gets velocity from `_dtCrowd.GetAgentVelocity(entity)` for each `CrowdAgent`-tagged entity
3. Integrates `SimTransform.Position += velocity * dt`

The `FakeDtCrowdProvider` implements simple steering toward the target. After enough ticks, the agent reaches the destination.

For S7 (disconnected), `PathfindingSolverSystem` returns `IsReachable=false`. The materialization system won't materialize a corridor. But what happens then with `NavigationExecutionSystem`? It sees:
- `NavigationIntent.Mode == DirectPoint` 
- The entity has `NavigationStatus.Result = InProgress` (set from the initial intent switch)
- Entity is not within arrival radius (destination is far away)
- Velocity may be 0 (no crowd movement for unreachable entities)
- After frustration ticks... `FailedBlocked`?

Hmm, but the design says S7 should give `FailedUnreachable`, not `FailedBlocked`. Let me think...

Actually, `PathfindingResultMaterializationSystem` handles the unreachable case. Looking at that system:
```csharp
if (evt.IsReachable) { ... }
// else: what happens?
```

Read the full `PathfindingResultMaterializationSystem` to see what it does when `IsReachable=false`.

### IFakeNavmeshProviderTestApi

Search for this interface definition. It probably has methods like `BlockPolygon`, `BumpVersion`, etc.

---

## Build verification

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-Object -Last 20

cd FDP\Toolkits
dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~Navigation" 2>&1 | Select-Object -Last 15
```

---

## Success criteria

1. `dotnet build IOS-IG-SimHost.sln` = 0 errors
2. Navigation tests >= 259 (existing 259 + new tests)
3. `NavTestHarness.LoadMap(NavTestMaps.LoadCorridor())` builds and disposes without exception
4. At minimum: one test that creates a harness, spawns an entity, and calls `Tick()` without throwing

**Optional but preferred** (if the pipeline works):
- S1 test passes: Infantry reaches destination, `MoveCompletedEvent.Reason == Arrived`
- S7 test passes OR is skipped with `[Fact(Skip = "...")]` explaining why

---

## Report

Write to `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\reports\BATCH-14-REPORT.md`.

Include:
1. Which parts of the pipeline work (harness builds? Tick runs? Events fire?)
2. Issues encountered with event bus timing
3. Which test maps were created
4. Test results (passing count)
5. Any design gaps discovered (e.g., IFakeNavmeshProviderTestApi missing methods)
6. Recommendations for BATCH-15 (next integration scenarios)
