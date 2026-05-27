# BATCH-07 Implementation Instructions

## Covered Tasks
- NAV-P4-T1: Extend MoveToExecutor (RouteHandle passthrough + PathFound case + MoveCompletedEvent emission)
- NAV-P4-T2: Four new executors (PlanRoute, FollowPath, FetchPathDetails, ReleasePath) + remove obsolete FollowRoadGraph executor
- NAV-P4-T3: NavigationPathDetailsUpdateSystem
- NAV-P4-T4: Register navigation events in BuiltInEngineEventCatalog
- NAV-P9-T5: New MoveToExecutorTests rows (DefaultHandle, ExplicitHandle, BTreeInstanceIdBump, MoveCompletedEvent)
- NAV-P9-T6: NavigationPathDetailsUpdateSystemTests (5 rows)

## Build Constraint
Build must succeed with 0 errors. All prior tests (162 - 4 retired = 158 baseline) must still pass,
plus the new tests (approx +17) must also pass. Total expected after batch: approx 175+ passing.

---

## STEP 1 — Delete obsolete files

Delete these two files (they implement/test the [Obsolete] ActionIdFollowRoadGraph=4):
- `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FollowRoadGraphExecutor.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/FollowRoadGraphExecutorTests.cs`

---

## STEP 2 — Add new event structs to PathfindingEvents.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs`

Append the following structs inside the `namespace Fdp.Toolkit.Navigation { }` block, after the
existing `OffMeshTraversalStartedEvent` (EventId 2035). All structs must use `[EventId(N)]` and
`[StructLayout(LayoutKind.Sequential)]`. All must be 16 bytes with explicit padding.

```csharp
    /// <summary>
    /// Fired by <see cref="Executors.MoveToExecutor"/> when a MoveTo command reaches a
    /// terminal state (Arrived, FailedBlocked, FailedUnreachable, NoPath, FailedInvalidHandle).
    /// </summary>
    [EventId(2036)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveCompletedEvent
    {
        /// <summary>The entity whose navigation command completed.</summary>
        public Entity Target;

        /// <summary>Terminal outcome of the navigation command.</summary>
        public NavigationResult Reason;

        // 3 bytes of explicit padding so RouteHandle stays at offset 12.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        /// <summary>Route handle that was active when the command ended; 0 if not applicable.</summary>
        public int RouteHandle;
    }

    /// <summary>
    /// Fired by the progress tracker when a MoveTo is blocked and replanning is attempted.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2037)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveBlockedEvent
    {
        /// <summary>The entity that is blocked.</summary>
        public Entity Target;

        /// <summary>Block reason code (reserved for Phase 5).</summary>
        public byte ReasonCode;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        private int _reserved;
    }

    /// <summary>
    /// Fired by the progress tracker when the agent advances past a waypoint segment.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2038)]
    [StructLayout(LayoutKind.Sequential)]
    public struct WaypointReachedEvent
    {
        /// <summary>The entity that reached the waypoint.</summary>
        public Entity Target;

        /// <summary>Zero-based index of the segment that was just completed.</summary>
        public int SegmentIndex;

        private int _reserved;
    }

    /// <summary>
    /// Fired by the progress tracker when the Muscle layer performs an automatic replan.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2039)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathReplannedEvent
    {
        /// <summary>The entity whose path was replanned.</summary>
        public Entity Target;

        /// <summary>Route handle of the replanned path.</summary>
        public int RouteHandle;

        /// <summary>Running replan count after this replan.</summary>
        public byte ReplanCount;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }

    /// <summary>
    /// Fired when an entity finishes an off-mesh traversal and resumes normal following.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2040)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OffMeshTraversalEndedEvent
    {
        /// <summary>The entity that completed the off-mesh traversal.</summary>
        public Entity Target;

        /// <summary>The kind of traversal that just ended.</summary>
        public TraversalKind Kind;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        private int _reserved;
    }

    /// <summary>
    /// Published by the Muscle-side path-details system when fresh path details are ready
    /// for ingestion into the Brain-side cache. Consumed by
    /// <see cref="Systems.NavigationPathDetailsUpdateSystem"/>.
    /// </summary>
    [EventId(2041)]
    [StructLayout(LayoutKind.Sequential)]
    public struct NavigationPathDetailsResponseEvent
    {
        /// <summary>The Brain entity that requested the path details.</summary>
        public Entity Target;

        /// <summary>Route handle whose details are ready in the Muscle-side registry.</summary>
        public int RouteHandle;

        /// <summary>Replan count at the time the path was snapshotted.</summary>
        public byte ReplanCount;

        /// <summary>1 = triggered automatically by a replan; 0 = explicit FetchPathDetails command.</summary>
        public byte IsAutoRefresh;

        private byte _pad0;
        private byte _pad1;
    }

    /// <summary>
    /// Emitted by <see cref="Systems.NavigationPathDetailsUpdateSystem"/> after the Brain-side
    /// path cache has been populated for a given entity/handle pair.
    /// </summary>
    [EventId(2042)]
    [StructLayout(LayoutKind.Sequential)]
    public struct NavigationPathDetailsArrivedEvent
    {
        /// <summary>The entity whose Brain-side cache was just updated.</summary>
        public Entity Target;

        /// <summary>Route handle that is now cached.</summary>
        public int RouteHandle;

        /// <summary>1 = this was an auto-refresh; 0 = explicit fetch.</summary>
        public byte IsAutoRefresh;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }
```

---

## STEP 3 — Modify MoveToExecutor.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/MoveToExecutor.cs`

### 3a. Restructure the switch in Execute() to:
1. Merge the two Failure case groups (FailedBlocked+FailedUnreachable and NoPath+FailedNoLayer+FailedInvalidHandle) into a single group.
2. Emit `MoveCompletedEvent` on Arrived (Success) and on Failure.
3. Add `PathFound` and `InProgress` as explicit keep-Running cases.

Replace the entire `Execute` method body (from the `switch` statement through the end of the method) with:

```csharp
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            // ── Stale-check: ignore status reports for a different intent ──────────────────────
            if (status.IntentId != intent.IntentId)
                return;   // keep Running; Muscle layer hasn't caught up yet

            // ── Map NavigationResult to channel status ─────────────────────────────────────────
            switch (status.Result)
            {
                case NavigationResult.Arrived:
                    channel.Status = NodeStatus.Success;
                    world.Bus.Publish(new MoveCompletedEvent
                    {
                        Target      = entity,
                        Reason      = NavigationResult.Arrived,
                        RouteHandle = status.RouteHandle,
                    });
                    break;

                case NavigationResult.FailedBlocked:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.NoPath:
                case NavigationResult.FailedNoLayer:
                case NavigationResult.FailedInvalidHandle:
                    channel.Status = NodeStatus.Failure;
                    world.Bus.Publish(new MoveCompletedEvent
                    {
                        Target      = entity,
                        Reason      = status.Result,
                        RouteHandle = status.RouteHandle,
                    });
                    break;

                case NavigationResult.PathFound:
                case NavigationResult.InProgress:
                default:
                    // Keep Running — nothing to do.
                    break;
            }
        }
```

---

## STEP 4 — Create PlanRouteExecutor.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/PlanRouteExecutor.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdPlanRoute"/>.
    /// Issues a pathfinding request without starting movement.
    /// Returns Success when <see cref="NavigationStatus.Result"/> is
    /// <see cref="NavigationResult.PathFound"/>; Failure on <see cref="NavigationResult.NoPath"/>
    /// or other non-recoverable results.
    /// </summary>
    public sealed class PlanRouteExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Increments <see cref="NavigationIntent.IntentId"/> so that the matching
        /// <see cref="NavigationStatus"/> can be identified later, and sets the channel Running.
        /// Mode is left as None because PlanRoute does not start corridor-following.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            PlanRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(PlanRouteParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.None;
            intent.FinalDestination = p.Destination;
            intent.TargetSpeed      = p.Speed;
            intent.ArrivalRadius    = p.ArrivalRadius;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Polls <see cref="NavigationStatus"/> written by the Muscle layer.
        /// <see cref="NavigationResult.PathFound"/> → Success; non-recoverable failures → Failure.
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            if (status.IntentId != intent.IntentId)
                return;   // stale; keep Running

            switch (status.Result)
            {
                case NavigationResult.PathFound:
                    channel.Status = NodeStatus.Success;
                    break;

                case NavigationResult.NoPath:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.FailedNoLayer:
                case NavigationResult.FailedInvalidHandle:
                    channel.Status = NodeStatus.Failure;
                    break;

                case NavigationResult.InProgress:
                default:
                    break;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears <see cref="NavigationIntent.Mode"/> and increments IntentId to cancel any
        /// pending Muscle-side activity.
        /// </summary>
        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
```

---

## STEP 5 — Create FollowPathExecutor.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FollowPathExecutor.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdFollowPath"/>.
    /// Instructs the Muscle layer to follow a pre-loaded route identified by
    /// <see cref="FollowPathParams.RouteHandle"/>.
    /// Returns Success on <see cref="NavigationResult.Arrived"/>; Failure if the handle
    /// is invalid or the path becomes unreachable.
    /// </summary>
    public sealed class FollowPathExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Copies <see cref="FollowPathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent"/> and sets the channel Running.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowPathParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowPathParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            intent.TargetSpeed = p.Speed;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Observes <see cref="NavigationStatus"/>. Returns Success on Arrived;
        /// Failure on FailedInvalidHandle, FailedUnreachable, or NoPath.
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            if (status.IntentId != intent.IntentId)
                return;   // stale; keep Running

            switch (status.Result)
            {
                case NavigationResult.Arrived:
                    channel.Status = NodeStatus.Success;
                    break;

                case NavigationResult.FailedInvalidHandle:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.NoPath:
                case NavigationResult.FailedNoLayer:
                    channel.Status = NodeStatus.Failure;
                    break;

                case NavigationResult.InProgress:
                default:
                    break;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
```

---

## STEP 6 — Create FetchPathDetailsExecutor.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FetchPathDetailsExecutor.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdFetchPathDetails"/>.
    /// When <see cref="FetchPathDetailsParams.NonBlocking"/> is non-zero, returns Success
    /// immediately (fire-and-forget fetch).  Otherwise blocks until the Brain-side path
    /// registry reports the handle as cached via <see cref="IPathRegistry.IsCached"/>.
    /// </summary>
    public sealed class FetchPathDetailsExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly IPathRegistry _pathRegistry;

        /// <param name="pathRegistry">Brain-side path cache polled in blocking mode.</param>
        public FetchPathDetailsExecutor(IPathRegistry pathRegistry)
        {
            _pathRegistry = pathRegistry;
        }

        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FetchPathDetailsParams p;
            fixed (byte* src = channel.Params)
                p = *(FetchPathDetailsParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-reads <see cref="FetchPathDetailsParams"/> every tick to determine blocking mode.
        /// If <see cref="FetchPathDetailsParams.NonBlocking"/> is 1 → Success immediately.
        /// Otherwise polls <see cref="IPathRegistry.IsCached"/> for the active route handle.
        /// </summary>
        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            FetchPathDetailsParams p;
            fixed (byte* src = channel.Params)
                p = *(FetchPathDetailsParams*)src;

            if (p.NonBlocking != 0)
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            var intent = world.GetComponent<NavigationIntent>(entity);
            if (_pathRegistry.IsCached(intent.RouteHandle))
                channel.Status = NodeStatus.Success;
            // otherwise: keep Running
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
```

---

## STEP 7 — Create ReleasePathExecutor.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/ReleasePathExecutor.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdReleasePath"/>.
    /// ReleasePath is a fire-and-forget command: the actual trajectory pool cleanup is
    /// performed by <see cref="Systems.NavigationIntentBridgeSystem"/> reading the
    /// <see cref="LocomotionChannel"/>.  The executor's job is to record the release
    /// intent and immediately signal Success so the BTree can continue.
    /// </summary>
    public sealed class ReleasePathExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the release intent and sets Success immediately.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            ReleasePathParams p;
            fixed (byte* src = channel.Params)
                p = *(ReleasePathParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            world.SetComponent(entity, intent);

            // Fire-and-forget: release is always considered immediately successful.
            channel.Status = NodeStatus.Success;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ReleasePath has no polling logic; the node should never linger in Execute
        /// because OnEnter sets Success.  Defensive pass-through in case the BTree
        /// calls Execute anyway.
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            channel.Status = NodeStatus.Success;
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode     = NavigationMode.None;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
```

---

## STEP 8 — Create NavigationPathDetailsUpdateSystem.cs

File: `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationPathDetailsUpdateSystem.cs`

```csharp
using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.Fake;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads <see cref="NavigationPathDetailsResponseEvent"/>s published by the Muscle-side
    /// path-details system and ingests them into the Brain-side path cache.
    ///
    /// <para>Per event the system:</para>
    /// <list type="number">
    ///   <item>Queries waypoints from <paramref name="muscleRegistry"/>.</item>
    ///   <item>Calls <see cref="BrainPathRegistry.TryIngestResponse"/> to populate the Brain cache.</item>
    ///   <item>Updates <see cref="NavigationPathDetailsBuffer"/> on the target entity.</item>
    ///   <item>Emits <see cref="NavigationPathDetailsArrivedEvent"/> on the bus.</item>
    /// </list>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class NavigationPathDetailsUpdateSystem : IEcsModuleSystem
    {
        private readonly IPathRegistry     _muscleRegistry;
        private readonly BrainPathRegistry _brainRegistry;

        // Scratch buffer for waypoint copy; avoids per-event heap allocation.
        private readonly NavWaypoint[] _waypointScratch;

        /// <param name="muscleRegistry">Source of stored waypoints (Muscle side).</param>
        /// <param name="brainRegistry">Brain-side LRU cache to populate.</param>
        /// <param name="maxWaypointsPerPath">Scratch buffer capacity (default 256).</param>
        public NavigationPathDetailsUpdateSystem(
            IPathRegistry     muscleRegistry,
            BrainPathRegistry brainRegistry,
            int               maxWaypointsPerPath = 256)
        {
            _muscleRegistry  = muscleRegistry;
            _brainRegistry   = brainRegistry;
            _waypointScratch = new NavWaypoint[maxWaypointsPerPath];
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<NavigationPathDetailsResponseEvent>();
            if (events.IsEmpty) return;

            if (view is not EntityRepository repo) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var entity = evt.Target;
                if (!repo.IsAlive(entity)) continue;

                // Get summary for distance/version/backend metadata.
                if (!_muscleRegistry.TryGetSummary(evt.RouteHandle, out var summary))
                    continue;

                // Copy waypoints from Muscle registry into scratch.
                if (!_muscleRegistry.TryGetWaypoints(
                        evt.RouteHandle, _waypointScratch.AsSpan(), out int count))
                    continue;

                var waypoints = new NavWaypoint[count];
                Array.Copy(_waypointScratch, waypoints, count);

                // Ingest into Brain LRU cache.
                _brainRegistry.TryIngestResponse(
                    entity,
                    evt.RouteHandle,
                    waypoints,
                    evt.ReplanCount,
                    summary.TotalDistanceMeters,
                    summary.NavmeshVersionAtPlan,
                    summary.PrimaryBackend);

                // Update NavigationPathDetailsBuffer if the component is present.
                if (repo.IsComponentTypeRegistered<NavigationPathDetailsBuffer>()
                    && repo.HasComponent<NavigationPathDetailsBuffer>(entity))
                {
                    var buf = repo.GetComponent<NavigationPathDetailsBuffer>(entity);
                    buf.RouteHandle        = evt.RouteHandle;
                    buf.ReplanCountAtFetch = (ushort)evt.ReplanCount;
                    buf.WaypointCount      = (ushort)count;
                    buf.TotalDistance      = summary.TotalDistanceMeters;
                    repo.SetComponent(entity, buf);
                }

                // Emit arrived notification.
                repo.Bus.Publish(new NavigationPathDetailsArrivedEvent
                {
                    Target        = entity,
                    RouteHandle   = evt.RouteHandle,
                    IsAutoRefresh = evt.IsAutoRefresh,
                });
            }
        }
    }
}
```

---

## STEP 9 — Update NavigationTestWorldFactory.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs`

Add event registrations for new events at the end of `Create()`, before `return world;`:

```csharp
            // Navigation lifecycle events — required by MoveToExecutor event-emission tests
            // and by NavigationPathDetailsUpdateSystem tests.
            world.RegisterEvent<MoveCompletedEvent>();
            world.RegisterEvent<NavigationPathDetailsResponseEvent>();
            world.RegisterEvent<NavigationPathDetailsArrivedEvent>();
```

Also update the comment on line 23 that still references FollowRoadGraphExecutor:
Change:
```
            // CarKinem navigation state — still used by FollowRouteExecutor,
            // FollowRoadGraphExecutor, and FleeExecutor.
```
To:
```
            // CarKinem navigation state — still used by FollowRouteExecutor and FleeExecutor.
```

---

## STEP 10 — Add new tests to MoveToExecutorTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/MoveToExecutorTests.cs`

Add the following tests BEFORE the closing `}` of the `MoveToExecutorTests` class.
These tests verify RouteHandle passthrough, MoveCompletedEvent emission, and the
BTreeInstanceIdBump abandonment behavior.

The existing `BuildWorld` helper does NOT set `RouteHandle` in `MoveToParams`.
Add a second helper (or overload) that accepts `routeHandle`:

```csharp
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorldWithHandle(Vector2 destination, float arrivalRadius, float speed,
                                 int routeHandle, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;

            unsafe
            {
                var p = new MoveToParams
                {
                    Destination   = destination,
                    ArrivalRadius = arrivalRadius,
                    Speed         = speed,
                    RouteHandle   = routeHandle,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 7: RouteHandle defaults to 0 when not provided ──────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 2: MoveTo_DefaultHandle_IsZero.
        /// When <see cref="MoveToParams.RouteHandle"/> is 0 (fire-and-forget),
        /// <see cref="NavigationIntent.RouteHandle"/> must also be 0 after OnEnter.
        /// </summary>
        [Fact]
        public void MoveToExecutor_OnEnter_DefaultRouteHandle_IsZero()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(0, intent.RouteHandle);
        }

        // ── Test 8: Explicit RouteHandle is passed through ────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 3: MoveTo_ExplicitHandle_PassedThrough.
        /// When <see cref="MoveToParams.RouteHandle"/> is non-zero, the same value must
        /// appear in <see cref="NavigationIntent.RouteHandle"/> after OnEnter.
        /// </summary>
        [Fact]
        public void MoveToExecutor_OnEnter_ExplicitRouteHandle_PassedThrough()
        {
            const int handle = 42;
            var (world, entity, channel) = BuildWorldWithHandle(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f, routeHandle: handle);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(handle, intent.RouteHandle);
        }

        // ── Test 9: Arrived emits MoveCompletedEvent ──────────────────────────────────────────────

        /// <summary>
        /// When <see cref="NavigationStatus.Result"/> is Arrived, <see cref="MoveToExecutor"/>
        /// must publish <see cref="MoveCompletedEvent"/> with Reason=Arrived and the route handle.
        /// </summary>
        [Fact]
        public void MoveToExecutor_Execute_Arrived_EmitsMoveCompletedEvent()
        {
            const int handle = 7;
            var (world, entity, channel) = BuildWorldWithHandle(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 10f,
                routeHandle: handle, existingIntentId: 0);

            var executor = new MoveToExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId    = intent.IntentId,
                Result      = NavigationResult.Arrived,
                RouteHandle = handle,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);

            // Verify the event.
            world.Bus.SwapBuffers();
            var events = world.Bus.Read<MoveCompletedEvent>().ToArray();
            Assert.Single(events);
            Assert.Equal(entity,                   events[0].Target);
            Assert.Equal(NavigationResult.Arrived, events[0].Reason);
            Assert.Equal(handle,                   events[0].RouteHandle);
        }

        // ── Test 10: BTreeInstanceIdBump abandons current move ────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 14: BTreeInstanceIdBump_AbandonsCurrentMove.
        /// After OnExit + new OnEnter (new BTree instance), stale <see cref="NavigationStatus"/>
        /// from the prior command must be ignored and the channel must remain Running.
        /// </summary>
        [Fact]
        public void MoveToExecutor_BTreeInstanceIdBump_AbandonsCurrentMove()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(50f, 0f), arrivalRadius: 5f, speed: 10f, existingIntentId: 0);

            var executor = new MoveToExecutor();

            // First BTree instance: OnEnter -> IntentId = 1.
            executor.OnEnter(entity, ref channel, world);
            var firstIntentId = world.GetComponent<NavigationIntent>(entity).IntentId;

            // Simulate Muscle writing a status for the first instance.
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = firstIntentId,
                Result   = NavigationResult.Arrived,
            });

            // BTree abandons the node: OnExit bumps IntentId to 2.
            executor.OnExit(entity, ref channel, world);

            // Second BTree instance: OnEnter -> IntentId = 3.
            channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            executor.OnEnter(entity, ref channel, world);

            // Status still carries IntentId=1 (stale from first instance).
            // Execute must ignore it and keep Running.
            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Running, channel.Status);
        }
```

---

## STEP 11 — Create PlanRouteExecutorTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/PlanRouteExecutorTests.cs`

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="PlanRouteExecutor"/> (DD-Tests-Nav §4.5, rows 7-9).
    /// </summary>
    public class PlanRouteExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(Vector2 destination, float arrivalRadius, float speed, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdPlanRoute;

            unsafe
            {
                var p = new PlanRouteParams
                {
                    Destination   = destination,
                    ArrivalRadius = arrivalRadius,
                    Speed         = speed,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent ────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 7: PlanRoute_WritesNavigationIntent_ActiveActionPlanRoute.
        /// OnEnter must increment IntentId, set Mode=None, copy Destination, and set Running.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_OnEnter_WritesNavigationIntent()
        {
            var destination = new Vector2(500f, 200f);
            var (world, entity, channel) = BuildWorld(destination, arrivalRadius: 10f, speed: 0f,
                                                      existingIntentId: 3);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);

            Assert.Equal(4u,                        intent.IntentId);
            Assert.Equal(NavigationMode.None,       intent.Mode);
            Assert.Equal(destination,               intent.FinalDestination);
            Assert.Equal(NodeStatus.Running,        channel.Status);
        }

        // ── Test 2: PathFound → Success ────────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 8: PlanRoute_StatusPathFound_ReturnsBTreeSuccess.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_Execute_PathFound_ReturnsSuccess()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 0f);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.PathFound,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3: NoPath → Failure ────────────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 9: PlanRoute_StatusNoPath_ReturnsBTreeFailure.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_Execute_NoPath_ReturnsFailure()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 0f);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.NoPath,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}
```

---

## STEP 12 — Create FollowPathExecutorTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/FollowPathExecutorTests.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FollowPathExecutor"/> (DD-Tests-Nav §4.5, row 10).
    /// </summary>
    public class FollowPathExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, float speed = 10f, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFollowPath;

            unsafe
            {
                var p = new FollowPathParams
                {
                    RouteHandle = routeHandle,
                    Speed       = speed,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent with the provided handle ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 10: FollowPath_WritesNavigationIntent_WithProvidedHandle.
        /// OnEnter must copy <see cref="FollowPathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent.RouteHandle"/> and set channel to Running.
        /// </summary>
        [Fact]
        public void FollowPathExecutor_OnEnter_WritesIntentWithHandle()
        {
            const int handle = 99;
            var (world, entity, channel) = BuildWorld(handle);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(handle,            intent.RouteHandle);
            Assert.Equal(NodeStatus.Running, channel.Status);
        }

        // ── Test 2: Arrived → Success ─────────────────────────────────────────────────────────────

        [Fact]
        public void FollowPathExecutor_Execute_Arrived_ReturnsSuccess()
        {
            var (world, entity, channel) = BuildWorld(routeHandle: 5);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.Arrived,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3: FailedInvalidHandle → Failure ─────────────────────────────────────────────────

        [Fact]
        public void FollowPathExecutor_Execute_FailedInvalidHandle_ReturnsFailure()
        {
            var (world, entity, channel) = BuildWorld(routeHandle: 0);

            var executor = new FollowPathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.FailedInvalidHandle,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}
```

---

## STEP 13 — Create FetchPathDetailsExecutorTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/FetchPathDetailsExecutorTests.cs`

The `FetchPathDetailsExecutor` requires an `IPathRegistry`. Use `MusclePathRegistry` (which
implements `IPathRegistry`) as the concrete registry in tests.

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FetchPathDetailsExecutor"/> (DD-Tests-Nav §4.5, rows 11-12).
    /// </summary>
    public class FetchPathDetailsExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, byte nonBlocking, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdFetchPathDetails;

            unsafe
            {
                var p = new FetchPathDetailsParams
                {
                    RouteHandle  = routeHandle,
                    NonBlocking  = nonBlocking,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: Blocking mode polls registry until cached ─────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 11: FetchPathDetails_Blocking_PollsRegistryUntilCached.
        /// With NonBlocking=0, Execute must keep Running until <see cref="IPathRegistry.IsCached"/>
        /// returns true for the route handle.
        /// </summary>
        [Fact]
        public void FetchPathDetailsExecutor_Blocking_PollsRegistryUntilCached()
        {
            const int routeHandle = 17;
            var registry = new MusclePathRegistry();

            var (world, entity, channel) = BuildWorld(routeHandle, nonBlocking: 0);
            var executor = new FetchPathDetailsExecutor(registry);
            executor.OnEnter(entity, ref channel, world);

            // First Execute: not cached yet → Running.
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Running, channel.Status);

            // Store path in registry (simulates Muscle-side path result materialisation).
            registry.StoreOrReplace(routeHandle, new[]
            {
                new NavWaypoint { Position = System.Numerics.Vector3.Zero },
            });

            // Second Execute: now cached → Success.
            executor.Execute(entity, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2: Non-blocking mode returns Success immediately ──────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 12: FetchPathDetails_NonBlocking_ReturnsImmediatelySuccess.
        /// With NonBlocking=1, Execute must return Success on the first call regardless of
        /// whether the path is cached.
        /// </summary>
        [Fact]
        public void FetchPathDetailsExecutor_NonBlocking_ReturnsSuccessImmediately()
        {
            const int routeHandle = 21;
            var registry = new MusclePathRegistry(); // empty — path NOT cached

            var (world, entity, channel) = BuildWorld(routeHandle, nonBlocking: 1);
            var executor = new FetchPathDetailsExecutor(registry);
            executor.OnEnter(entity, ref channel, world);

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }
    }
}
```

---

## STEP 14 — Create ReleasePathExecutorTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/ExecutorTests/ReleasePathExecutorTests.cs`

```csharp
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="ReleasePathExecutor"/> (DD-Tests-Nav §4.5, row 13).
    /// </summary>
    public class ReleasePathExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(int routeHandle, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdReleasePath;

            unsafe
            {
                var p = new ReleasePathParams { RouteHandle = routeHandle };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent and succeeds immediately ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 13: ReleasePath_WritesNavigationIntent_ActiveActionReleasePath.
        /// OnEnter must copy <see cref="ReleasePathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent.RouteHandle"/> and set channel to Success immediately.
        /// </summary>
        [Fact]
        public void ReleasePathExecutor_OnEnter_WritesIntentAndSucceeds()
        {
            const int handle = 33;
            var (world, entity, channel) = BuildWorld(handle, existingIntentId: 0);

            var executor = new ReleasePathExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(handle,            intent.RouteHandle);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }
    }
}
```

---

## STEP 15 — Create NavigationPathDetailsUpdateSystemTests.cs

File: `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationPathDetailsUpdateSystemTests.cs`

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavigationPathDetailsUpdateSystem"/> (DD-Tests-Nav §4.6, 5 rows).
    /// </summary>
    public class NavigationPathDetailsUpdateSystemTests
    {
        private const int RouteHandle = 55;

        private static (EntityRepository world, MusclePathRegistry muscleRegistry,
                        BrainPathRegistry brainRegistry, Entity entity)
            CreateWorld(int brainMaxEntries = 32)
        {
            var world = new EntityRepository();

            world.RegisterEvent<NavigationPathDetailsResponseEvent>();
            world.RegisterEvent<NavigationPathDetailsArrivedEvent>();

            world.RegisterComponent<NavigationPathDetailsBuffer>();

            var muscleRegistry = new MusclePathRegistry();
            var brainRegistry  = new BrainPathRegistry(brainMaxEntries);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new NavigationPathDetailsBuffer());

            return (world, muscleRegistry, brainRegistry, entity);
        }

        private static void PublishResponseEvent(EntityRepository world, Entity entity,
                                                  int routeHandle,
                                                  byte replanCount   = 0,
                                                  byte isAutoRefresh = 0)
        {
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target        = entity,
                RouteHandle   = routeHandle,
                ReplanCount   = replanCount,
                IsAutoRefresh = isAutoRefresh,
            });
            world.Bus.SwapBuffers();
        }

        // ── Test 1: Response event populates Brain path registry ─────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 1: ResponseEventArrives_PopulatesBrainPathRegistry.
        /// After the system processes a response event, <see cref="BrainPathRegistry.IsCached"/>
        /// must return true for the route handle.
        /// </summary>
        [Fact]
        public void ResponseEvent_PopulatesBrainPathRegistry()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero,       Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = new Vector3(10, 0, 0), Traversal = TraversalKind.Walk },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            PublishResponseEvent(world, entity, RouteHandle);
            system.Execute(world, 0.016f);

            Assert.True(brainRegistry.IsCached(RouteHandle));
        }

        // ── Test 2: Response event fires NavigationPathDetailsArrivedEvent ───────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 2: ResponseEventArrives_FiresArrivedEvent.
        /// The system must emit exactly one <see cref="NavigationPathDetailsArrivedEvent"/>
        /// on the bus after processing.
        /// </summary>
        [Fact]
        public void ResponseEvent_FiresArrivedEvent()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            PublishResponseEvent(world, entity, RouteHandle);
            system.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<NavigationPathDetailsArrivedEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(entity,      events[0].Target);
            Assert.Equal(RouteHandle, events[0].RouteHandle);
        }

        // ── Test 3: IsAutoRefresh flag preserved in arrived event ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 3: ResponseEvent_IsAutoRefresh_PreservesFlag.
        /// When the response event carries IsAutoRefresh=1, the arrived event must echo the flag.
        /// </summary>
        [Fact]
        public void ResponseEvent_IsAutoRefresh_PreservedInArrivedEvent()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target        = entity,
                RouteHandle   = RouteHandle,
                ReplanCount   = 0,
                IsAutoRefresh = 1,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<NavigationPathDetailsArrivedEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal((byte)1, events[0].IsAutoRefresh);
        }

        // ── Test 4: ReplanCount updated in Brain registry ────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 4: ResponseEventReceived_LastObservedReplanCountUpdated.
        /// After processing, the Brain cache entry must carry the replan count from the event.
        /// </summary>
        [Fact]
        public void ResponseEvent_UpdatesReplanCountInBrainRegistry()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();
            const byte replanCount = 3;

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = RouteHandle,
                ReplanCount = replanCount,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            var entries = ((IFakeBrainPathRegistryTestApi)brainRegistry).SnapshotEntityCache(entity);
            Assert.Single(entries);
            Assert.Equal(replanCount, entries[0].LastObservedReplanCount);
        }

        // ── Test 5: LRU cap evicts oldest entry ──────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 5: LruCapExceeded_OldestEvicted.
        /// When the Brain registry is at capacity (maxEntries=1) and a second response arrives,
        /// the first entry must be evicted to make room for the new one.
        /// </summary>
        [Fact]
        public void ResponseEvent_LruCapExceeded_OldestEvicted()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld(brainMaxEntries: 1);
            const int handle1 = 100;
            const int handle2 = 200;

            muscleRegistry.StoreOrReplace(handle1, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });
            muscleRegistry.StoreOrReplace(handle2, new[]
            {
                new NavWaypoint { Position = Vector3.One },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            // Ingest first handle.
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = handle1,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            Assert.True(brainRegistry.IsCached(handle1));

            // Ingest second handle — should evict handle1 (LRU cap = 1).
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = handle2,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            Assert.False(brainRegistry.IsCached(handle1), "handle1 should have been evicted");
            Assert.True(brainRegistry.IsCached(handle2),  "handle2 should be cached");
        }
    }
}
```

---

## STEP 16 — Add navigation events to BuiltInEngineEventCatalog.cs

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInEngineEventCatalog.cs`

### 16a. Add a file-scoped helper constant for Navigation FQN prefix

After the closing `}` of `file static class AnimFqn`, add:

```csharp
// Navigation event type FQN prefix (Fdp.Toolkit.Navigation namespace).
file static class NavFqn
{
    private const string Ns = "Fdp.Toolkit.Navigation";
    public static string Of(string typeName) => $"{Ns}.{typeName}";
}
```

### 16b. Add navigation event entries at the end of the `GetEntries()` list

Append the following entries before the closing `};` of the list:

```csharp
            // ---- Navigation lifecycle events (NAV-P4 §4.5, §5) ------------------
            // All propagate across nodes (Brain-visible) unless noted.

            new(Name:                "MoveStartedEvent",
                EventTypeFqn:        NavFqn.Of("MoveStartedEvent"),
                DisplayName:         "Move Started",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     null,
                FilterableFields:    new[] { "RouteHandle" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MoveCompletedEvent",
                EventTypeFqn:        NavFqn.Of("MoveCompletedEvent"),
                DisplayName:         "Move Completed",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "Reason", "RouteHandle" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "PathReplannedEvent",
                EventTypeFqn:        NavFqn.Of("PathReplannedEvent"),
                DisplayName:         "Path Replanned",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "RouteHandle", "ReplanCount" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "OffMeshTraversalStartedEvent",
                EventTypeFqn:        NavFqn.Of("OffMeshTraversalStartedEvent"),
                DisplayName:         "Off-Mesh Traversal Started",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "TraversalKind" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "OffMeshTraversalEndedEvent",
                EventTypeFqn:        NavFqn.Of("OffMeshTraversalEndedEvent"),
                DisplayName:         "Off-Mesh Traversal Ended",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "Kind" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            new(Name:                "MoveBlockedEvent",
                EventTypeFqn:        NavFqn.Of("MoveBlockedEvent"),
                DisplayName:         "Move Blocked",
                Category:            "Navigation/Lifecycle",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "ReasonCode" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),

            // WaypointReachedEvent: Muscle-local only (progress tracking). Brain Blueprints
            // should not subscribe to individual waypoint events due to high frequency.
            new(Name:                "WaypointReachedEvent",
                EventTypeFqn:        NavFqn.Of("WaypointReachedEvent"),
                DisplayName:         "Waypoint Reached",
                Category:            "Navigation/Progress",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "SegmentIndex" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: false),

            new(Name:                "NavigationPathDetailsArrivedEvent",
                EventTypeFqn:        NavFqn.Of("NavigationPathDetailsArrivedEvent"),
                DisplayName:         "Navigation Path Details Arrived",
                Category:            "Navigation/PathDetails",
                TargetFieldName:     "Target",
                FilterableFields:    new[] { "RouteHandle", "IsAutoRefresh" },
                QoS:                 EventQoS.Reliable,
                PropagatesAcrossNodes: true),
```

---

## Build and Test Verification

After all changes:

1. Build the full solution:
   ```
   dotnet build FDP/Toolkits/Fdp.Toolkits.sln --configuration Debug 2>&1 | Select-Object -Last 10
   dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/ 2>&1 | Select-Object -Last 10
   ```
   Must show **0 errors**.

2. Run the navigation test suite:
   ```
   dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "Navigation" --no-build 2>&1 | Select-Object -Last 15
   ```
   Expected: **all tests pass**. Approx 175+ total (162 baseline - 4 FollowRoadGraph retired + ~17 new).

---

## Key Implementation Constraints

- All production code lives in `FDP/Toolkits/Fdp.Toolkits/Navigation/`
- All test code lives in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`
- Do NOT add using directives for `Hrot.MuscleCharacter.Animation` (assembly boundary)
- Event struct sizes must be 16 bytes with explicit padding (no implicit compiler padding)
- Comments must NOT be deleted, rewritten, or reformatted unless they reference the deleted class
- Do NOT rename any existing symbols
- The `BrainPathRegistry` type is in namespace `Fdp.Toolkit.Navigation.Fake` — use the full type
  not the interface when the `NavigationPathDetailsUpdateSystem` needs to call `TryIngestResponse`
- `NavigationPathDetailsBuffer.ReplanCountAtFetch` is a `ushort` field — cast `(ushort)evt.ReplanCount`
- `NavigationPathDetailsBuffer.WaypointCount` is a `ushort` field — cast `(ushort)count`
