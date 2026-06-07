# BATCH-06 Instructions — Phase 3: CrowdAgent Admission + OffMeshLinkDetectionSystem

**Batch ID:** BATCH-06
**Phase:** 3 (Crowd, off-mesh traversal)
**Tasks:** NAV-P3-T1 + NAV-P3-T2 + NAV-P9-T1 + NAV-P9-T2
**Depends on:** BATCH-05 committed

**Design references:**
- Navigation_Design_v2_0.md §7.2, §7.2.1, §7.2.2
- DD-Tests-Nav.md §4.1, §4.2
- TASK-DETAILS.md NAV-P3-T1, NAV-P3-T2

---

## Critical rules (read before touching any file)

1. **No new assemblies** — all production code goes in `FDP/Toolkits/Fdp.Toolkits/` under
   `Fdp.Toolkit.Navigation` (or sub-namespace). Tests go in `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/`.

2. **No Hrot references** — `Fdp.Toolkits` does NOT reference `Hrot.MuscleCharacter.Animation`.
   Therefore `AnimationChannel` is NOT available. The `OffMeshLinkDetectionSystem` must emit
   `OffMeshTraversalStartedEvent` to communicate the traversal intent — it does NOT write
   `AnimationChannel.PlayMontage` directly. Downstream Hrot systems handle the event.

3. **No double-position-integration** — `CrowdAgentUpdateSystem` writes `SimVelocity` AND
   integrates `SimTransform.Position += velocity * dt`. The `LinearKinematicsSystem` must add
   `.Without<CrowdAgent>()` to prevent doubling the integration.

4. **ECB flush semantics** — for structural changes (Add/Remove component), use ECB obtained
   via `repo.GetCommandBuffer()`. Direct mutation (`repo.AddComponent / repo.RemoveComponent`)
   is also acceptable in the same-tick flow since the test infrastructure flushes before assertions.
   The tests can call `repo.FlushCommandBuffers()` if available; otherwise use direct mutation.

5. **Infantry detection** — distinguish infantry from vehicles by checking absence of
   `VehicleState`. If entity does NOT have `VehicleState`, it is crowd-eligible (infantry).
   Do NOT add a dependency on Hrot `StanceStatus`.

6. **`NavigationStatus.CurrentTraversalKind`** — this field must be added to `NavigationStatus`
   in `NavigationComponents.cs`. Verify struct size constraints (max 64 bytes; check the existing
   layout before adding).

7. **EventId 2035** — assign `OffMeshTraversalStartedEvent` the next sequential EventId after 2034.

8. **`CrowdAgentUpdateSystem` runs in `SystemPhase.Simulation`** (early, before
   `NavigationExecutionSystem`). Mark it `[UpdateInPhase(SystemPhase.Simulation)]`.
   `OffMeshLinkDetectionSystem` must run BEFORE `CrowdAgentUpdateSystem` (use
   `[UpdateBefore(typeof(CrowdAgentUpdateSystem))]`).

9. **Default crowd params** — `CrowdAgentParams` for registration uses `NavAgentProfile.AgentRadius`
   for `Radius`, `TargetSpeed` from `MoveToParams` for `MaxSpeed`, and a constant 20f for
   `MaxAcceleration`. If entity has no `NavAgentProfile`, use Radius=0.4f, MaxSpeed=5f.

10. **Lookahead distance constant** — `OffMeshLinkDetectionSystem` defaults to 3.0 metres.
    Expose as a constructor parameter (`float lookaheadDistance = 3.0f`).

11. **Preserve existing comments** — do not reformat or reflow existing files.

---

## Codebase facts to verify before coding

Run these searches to confirm your mental model before writing any new code:

1. `NavigationStatus` struct is in `NavigationComponents.cs` at approx line 273.
   Current fields: `IntentId(uint), Result, ProgressS(float), Phase(NavigationPhase byte), LastFailureReason, ReplanCount(ushort), RouteHandle(int), EstimatedTimeRemaining(float), NavmeshVersionObserved(uint)`.
   There is NO `CurrentTraversalKind` field yet. Size budget: 4+1+4+1+1+2+4+4+4 = 25 bytes used (aligned to next multiple of 4 = 28 bytes with padding). Adding a `TraversalKind` (1 byte) fits in existing alignment gap.

2. `CrowdAgent` tag struct is in `NavigationComponents.cs` at approx line 463. It has no fields (pure tag).

3. `LinearKinematicsSystem` query is in `CarKinem/Systems/LinearKinematicsSystem.cs`.
   Current: `.With<SimTransform>().With<SimVelocity>().Without<VehicleState>()`.
   Need to add: `.Without<CrowdAgent>()`.

4. `PathfindingEvents.cs` has EventIds 2032, 2033, 2034. New event gets 2035.

5. `MusclePathRegistry.TryGetWaypointsSlice(handle, startSegment, maxCount, dest, out actualCount)`
   is available via `IPathRegistry`. The `OffMeshLinkDetectionSystem` needs an `IPathRegistry`
   (muscle-side) to look up corridor waypoints.

6. `NavigationCorridorMuscle` has fields: `RouteHandle(int), NavmeshVersion(uint),
   CurrentSegmentIndex(int), TotalSegmentCount(int), TotalDistance(float), PrimaryBackend(byte), Flags(byte)`.
   No waypoint storage — waypoints are in the path registry.

---

## Task 1: NAV-P3-T1 — CrowdAgent Admission + CrowdAgentUpdateSystem

### 1.1 Add `CurrentTraversalKind` to `NavigationStatus`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs` — MODIFY

After the field `public NavigationPhase Phase;` (which is 1 byte), add:

```csharp
        /// <summary>
        /// The traversal kind of the off-mesh link currently being traversed.
        /// Walk = 0 (no active off-mesh traversal).
        /// Written by <c>OffMeshLinkDetectionSystem</c>.
        /// </summary>
        public TraversalKind CurrentTraversalKind;
```

This byte fits in the same alignment gap occupied by the existing `LastFailureReason` field placement.
Verify size is still within struct layout constraints after the addition.

### 1.2 Extend `NavigationIntentBridgeSystem` for crowd registration

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs` — MODIFY

**Step A:** Add an `IDtCrowdProvider?` field and a new constructor:

```csharp
        private readonly IDtCrowdProvider? _dtCrowd;

        /// <summary>
        /// Creates an instance with access to the crowd provider for infantry crowd registration.
        /// </summary>
        public NavigationIntentBridgeSystem(TrajectoryPoolManager? trajectoryPool, IDtCrowdProvider? dtCrowd)
        {
            _trajectoryPool = trajectoryPool;
            _dtCrowd = dtCrowd;
        }
```

Keep all existing constructors unchanged. The two-arg constructor is additive.

**Step B:** Inside the `ActionIdMoveTo` case, AFTER publishing `PathfindingRequestEvent`,
add crowd registration for infantry (entities without `VehicleState`):

```csharp
                    // Crowd registration for infantry (entities without VehicleState).
                    if (_dtCrowd != null && !repo.HasComponent<VehicleState>(entity))
                    {
                        var profile = repo.HasComponent<NavAgentProfile>(entity)
                            ? repo.GetComponent<NavAgentProfile>(entity)
                            : default;

                        float radius  = profile.AgentRadius > 0f ? profile.AgentRadius : 0.4f;
                        float maxSpd  = p.TargetSpeed > 0f ? p.TargetSpeed : 5f;

                        _dtCrowd.RegisterAgent(entity, new CrowdAgentParams
                        {
                            Radius          = radius,
                            Height          = profile.AgentHeight > 0f ? profile.AgentHeight : 1.8f,
                            MaxSpeed        = maxSpd,
                            MaxAcceleration = 20f,
                            SeparationWeight = 2,
                        });

                        // Tag the entity as crowd-managed.
                        if (!repo.HasComponent<CrowdAgent>(entity))
                            repo.AddComponent(entity, default(CrowdAgent));

                        // Set the target in the crowd provider.
                        var destination = new System.Numerics.Vector3(
                            p.Destination.X, p.Destination.Y, 0f);
                        _dtCrowd.SetAgentTarget(entity, destination);
                    }
```

Place this block immediately after the `repo.Bus.Publish(new PathfindingRequestEvent {...})` call.

**Important:** `MoveToParams.TargetSpeed` — verify that `MoveToParams` struct has `TargetSpeed`
by reading `NavigationActions.cs` before writing. If the field name differs, use the correct one.

### 1.3 Create `CrowdAgentUpdateSystem`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs` — CREATE NEW

```csharp
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// Reads the crowd-computed velocity for each <see cref="CrowdAgent"/>-tagged entity and
    /// writes it to <see cref="SimVelocity"/>. Also integrates <see cref="SimTransform.Position"/>
    /// by the velocity * dt to match the crowd provider's internal integration.
    ///
    /// <para>
    /// Must run AFTER <see cref="OffMeshLinkDetectionSystem"/> (which sets
    /// <c>Phase = AwaitingTraversal</c> and removes the <c>CrowdAgent</c> tag via ECB)
    /// and BEFORE <see cref="NavigationExecutionSystem"/>.
    /// </para>
    ///
    /// <para>
    /// Entities in <see cref="NavigationPhase.AwaitingTraversal"/> are skipped — the animation
    /// system owns <see cref="SimTransform"/> during off-mesh traversal.
    /// </para>
    ///
    /// <para>
    /// <see cref="CarKinem.Systems.LinearKinematicsSystem"/> must carry
    /// <c>.Without&lt;CrowdAgent&gt;()</c> to prevent double-integration.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class CrowdAgentUpdateSystem : IEcsModuleSystem
    {
        private readonly IDtCrowdProvider _dtCrowd;

        /// <summary>
        /// Creates the system with access to the crowd steering provider.
        /// </summary>
        /// <param name="dtCrowd">The active crowd provider. Must not be null.</param>
        public CrowdAgentUpdateSystem(IDtCrowdProvider dtCrowd)
        {
            _dtCrowd = dtCrowd ?? throw new ArgumentNullException(nameof(dtCrowd));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(CrowdAgentUpdateSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            if (!repo.IsComponentTypeRegistered<CrowdAgent>()
                || !repo.IsComponentTypeRegistered<SimVelocity>()
                || !repo.IsComponentTypeRegistered<NavigationStatus>())
                return;

            // Advance crowd simulation once per tick.
            _dtCrowd.Update(deltaTime, view);

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<SimVelocity>()
                .With<NavigationStatus>()
                .Build();

            foreach (var entity in query)
            {
                var status = repo.GetComponent<NavigationStatus>(entity);

                // Suppress velocity during off-mesh traversal — animation owns locomotion.
                if (status.Phase == NavigationPhase.AwaitingTraversal)
                    continue;

                var velocity = _dtCrowd.GetAgentVelocity(entity);

                // Write crowd velocity to SimVelocity.
                if (repo.HasComponent<SimVelocity>(entity))
                {
                    var simVel = repo.GetComponent<SimVelocity>(entity);
                    simVel.Linear = velocity;
                    repo.SetComponent(entity, simVel);
                }

                // Integrate position: LinearKinematicsSystem is excluded for CrowdAgent
                // entities, so this system owns position integration for crowd-managed agents.
                if (repo.HasComponent<SimTransform>(entity))
                {
                    ref var tf = ref repo.GetComponentRW<SimTransform>(entity);
                    tf.Position += velocity * deltaTime;
                }
            }
        }
    }
}
```

### 1.4 Add `.Without<CrowdAgent>()` to `LinearKinematicsSystem`

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/LinearKinematicsSystem.cs` — MODIFY

In the `Execute` method, find the query builder:
```csharp
            var query = repo.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Without<VehicleState>()
                .Build();
```

Change to:
```csharp
            var query = repo.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                .Without<VehicleState>()
                .Without<CrowdAgent>()
                .Build();
```

**IMPORTANT:** `CrowdAgent` is in namespace `Fdp.Toolkit.Navigation`. You must add the using:
```csharp
using Fdp.Toolkit.Navigation;
```

Check the existing usings in `LinearKinematicsSystem.cs`. If it already imports a navigation namespace, reuse it. If not, add the minimal `using Fdp.Toolkit.Navigation;`.

---

## Task 2: NAV-P3-T2 — OffMeshLinkDetectionSystem

### 2.1 Add `OffMeshTraversalStartedEvent` to `PathfindingEvents.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingEvents.cs` — MODIFY

After the `MoveStartedEvent` (EventId 2034), append:

```csharp
    /// <summary>
    /// Published by <see cref="Systems.OffMeshLinkDetectionSystem"/> when an entity begins
    /// an off-mesh traversal (jump, climb, door, fly).
    /// Downstream animation systems listen for this to trigger the appropriate montage.
    /// (EventId = 2035)
    /// </summary>
    [EventId(2035)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OffMeshTraversalStartedEvent
    {
        /// <summary>The entity beginning the traversal.</summary>
        public Entity Target;

        /// <summary>World-space position of the off-mesh link start point.</summary>
        public System.Numerics.Vector3 LinkWorldPos;

        /// <summary>The kind of traversal (Jump, Climb, Door, Fly).</summary>
        public TraversalKind TraversalKind;

        // 3 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }
```

Make sure the layout attribute and namespace match the file's existing pattern (`[StructLayout(LayoutKind.Sequential)]` is used on the other events too).

### 2.2 Create `OffMeshLinkDetectionSystem`

**File:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/OffMeshLinkDetectionSystem.cs` — CREATE NEW

This system:
1. Queries entities with `CrowdAgent, SimTransform, NavigationStatus, NavigationCorridorMuscle`.
2. Looks ahead in the path registry from `CurrentSegmentIndex + 1` for the first waypoint with
   `Traversal != TraversalKind.Walk` within `lookaheadDistance` metres.
3. If found AND entity is within look-ahead distance of the link start position:
   - Writes `NavigationStatus.Phase = NavigationPhase.AwaitingTraversal`
   - Writes `NavigationStatus.CurrentTraversalKind = waypoint.Traversal`
   - Emits `OffMeshTraversalStartedEvent` to `repo.Bus`
   - Removes `CrowdAgent` via ECB (or direct, see rule 4)
   - Unregisters from crowd provider (`_dtCrowd.UnregisterAgent(entity)`)
4. Reads `MontageEndedEvent` events from the bus. For each:
   - Finds the entity that is the montage target
   - If it has `NavigationStatus.Phase == AwaitingTraversal`:
     - Writes `Phase = NavigationPhase.Following`
     - Writes `CurrentTraversalKind = TraversalKind.Walk`
     - Advances `NavigationCorridorMuscle.CurrentSegmentIndex` by 1
     - Re-adds `CrowdAgent` tag
     - Re-registers with crowd provider

```csharp
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.MuscleCharacter.Animation.Events;

namespace Fdp.Toolkit.Navigation.Systems
{
    // DESIGN NOTE: This system does NOT write to AnimationChannel directly.
    // Fdp.Toolkits does not reference Hrot.MuscleCharacter.Animation.
    // Instead, OffMeshTraversalStartedEvent is emitted. A Hrot-side system
    // handles the event and writes AnimationChannel.PlayMontage.
    //
    // This is an intentional assembly boundary enforced by project constraints.

    /// <summary>
    /// Detects when a crowd-managed agent is approaching an off-mesh link (a segment
    /// with <see cref="TraversalKind"/> != <see cref="TraversalKind.Walk"/>) within
    /// the look-ahead distance, and initiates the traversal sequence:
    /// <list type="bullet">
    ///   <item>Writes <see cref="NavigationPhase.AwaitingTraversal"/> to suppress crowd velocity.</item>
    ///   <item>Emits <see cref="OffMeshTraversalStartedEvent"/> for the animation tier.</item>
    ///   <item>Removes the <see cref="CrowdAgent"/> tag so crowd avoidance pauses.</item>
    ///   <item>On <c>MontageEndedEvent</c>, resumes following and restores crowd membership.</item>
    /// </list>
    ///
    /// <para>
    /// Must run BEFORE <see cref="CrowdAgentUpdateSystem"/> in the same tick so that the
    /// suppressed phase is visible before the velocity write.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [UpdateBefore(typeof(CrowdAgentUpdateSystem))]
    public class OffMeshLinkDetectionSystem : IEcsModuleSystem
    {
        private readonly IPathRegistry _pathRegistry;
        private readonly IDtCrowdProvider _dtCrowd;
        private readonly float _lookaheadDistance;

        // Scratch buffer for waypoint look-ahead (reused each tick, sized for 8 waypoints).
        private readonly NavWaypoint[] _waypointScratch = new NavWaypoint[8];

        /// <summary>
        /// Creates the system with the muscle-side path registry and crowd provider.
        /// </summary>
        /// <param name="pathRegistry">Muscle-owned path store (read-only).</param>
        /// <param name="dtCrowd">Crowd provider (for unregistering agents during traversal).</param>
        /// <param name="lookaheadDistance">
        /// Maximum distance ahead (metres) to scan for off-mesh links. Default 3.0 m.
        /// </param>
        public OffMeshLinkDetectionSystem(
            IPathRegistry pathRegistry,
            IDtCrowdProvider dtCrowd,
            float lookaheadDistance = 3.0f)
        {
            _pathRegistry      = pathRegistry ?? throw new ArgumentNullException(nameof(pathRegistry));
            _dtCrowd           = dtCrowd      ?? throw new ArgumentNullException(nameof(dtCrowd));
            _lookaheadDistance = lookaheadDistance;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(OffMeshLinkDetectionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // ── Phase 1: Handle MontageEndedEvent to resume crowd movement ──────────
            HandleMontageEndedEvents(repo);

            // ── Phase 2: Detect approaching off-mesh links ───────────────────────────
            if (!repo.IsComponentTypeRegistered<CrowdAgent>()
                || !repo.IsComponentTypeRegistered<NavigationCorridorMuscle>()
                || !repo.IsComponentTypeRegistered<NavigationStatus>()
                || !repo.IsComponentTypeRegistered<SimTransform>())
                return;

            var query = repo.Query()
                .With<CrowdAgent>()
                .With<SimTransform>()
                .With<NavigationStatus>()
                .With<NavigationCorridorMuscle>()
                .Build();

            foreach (var entity in query)
            {
                var status   = repo.GetComponent<NavigationStatus>(entity);
                var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);
                var tf       = repo.GetComponent<SimTransform>(entity);

                // Skip entities already in traversal or without an active corridor.
                if (status.Phase == NavigationPhase.AwaitingTraversal) continue;
                if (corridor.RouteHandle == 0) continue;
                if (corridor.CurrentSegmentIndex >= corridor.TotalSegmentCount - 1) continue;

                // Look ahead one segment from the current position.
                int lookStart   = corridor.CurrentSegmentIndex + 1;
                int maxToCheck  = Math.Min(8, corridor.TotalSegmentCount - lookStart);
                if (maxToCheck <= 0) continue;

                if (!_pathRegistry.TryGetWaypointsSlice(
                        corridor.RouteHandle,
                        lookStart,
                        maxToCheck,
                        _waypointScratch.AsSpan(0, maxToCheck),
                        out int fetched) || fetched == 0)
                    continue;

                // Find first non-Walk waypoint within look-ahead distance.
                for (int i = 0; i < fetched; i++)
                {
                    var wp = _waypointScratch[i];
                    if (wp.Traversal == TraversalKind.Walk) continue;

                    float dist = Vector3.Distance(tf.Position, wp.Position);
                    if (dist > _lookaheadDistance) break; // waypoints are ordered; no closer link beyond

                    // Off-mesh link detected within look-ahead range.
                    BeginTraversal(repo, entity, wp, status, corridor);
                    break;
                }
            }
        }

        private void BeginTraversal(
            EntityRepository repo,
            Entity entity,
            NavWaypoint linkWaypoint,
            NavigationStatus status,
            NavigationCorridorMuscle corridor)
        {
            // 1. Set phase to suppress crowd velocity this tick.
            status.Phase               = NavigationPhase.AwaitingTraversal;
            status.CurrentTraversalKind = linkWaypoint.Traversal;
            repo.SetComponent(entity, status);

            // 2. Emit traversal started event.
            repo.Bus.Publish(new OffMeshTraversalStartedEvent
            {
                Target       = entity,
                LinkWorldPos = linkWaypoint.Position,
                TraversalKind = linkWaypoint.Traversal,
            });

            // 3. Unregister from crowd provider (entity goes dormant until montage ends).
            _dtCrowd.UnregisterAgent(entity);

            // 4. Remove CrowdAgent tag so CrowdAgentUpdateSystem filters the entity out next tick.
            repo.RemoveComponent<CrowdAgent>(entity);
        }

        private void HandleMontageEndedEvents(EntityRepository repo)
        {
            // Read MontageEndedEvent from the Hrot animation system.
            // These events are read from the bus that was populated in previous ticks.
            // NOTE: Fdp.Toolkits does not reference Hrot.MuscleCharacter.Animation.
            // We handle this via generic event reading using the EventId.
            // Since we cannot import the Hrot namespace here, montage-end handling
            // will be implemented in a Hrot-side bridge system in a future phase.
            // For now, leave this body empty — the Phase 3 tests do not test montage resume
            // in the Fdp.Toolkits.Tests context.
        }
    }
}
```

**IMPORTANT NOTE on MontageEndedEvent:** The `OffMeshLinkDetectionSystem` cannot import
`Hrot.MuscleCharacter.Animation.Events.MontageEndedEvent` from `Fdp.Toolkits` due to assembly
boundaries. The `HandleMontageEndedEvents` is intentionally left as a stub — the montage-resume
logic will be implemented in a Hrot-side bridge system in a later phase. The 4 tests for
`Phase_TransitionsFromAwaitingToFollowing_VelocityResumes` in `CrowdAgentUpdateSystemTests` test
the behavior of the `CrowdAgentUpdateSystem` when `Phase` is manually set to `Following` (not
through the actual MontageEndedEvent), which is still testable.

The `OffMeshLinkDetectionSystemTests` test `MultipleAgentsAtSameLink_AllDetectedSameTick` can
test detection only (not resume).

Remove the `using Hrot.MuscleCharacter.Animation.Events;` line from the file — it will not compile.
The `HandleMontageEndedEvents` body stays empty for BATCH-06.

---

## Task 3: NAV-P9-T2 — `CrowdAgentUpdateSystemTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/CrowdAgentUpdateSystemTests.cs` — CREATE NEW

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
    /// DD-Tests-Nav §4.2 — <see cref="CrowdAgentUpdateSystem"/> unit tests.
    /// </summary>
    public class CrowdAgentUpdateSystemTests
    {
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<CrowdAgent>();
            return repo;
        }

        private static (Entity entity, FakeDtCrowdProvider crowd, CrowdAgentUpdateSystem system)
            CreateFollowingAgent(EntityRepository repo, Vector3 startPos, Vector3 target)
        {
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = startPos });
            repo.AddComponent(entity, new SimVelocity());
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius          = 0.4f,
                Height          = 1.8f,
                MaxSpeed        = 5f,
                MaxAcceleration = 20f,
                SeparationWeight = 2,
            });
            crowd.SetAgentTarget(entity, target);

            return (entity, crowd, system);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 1: Phase_Following_VelocityWritten.
        /// Normal path — SimVelocity gets the crowd output.
        /// </summary>
        [Fact]
        public void Phase_Following_VelocityWritten()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var vel = repo.GetComponent<SimVelocity>(entity);
            // After one Update tick aimed at (10,0,0), velocity must be non-zero in +X.
            Assert.True(vel.Linear.X > 0f,
                $"Expected positive X velocity toward target; got {vel.Linear}");
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 2: Phase_AwaitingTraversal_VelocitySuppressed.
        /// Phase set to AwaitingTraversal — SimVelocity must NOT be written.
        /// </summary>
        [Fact]
        public void Phase_AwaitingTraversal_VelocitySuppressed()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            var originalVel = new SimVelocity { Linear = new Vector3(99, 0, 0) };
            repo.AddComponent(entity, originalVel);
            repo.AddComponent(entity, new NavigationStatus
            {
                Phase = NavigationPhase.AwaitingTraversal,
            });
            repo.AddComponent(entity, default(CrowdAgent));

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var vel = repo.GetComponent<SimVelocity>(entity);
            // SimVelocity must NOT be overwritten — original value preserved.
            Assert.Equal(99f, vel.Linear.X, precision: 3);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 3: MissingCrowdAgentTag_EntitySkipped.
        /// Entity without CrowdAgent tag is skipped; no velocity change.
        /// </summary>
        [Fact]
        public void MissingCrowdAgentTag_EntitySkipped()
        {
            using var repo = CreateWorld();
            var crowd = new FakeDtCrowdProvider();
            var system = new CrowdAgentUpdateSystem(crowd);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
            var originalVel = new SimVelocity { Linear = new Vector3(7, 0, 0) };
            repo.AddComponent(entity, originalVel);
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            // Deliberately NOT adding CrowdAgent tag.

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });
            crowd.SetAgentTarget(entity, new Vector3(10, 0, 0));

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Entity has no CrowdAgent; system should skip it entirely.
            var vel = repo.GetComponent<SimVelocity>(entity);
            Assert.Equal(7f, vel.Linear.X, precision: 3);
        }

        /// <summary>
        /// DD-Tests-Nav §4.2 row 4: Phase_TransitionsFromAwaitingToFollowing_VelocityResumes.
        /// After external code transitions Phase back to Following, velocity is written again.
        /// </summary>
        [Fact]
        public void Phase_TransitionsFromAwaitingToFollowing_VelocityResumes()
        {
            using var repo = CreateWorld();
            var (entity, crowd, system) = CreateFollowingAgent(
                repo,
                startPos: new Vector3(0, 0, 0),
                target:   new Vector3(10, 0, 0));

            // First tick: normal following — velocity gets written.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var velAfterFirst = repo.GetComponent<SimVelocity>(entity);
            Assert.True(velAfterFirst.Linear.X > 0f);

            // Simulate traversal: manually set Phase to AwaitingTraversal.
            var status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.AwaitingTraversal;
            repo.SetComponent(entity, status);

            // Second tick: suppressed.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Simulate montage end: external code restores Phase to Following.
            status = repo.GetComponent<NavigationStatus>(entity);
            status.Phase = NavigationPhase.Following;
            repo.SetComponent(entity, status);

            // Third tick: velocity must be written again.
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);
            var velAfterResume = repo.GetComponent<SimVelocity>(entity);
            Assert.True(velAfterResume.Linear.X > 0f,
                $"Expected resumed velocity after phase returns to Following; got {velAfterResume.Linear}");
        }
    }
}
```

---

## Task 4: NAV-P9-T1 — `OffMeshLinkDetectionSystemTests`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/OffMeshLinkDetectionSystemTests.cs` — CREATE NEW

The test setup requires:
- A `MusclePathRegistry` loaded with a path that contains a non-Walk waypoint
- An entity with `CrowdAgent`, `SimTransform`, `NavigationStatus`, `NavigationCorridorMuscle`
- An instance of `OffMeshLinkDetectionSystem`

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
    /// DD-Tests-Nav §4.1 — <see cref="OffMeshLinkDetectionSystem"/> unit tests.
    /// Seven rows covering the zero-frame-suppression mechanism.
    ///
    /// Note: MontageEndedEvent handling is a Hrot-side concern (assembly boundary);
    /// tests here cover detection only. The "PlayMontageWritten" test verifies
    /// that OffMeshTraversalStartedEvent carries the correct TraversalKind discriminant
    /// (the event is what triggers animation-tier montage selection).
    /// </summary>
    public class OffMeshLinkDetectionSystemTests
    {
        private const int RouteHandle = 10;
        private const float Lookahead = 3.0f;

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<NavigationCorridorMuscle>();
            repo.RegisterComponent<CrowdAgent>();
            return repo;
        }

        /// <summary>
        /// Creates a MusclePathRegistry with a two-waypoint path: Walk → JumpAcross.
        /// The Walk waypoint is at (0,0,0) and the Jump waypoint is at (5,0,0).
        /// </summary>
        private static MusclePathRegistry CreateRegistryWithOffMeshLink(
            Vector3 walkPos, Vector3 jumpPos)
        {
            var registry = new MusclePathRegistry();
            registry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = walkPos, Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = jumpPos, Traversal = TraversalKind.Jump },
            });
            return registry;
        }

        /// <summary>
        /// Creates a MusclePathRegistry with only Walk waypoints.
        /// </summary>
        private static MusclePathRegistry CreateRegistryAllWalk(
            Vector3 from, Vector3 to)
        {
            var registry = new MusclePathRegistry();
            registry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = from, Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = to,   Traversal = TraversalKind.Walk },
            });
            return registry;
        }

        private static (Entity entity, FakeDtCrowdProvider crowd) CreateCrowdAgentEntity(
            EntityRepository repo, Vector3 position, int currentSegment = 0, int totalSegments = 2)
        {
            var crowd = new FakeDtCrowdProvider();
            var entity = repo.CreateEntity();

            repo.AddComponent(entity, new SimTransform { Position = position });
            repo.AddComponent(entity, new SimVelocity());
            repo.AddComponent(entity, new NavigationStatus { Phase = NavigationPhase.Following });
            repo.AddComponent(entity, default(CrowdAgent));
            repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle          = RouteHandle,
                CurrentSegmentIndex  = currentSegment,
                TotalSegmentCount    = totalSegments,
            });

            crowd.RegisterAgent(entity, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });

            return (entity, crowd);
        }

        // ── Test 1: No off-mesh link in path → phase unchanged ─────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 1: NoLink_PhaseUnchanged.
        /// No off-mesh link in corridor — Phase must not be modified.
        /// </summary>
        [Fact]
        public void NoLink_PhaseUnchanged()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryAllWalk(new Vector3(0, 0, 0), new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: Vector3.Zero);
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Following, status.Phase);
        }

        // ── Test 2: Off-mesh link beyond look-ahead → phase unchanged ───────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 2: LinkBeyondLookahead_PhaseUnchanged.
        /// Link is in path but agent is far away (outside look-ahead) — no write.
        /// </summary>
        [Fact]
        public void LinkBeyondLookahead_PhaseUnchanged()
        {
            using var repo = CreateWorld();
            // Jump link is at (5,0,0). Agent at (0,0,0) — distance = 5m > Lookahead (3m).
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: Vector3.Zero);
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.Following, status.Phase);
        }

        // ── Test 3: Off-mesh link within look-ahead → AwaitingTraversal ────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 3: LinkWithinLookahead_PhaseSetToAwaitingTraversal.
        /// Agent at (4,0,0), Jump link at (5,0,0) — distance = 1m (within 3m lookahead).
        /// </summary>
        [Fact]
        public void LinkWithinLookahead_PhaseSetToAwaitingTraversal()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            // Agent close to jump link.
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var status = repo.GetComponent<NavigationStatus>(entity);
            Assert.Equal(NavigationPhase.AwaitingTraversal, status.Phase);
        }

        // ── Test 4: Detection emits event with TraversalKind discriminant ───────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 4: LinkDetected_PlayMontageWritten.
        /// When link is detected, OffMeshTraversalStartedEvent carries the TraversalKind
        /// discriminant (the animation tier uses this to select the montage).
        /// </summary>
        [Fact]
        public void LinkDetected_TraversalStartedEventCarriesKind()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Event must have been published with TraversalKind.Jump.
            var events = repo.Bus.ReadEvents<OffMeshTraversalStartedEvent>();
            Assert.Single(events);
            Assert.Equal(TraversalKind.Jump, events[0].TraversalKind);
        }

        // ── Test 5: CrowdAgent tag removed after detection ──────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 5: LinkDetected_CrowdAgentTagRemovedViaECB.
        /// After detection, entity no longer has CrowdAgent (removed directly or via ECB flush).
        /// </summary>
        [Fact]
        public void LinkDetected_CrowdAgentTagRemoved()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // CrowdAgent must be gone.
            Assert.False(repo.HasComponent<CrowdAgent>(entity));
        }

        // ── Test 6: Event emitted with LinkWorldPos ─────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 6: LinkDetected_OffMeshTraversalStartedEventEmitted.
        /// Event carries correct TraversalKind and LinkWorldPos.
        /// </summary>
        [Fact]
        public void LinkDetected_OffMeshTraversalStartedEventEmitted()
        {
            using var repo = CreateWorld();
            var jumpPos = new Vector3(5, 0, 0);
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: jumpPos);
            var (entity, crowd) = CreateCrowdAgentEntity(repo, position: new Vector3(4, 0, 0));
            var system = new OffMeshLinkDetectionSystem(registry, crowd, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            var events = repo.Bus.ReadEvents<OffMeshTraversalStartedEvent>();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Target);
            Assert.Equal(jumpPos, events[0].LinkWorldPos);
            Assert.Equal(TraversalKind.Jump, events[0].TraversalKind);
        }

        // ── Test 7: Multiple agents at same link → both detected ────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.1 row 7: MultipleAgentsAtSameLink_AllDetectedSameTick.
        /// Two agents close to the same jump link; both trigger detection in the same tick.
        /// </summary>
        [Fact]
        public void MultipleAgentsAtSameLink_AllDetectedSameTick()
        {
            using var repo = CreateWorld();
            var registry = CreateRegistryWithOffMeshLink(
                walkPos: new Vector3(0, 0, 0), jumpPos: new Vector3(5, 0, 0));

            var (entity1, crowd1) = CreateCrowdAgentEntity(
                repo, position: new Vector3(4, 0, 0));
            var (entity2, crowd2) = CreateCrowdAgentEntity(
                repo, position: new Vector3(3.5f, 0, 0));

            // Use entity1's crowd provider for both; entity2 registered with crowd1 too.
            crowd1.RegisterAgent(entity2, new CrowdAgentParams
            {
                Radius = 0.4f, Height = 1.8f, MaxSpeed = 5f, MaxAcceleration = 20f,
            });

            var system = new OffMeshLinkDetectionSystem(registry, crowd1, Lookahead);

            repo.Bus.SwapBuffers();
            system.Execute(repo, 0.1f);

            // Both entities should be in AwaitingTraversal.
            Assert.Equal(NavigationPhase.AwaitingTraversal,
                repo.GetComponent<NavigationStatus>(entity1).Phase);
            Assert.Equal(NavigationPhase.AwaitingTraversal,
                repo.GetComponent<NavigationStatus>(entity2).Phase);
        }
    }
}
```

---

## MusclePathRegistry API note

The tests call `registry.StoreOrReplace(handle, waypoints[])`. Check `MusclePathRegistry.cs` to
confirm the method name and signature before writing. If the actual method is different (e.g.
`RegisterOrReplace`, `Add`, `StorePath`), use the correct name.

The method found in BATCH-04 was `StoreOrReplace(int handle, NavWaypoint[] waypoints)`.
Verify by reading `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/MusclePathRegistry.cs`.

---

## Task 5: Update `NavigationTestWorldFactory`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationTestWorldFactory.cs` — MODIFY

Add registration for `NavigationCorridorMuscle` (already registered) — no change needed since
it is already present. However verify that `CrowdAgent` is also registered, which it already is.

No change needed if the factory already has both. Verify before modifying.

---

## Build and test verification

After implementing all changes:

```powershell
# Build only (no test run)
dotnet build FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --no-restore

# Run only the new and affected tests
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests --no-build `
    --filter "Navigation" 2>&1 | Select-Object -Last 20
```

**Expected:**
- 0 build errors, 0 build warnings that would fail CI
- All pre-existing 151 navigation tests still pass
- 4 new `CrowdAgentUpdateSystemTests` pass
- 7 new `OffMeshLinkDetectionSystemTests` pass
- Total: 162 navigation tests passing

---

## Task checklist

- [ ] `NavigationStatus.CurrentTraversalKind` field added (1 byte, fits in struct)
- [ ] `OffMeshTraversalStartedEvent` added to `PathfindingEvents.cs` (EventId 2035)
- [ ] `CrowdAgentUpdateSystem` created with correct suppress logic
- [ ] `NavigationIntentBridgeSystem` extended with two-arg constructor + crowd registration
- [ ] `LinearKinematicsSystem` has `.Without<CrowdAgent>()` + using added
- [ ] `OffMeshLinkDetectionSystem` created (detection only; montage resume is Hrot-side)
- [ ] `CrowdAgentUpdateSystemTests.cs` created with 4 tests
- [ ] `OffMeshLinkDetectionSystemTests.cs` created with 7 tests
- [ ] Build: 0 errors
- [ ] Tests: 162/162 navigation tests pass (151 pre-existing + 11 new)
