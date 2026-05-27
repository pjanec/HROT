# BATCH-16 Implementation Instructions

## Objective
Implement NAV-P10 integration tests T6 (S5 + S5b), T7 (S6), and T9 (S8).  
**Target**: ≥ 279 passing tests (268 existing + 4 new tests + possible extras).

## Tasks

| Task ID | Description |
|---------|-------------|
| NAV-P10-T6 | `S5_ReplanOnNavmeshPatch` + `S5b_ReplanWithAutoRefresh` |
| NAV-P10-T7 | `S6_CrowdAvoidance` |
| NAV-P10-T9 | `S8_FrustrationWatchdog` |

---

## Files to Modify

### 1. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs`

Add `UnblockPolygon` to the `IFakeNavmeshProviderTestApi` interface:

```csharp
/// <summary>
/// Marks a polygon as unblocked (walkable) across all layers and bumps the version.
/// Returns false if no polygon with that ID is found.
/// </summary>
bool UnblockPolygon(int polygonId);
```

Add implementation in `FakeNavmeshProvider` (next to the `BlockPolygon` method):

```csharp
/// <inheritdoc/>
public bool UnblockPolygon(int polygonId)
{
    bool found = false;
    foreach (var layer in _layers)
    {
        foreach (var poly in layer.Polygons)
        {
            if (poly.Id == polygonId)
            {
                poly.IsBlocked = false;
                layer.Version++;
                found = true;
            }
        }
    }
    return found;
}
```

---

### 2. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/NavTestMaps.cs`

Replace the existing `LoadReplan()` method with this expanded version that adds an alternate bypass polygon (polygon 3):

```csharp
/// <summary>
/// Four-polygon map: three in a corridor (0→1→2) plus one bypass polygon (3) connecting
/// polygon 0 and polygon 2 north of the main route.  Polygon 1 is pre-blocked so initial
/// path queries use the bypass (3).  Tests that need the main route call
/// <c>NavmeshApi.UnblockPolygon(1)</c> first.
/// Infantry layer.
/// </summary>
public static NavTestMap LoadReplan()
{
    var map = new NavTestMapBuilder()
        .Layer(NavLayerMask.Infantry, b => b
            .Polygon(0, Square(5f,  5f))
            .Polygon(1, Square(15f, 5f))   // main route; blockable mid-test
            .Polygon(2, Square(25f, 5f))
            .Polygon(3, Square(15f, 15f))  // alternate bypass north of polygon 1
            .Adjacent(0, 1)
            .Adjacent(1, 2)
            .Adjacent(0, 3)
            .Adjacent(3, 2))
        .Build();
    // Pre-block the middle polygon so the initial path query returns results via polygon 3.
    // Tests that need the main path first call NavmeshApi.UnblockPolygon(1).
    map.Layers[0].Polygons[1].IsBlocked = true;
    return map;
}
```

**Polygon layout (XZ plane)**:
- Polygon 0: X=0..10, Z=0..10 (start zone)
- Polygon 1: X=10..20, Z=0..10 (main route — blockable)
- Polygon 2: X=20..30, Z=0..10 (end zone)
- Polygon 3: X=10..20, Z=10..20 (alternate bypass — never pre-blocked)

Route when polygon 1 unblocked: 0→1→2 (direct)  
Route when polygon 1 blocked: 0→3→2 (alternate via north)

---

### 3. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavTestHarness.cs`

#### 3a. Add public list accessors to `CapturedEventLog`

In the `CapturedEventLog` class, replace the existing private-field-only design with public accessors and add `NavigationPathDetailsResponseEvent` support.

Add after the existing private fields:

```csharp
private readonly List<NavigationPathDetailsResponseEvent> _pathDetailsResponses = new();
```

Add public read-only list properties (add after the `Clear()` method or at the end of the class, before the last `}`):

```csharp
public IReadOnlyList<MoveStartedEvent>                       MoveStarted          => _started;
public IReadOnlyList<MoveCompletedEvent>                     MoveCompleted        => _completed;
public IReadOnlyList<PathReplannedEvent>                     PathReplanned        => _replanned;
public IReadOnlyList<MoveBlockedEvent>                       MoveBlocked          => _blocked;
public IReadOnlyList<OffMeshTraversalStartedEvent>           OffMeshStarted       => _offMeshStarted;
public IReadOnlyList<NavigationPathDetailsResponseEvent>     PathDetailsResponses => _pathDetailsResponses;
```

#### 3b. Update `CapturedEventLog.Capture` to read `NavigationPathDetailsResponseEvent`

In `Capture(EntityRepository repo)`, add after the existing `ReadEvents` loops:

```csharp
foreach (ref readonly var e in view.ReadEvents<NavigationPathDetailsResponseEvent>())
    _pathDetailsResponses.Add(e);
```

#### 3c. Update `CapturedEventLog.Clear` to clear the new list

In `Clear()`, add:

```csharp
_pathDetailsResponses.Clear();
```

**Important**: Do NOT remove the existing `HasMoveCompleted`, `GetMoveCompleted`, `HasOffMeshTraversalStarted`, `GetFirstOffMeshTraversalStarted` helper methods — they must remain for backward compatibility.

---

## Files to Create

### 4. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S5_ReplanOnNavmeshPatchTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T6 (S5). Muscle-internal replan triggered by frustration after a navmesh patch.
    /// Proves: PathReplannedEvent fires, ReplanCount > 0, entity arrives via alternate route.
    /// </summary>
    public sealed class S5_ReplanOnNavmeshPatchTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S5_ReplanOnNavmeshPatchTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S5_AgentReroutes_ViaAlternate_AndArrives()
        {
            // LoadReplan pre-blocks polygon 1. Unblock so initial path goes 0→1→2.
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));
            _h.IssueMoveTo(e, new Vector2(28f, 0f),
                flags: (byte)(1 << NavigationConstants.FlagBitAllowReplan));

            // Pump for 15 ticks: bridge → solver → materialize (path resolves).
            _h.PumpFor(15);

            // Block polygon 1 to cut the main route.
            _h.NavmeshApi.BlockPolygon(1);

            // Override crowd velocity to zero so frustration accumulates.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);

            // Pump FrustrationTickLimit + 5 ticks → PathReplannedEvent fires.
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            Assert.True(_h.EventLog.PathReplanned.Count > 0,
                "PathReplannedEvent must fire after FrustrationTickLimit ticks with AllowReplan set.");

            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.True(status.ReplanCount > 0, "NavigationStatus.ReplanCount must be > 0 after replan.");

            // Restore velocity — crowd drives entity to destination.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).ClearAgentVelocityOverride(e);

            // Pump until arrival (alternate path 0→3→2 takes a few extra ticks).
            _h.PumpUntil(() => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 1000,
                failMessage: "Entity did not arrive within 1000 ticks after replan.");

            var completed = _h.EventLog.MoveCompleted.First(c => c.Target == e);
            Assert.Equal(NavigationResult.Arrived, completed.Reason);

            var tf = _h.Repo.GetComponent<SimTransform>(e);
            float dist = Vector2.Distance(
                new Vector2(tf.Position.X, tf.Position.Y),
                new Vector2(28f, 0f));
            Assert.True(dist <= 2.0f,
                $"Final position should be within 2 m of destination; actual dist={dist:F2}");

            // NavigationStatus must NOT have reached FailedBlocked.
            Assert.NotEqual(NavigationResult.FailedBlocked, status.Result);
        }
    }
}
```

---

### 5. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S5b_ReplanWithAutoRefreshTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T6 (S5b). Auto-refresh side of the replan scenario.
    /// With <c>FlagBitAutoSendPathOnReplan</c> set, a Muscle-internal replan additionally
    /// fires <see cref="NavigationPathDetailsResponseEvent"/> with <c>IsAutoRefresh = 1</c>.
    /// Without the flag (sibling control), no such event fires.
    /// </summary>
    public sealed class S5b_ReplanWithAutoRefreshTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S5b_ReplanWithAutoRefreshTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadReplan());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S5b_AutoSendPathOnReplan_FiresPathDetailsResponseEvent_IsAutoRefresh()
        {
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));

            // AllowReplan + AutoSendPathOnReplan flags.
            byte flags = (byte)(
                (1 << NavigationConstants.FlagBitAllowReplan) |
                (1 << NavigationConstants.FlagBitAutoSendPathOnReplan));

            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: 1);

            _h.PumpFor(15);

            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // NavigationPathDetailsResponseEvent with IsAutoRefresh=1 must fire.
            Assert.True(_h.EventLog.PathDetailsResponses.Count > 0,
                "NavigationPathDetailsResponseEvent must fire when AutoSendPathOnReplan is set.");

            var resp = _h.EventLog.PathDetailsResponses[0];
            Assert.Equal(1, resp.IsAutoRefresh);
            Assert.Equal(1, resp.RouteHandle);

            // PathReplannedEvent must also fire.
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            // Entity arrives after velocity is restored.
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).ClearAgentVelocityOverride(e);
            _h.PumpUntil(() => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: 1000,
                failMessage: "Entity did not arrive within 1000 ticks.");
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);
        }

        [Fact]
        public void S5b_WithoutAutoSendFlag_NoPathDetailsResponseFired()
        {
            // Sibling control: same setup but WITHOUT AutoSendPathOnReplan flag.
            _h.NavmeshApi.UnblockPolygon(1);

            var e = _h.SpawnInfantry(new Vector2(3f, 0f));

            // AllowReplan only — no AutoSendPathOnReplan.
            byte flags = (byte)(1 << NavigationConstants.FlagBitAllowReplan);
            _h.IssueMoveTo(e, new Vector2(28f, 0f), flags: flags, routeHandle: 2);

            _h.PumpFor(15);
            _h.NavmeshApi.BlockPolygon(1);
            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);
            _h.PumpFor(NavigationExecutionSystem.FrustrationTickLimit + 5);

            // Replan should have fired…
            Assert.True(_h.EventLog.PathReplanned.Count > 0, "PathReplannedEvent must fire.");

            // …but NO PathDetailsResponseEvent (flag not set).
            Assert.Equal(0, _h.EventLog.PathDetailsResponses.Count);
        }
    }
}
```

---

### 6. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S6_CrowdAvoidanceTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T7 (S6). Four infantry entities with crossing paths; all must arrive.
    /// Proves FakeDtCrowdProvider separation forces prevent permanent deadlocks.
    /// </summary>
    public sealed class S6_CrowdAvoidanceTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S6_CrowdAvoidanceTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadCrowded());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S6_FourCrossingAgents_AllArrive()
        {
            // Four agents with diagonally-crossing paths through the corridor.
            // A + B move right; C + D move left. Paths cross near the centre.
            var eA = _h.SpawnInfantry(new Vector2(2f,  0f));
            var eB = _h.SpawnInfantry(new Vector2(2f,  6f));
            var eC = _h.SpawnInfantry(new Vector2(58f, 0f));
            var eD = _h.SpawnInfantry(new Vector2(58f, 6f));

            _h.IssueMoveTo(eA, new Vector2(58f, 6f));
            _h.IssueMoveTo(eB, new Vector2(58f, 0f));
            _h.IssueMoveTo(eC, new Vector2(2f,  6f));
            _h.IssueMoveTo(eD, new Vector2(2f,  0f));

            bool AllArrived() =>
                _h.EventLog.MoveCompleted.Any(c => c.Target == eA) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eB) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eC) &&
                _h.EventLog.MoveCompleted.Any(c => c.Target == eD);

            _h.PumpUntil(AllArrived, maxTicks: 2000,
                failMessage: "Not all four crowd agents arrived within 2000 ticks.");

            // Verify all four reached their destinations (not failed).
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eA).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eB).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eC).Reason);
            Assert.Equal(NavigationResult.Arrived,
                _h.EventLog.MoveCompleted.First(c => c.Target == eD).Reason);
        }
    }
}
```

---

### 7. `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/Integration/S8_FrustrationWatchdogTests.cs`

```csharp
using System;
using System.Linq;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.Integration
{
    /// <summary>
    /// NAV-P10-T9 (S8). Frustration watchdog: agents stuck at zero velocity exhaust their
    /// replan budget and surface <see cref="NavigationResult.FailedBlocked"/> to Brain.
    /// </summary>
    public sealed class S8_FrustrationWatchdogTests : IDisposable
    {
        private readonly NavTestHarness _h;

        public S8_FrustrationWatchdogTests()
        {
            _h = new NavTestHarness(NavTestMaps.LoadFrustration());
        }

        public void Dispose() => _h.Dispose();

        [Fact]
        public void S8_AgentsStuck_FailedBlocked_After_ReplanBudgetExhausted()
        {
            var dest  = new Vector2(35f, 0f); // polygon 3 centre
            byte flags = (byte)(1 << NavigationConstants.FlagBitAllowReplan);

            var e1 = _h.SpawnInfantry(new Vector2(5f, 0f));
            var e2 = _h.SpawnInfantry(new Vector2(5f, 0f));
            var e3 = _h.SpawnInfantry(new Vector2(5f, 0f));

            _h.IssueMoveTo(e1, dest, flags: flags);
            _h.IssueMoveTo(e2, dest, flags: flags);
            _h.IssueMoveTo(e3, dest, flags: flags);

            // Set MaxReplans = 1 so the budget exhausts after 2 × FrustrationTickLimit ticks.
            ref var intent1 = ref _h.Repo.GetComponentRW<NavigationIntent>(e1);
            intent1.MaxReplans = 1;
            ref var intent2 = ref _h.Repo.GetComponentRW<NavigationIntent>(e2);
            intent2.MaxReplans = 1;
            ref var intent3 = ref _h.Repo.GetComponentRW<NavigationIntent>(e3);
            intent3.MaxReplans = 1;

            // Zero velocity → frustration accumulates deterministically.
            var crowdApi = (IFakeDtCrowdProviderTestApi)_h.Crowd;
            crowdApi.OverrideAgentVelocity(e1, Vector3.Zero);
            crowdApi.OverrideAgentVelocity(e2, Vector3.Zero);
            crowdApi.OverrideAgentVelocity(e3, Vector3.Zero);

            // Wait until at least one agent surfaces FailedBlocked.
            // With FrustrationTickLimit=120 and MaxReplans=1: fails at ~242 ticks.
            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Reason == NavigationResult.FailedBlocked),
                maxTicks: 400,
                failMessage: "Expected FailedBlocked within 400 ticks (2 × FrustrationTickLimit).");

            // MoveBlockedEvent must have fired at least once (throttled per episode).
            Assert.True(_h.EventLog.MoveBlocked.Count > 0,
                "MoveBlockedEvent must fire when the Muscle-internal replan is triggered.");

            // At least one FailedBlocked.
            Assert.Contains(_h.EventLog.MoveCompleted,
                c => c.Reason == NavigationResult.FailedBlocked);

            // The failing entity's ReplanCount should be >= 1 (it tried replan first).
            var failedTarget = _h.EventLog.MoveCompleted
                .First(c => c.Reason == NavigationResult.FailedBlocked).Target;
            var failedStatus = _h.Repo.GetComponent<NavigationStatus>(failedTarget);
            Assert.True(failedStatus.ReplanCount >= 1,
                "Muscle must have attempted at least one replan before hard-failing.");
        }

        [Fact]
        public void S8_WithoutAllowReplan_FailedBlocked_Immediately_AfterOneFrustrationEpisode()
        {
            var dest = new Vector2(35f, 0f);
            // No AllowReplan flag → first frustration episode → FailedBlocked (no replan).
            var e = _h.SpawnInfantry(new Vector2(5f, 0f));
            _h.IssueMoveTo(e, dest, flags: 0);

            ((IFakeDtCrowdProviderTestApi)_h.Crowd).OverrideAgentVelocity(e, Vector3.Zero);

            _h.PumpUntil(
                () => _h.EventLog.MoveCompleted.Any(c => c.Target == e),
                maxTicks: NavigationExecutionSystem.FrustrationTickLimit + 10,
                failMessage: "Entity must reach FailedBlocked within FrustrationTickLimit + 10 ticks.");

            Assert.Equal(NavigationResult.FailedBlocked,
                _h.EventLog.MoveCompleted.First(c => c.Target == e).Reason);

            // Without AllowReplan, NO MoveBlockedEvent fires (that's only for replan path).
            Assert.Equal(0, _h.EventLog.MoveBlocked.Count);

            // ReplanCount must remain 0.
            var status = _h.Repo.GetComponent<NavigationStatus>(e);
            Assert.Equal(0, status.ReplanCount);
        }
    }
}
```

---

## Key Architectural Notes

### Coordinate system
- `Vector2(x, y)` in harness → `Vector3(x, y, 0)` in ECS.
- `FakeNavmeshProvider.PlanPath` uses `PointInPolygon(pos.X, pos.Z, poly)` — the Z=0 bottom edge is inside polygons (winding-number boundary-inclusive for upward crossings).
- Entity position update: `CrowdAgentUpdateSystem` integrates `tf.Position += velocity * dt` on every tick for `CrowdAgent`-tagged entities.
- Arrival check in `NavigationExecutionSystem`: `Vector2.Distance(new Vector2(pos.X, pos.Y), intent.FinalDestination)`.

### `LoadReplan` polygon 3 position
- Polygon 3: `Square(15f, 15f)` = X=10..20, Z=10..20.
- `PointInPolygon(X=15, Z=0)` — Z=0 is NOT in polygon 3 (Z range 10..20). So positions at Z=0 cannot enter polygon 3 via `PlanPath(start)`.
- BUT polygon 3 is used as an INTERMEDIATE waypoint only. `Dijkstra` traverses 0→3→2 and inserts centroid of polygon 3 (15, 0, 15) as an intermediate waypoint. The entity doesn't "enter" polygon 3 physically — the path just passes through the centroid.
- After replan, `TrajectoryPoolManager` stores the new path: [start, centroid(poly3), end]. `SyncSolverTrajectoriesIntoPathRegistry()` syncs this. The entity still moves crowd-driven toward its original destination in the `FakeDtCrowdProvider`.

### Frustration + replan interaction
`NavigationExecutionSystem` replan trigger (when `AllowReplan=true`):
1. `vel.Linear.Length() < FrustrationSpeedThreshold (0.2)` for > `FrustrationTickLimit (120)` ticks.
2. `status.ReplanCount < effectiveMax` (MaxReplans=0 → DefaultMaxReplans=3; MaxReplans=1 → 1).
3. Publishes `PathfindingRequestEvent` (re-routes) + `PathReplannedEvent` + optionally `NavigationPathDetailsResponseEvent` (if AutoSendPathOnReplan flag set) + `MoveBlockedEvent` (throttled).
4. Resets `frustration.Ticks = 0`.

On the NEXT tick, solver processes the new `PathfindingRequestEvent` (swapped in before solver runs). After 2 more ticks, `PathfindingResultMaterializationSystem` updates `NavigationCorridorMuscle`.

### `OverrideAgentVelocity` usage
`IFakeDtCrowdProviderTestApi.OverrideAgentVelocity(entity, Vector3.Zero)` bypasses the crowd's steering calculation. `CrowdAgentUpdateSystem` reads this zero velocity and writes it to `SimVelocity.Linear`. `NavigationExecutionSystem` sees `vel.Linear.Length() = 0 < 0.2` and increments `FrustrationTicks`.

### `PumpUntil` signature
Existing `PumpUntil(Func<bool> condition, int maxTicks = 600)` — use the existing method. 
If it doesn't have a `failMessage` parameter, remove it from the test calls or add the overload to `NavTestHarness`. CHECK the current signature before writing tests.

**IMPORTANT**: Read the actual `PumpUntil` signature in `NavTestHarness.cs` before writing. If it doesn't accept `failMessage`, remove that parameter from the test calls.

---

## Build & Test Verification

After implementing, run:
```
cd D:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits
dotnet build Fdp.Toolkits.sln
dotnet test Fdp.Toolkits.Tests --filter "FullyQualifiedName~Navigation"
```

Expected: ≥ 279 tests passing, 0 errors.

## Reporting

Create `d:\Work\IOS-IG-SimHost-FDP-2\.dev\navig-2\batches\BATCH-16-REPORT.md` with:
- Summary of changes made
- Final test count
- Any deviations from instructions
