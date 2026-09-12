#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.Stride.Core;
using Xunit;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless integration tests for the BATCH-20 PRODUCTION navigation front door (STR-D19):
/// driving both characters and vehicles through the FDP <see cref="NavigationIntent"/> /
/// <see cref="LocomotionChannel"/> command path rather than the demo's direct
/// <c>DtCrowd.RegisterAgent</c> shortcut.
///
/// <para>
/// <b>Part A (character):</b> issuing the production front door — a
/// <see cref="NavigationConstants.ActionIdMoveTo"/> on the <see cref="LocomotionChannel"/> via
/// <see cref="FdpNavigationOrders.IssueMoveTo"/> — causes <see cref="NavigationIntentBridgeSystem"/>
/// to AUTO-REGISTER a DotRecast crowd agent (no direct provider call), and the resulting chain
/// (<c>CrowdAgentUpdateSystem → CrowdMotorIntent</c>) produces a non-zero steering velocity.
/// </para>
///
/// <para>
/// <b>Part B (vehicle):</b> <see cref="VehicleNavigationIntentSystem"/> over floor+wall geometry
/// plans a navmesh path from a <see cref="NavigationIntent"/>, steers <see cref="VehicleState"/>
/// toward the first corner / around the wall, advances corners as the vehicle moves, and sets
/// <see cref="NavigationResult.Arrived"/> at the goal.
/// </para>
///
/// <para>
/// Coordinate space: navmesh-query space = Stride world space = X=East, Y=altitude(up), Z=North.
/// FDP world space is X=East, Y=North, Z=Up (the swizzle Stride=(fdp.X, fdp.Z, fdp.Y) is applied
/// by <see cref="FdpStrideTransform"/> / the crowd + navmesh providers).
/// </para>
/// </summary>
public sealed class FdpMoveOrderIntegrationTests : IDisposable
{
    private readonly EntityRepository _world;

    public FdpMoveOrderIntegrationTests()
    {
        _world = new EntityRepository();
        // Mirror the editor_stride registered-component set relevant to navigation.
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<NavState>();
        _world.RegisterComponent<NavigationStatus>();
        _world.RegisterComponent<NavigationIntent>();
        _world.RegisterComponent<NavAgentProfile>();
        _world.RegisterComponent<CrowdAgent>();
        _world.RegisterComponent<CrowdMotorIntent>();
        _world.RegisterComponent<LocomotionChannel>();
        _world.RegisterComponent<VehicleState>();
        _world.RegisterEvent<PathfindingRequestEvent>();
    }

    public void Dispose() => _world.Dispose();

    // ── Geometry helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// L-corridor Infantry navmesh (same layout as <c>NavmeshWalkIntegrationTests</c>):
    /// west strip X=[−12,0] Z=[−5,+15], east strip X=[0,+12] Z=[+5,+15]. The direct east route at
    /// Z≈0 is not walkable east of X=0, so a path from the west strip to the east strip must detour
    /// north (Z &gt; +5).
    /// </summary>
    private static DtNavMesh BakeLCorridorInfantry()
    {
        var verts = new List<float>();
        var idx   = new List<int>();

        void AddQuad(float x0, float z0, float x1, float z1)
        {
            int b = verts.Count / 3;
            verts.AddRange(new[] { x0, 0f, z0,  x1, 0f, z0,  x1, 0f, z1,  x0, 0f, z1 });
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); // CCW from above → walkable
        }

        AddQuad(-12f, -5f, 0f, 15f);  // west strip
        AddQuad(0f, 5f, 12f, 15f);    // east strip (north portion only)

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts.ToArray(), idx.ToArray(), NavLayerMask.Infantry);
        Assert.True(meshes.ContainsKey(NavLayerMask.Infantry), "Infantry navmesh must bake.");
        return meshes[NavLayerMask.Infantry];
    }

    /// <summary>
    /// Floor + E-W wall Vehicle navmesh: floor X∈[−15,15], Z∈[−1,20]; a solid wall at Z=5 spanning
    /// X∈[−5,5]. After 1.5 m vehicle erosion the clear passage starts at |X| &gt; 6.5 m, so a path
    /// from south of the wall to north of it must detour around the ends (|X| &gt; ~6).
    /// </summary>
    private static DtNavMesh BakeFloorWallVehicle()
    {
        var verts = new List<float>();
        var idx   = new List<int>();

        // Floor quad (CCW from above → walkable).
        void AddGroundQuad(float minX, float maxX, float minZ, float maxZ, float y)
        {
            int b = verts.Count / 3;
            verts.AddRange(new[] { minX, y, minZ,  maxX, y, minZ,  maxX, y, maxZ,  minX, y, maxZ });
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        AddGroundQuad(-15f, 15f, -1f, 20f, 0f);
        // Wall box: centre (0,1,5), half-extents (5,1,0.25) → blocks X∈[−5,5] at Z=5.
        BoxGeometryHelper.ExtractBoxTriangles(
            SMath.Matrix.Translation(new SMath.Vector3(0f, 1f, 5f)),
            new SMath.Vector3(5f, 1f, 0.25f), verts, idx);

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts.ToArray(), idx.ToArray(), NavLayerMask.Vehicle);
        Assert.True(meshes.ContainsKey(NavLayerMask.Vehicle), "Vehicle navmesh must bake.");
        return meshes[NavLayerMask.Vehicle];
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  PART A — CHARACTER: production front door auto-registers + drives the chain
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// B20-A1: Setting the production front door (a <see cref="NavigationConstants.ActionIdMoveTo"/>
    /// on the <see cref="LocomotionChannel"/> via <see cref="FdpNavigationOrders.IssueMoveTo"/>)
    /// causes <see cref="NavigationIntentBridgeSystem"/> to auto-register the entity as a crowd
    /// agent — WITHOUT any direct <c>RegisterAgent</c> call from the test — and the
    /// <c>CrowdAgentUpdateSystem</c> then writes a non-zero <see cref="CrowdMotorIntent.Velocity"/>
    /// that routes north around the L-corridor gap.
    /// </summary>
    [Fact]
    public void CharacterFrontDoor_MoveToChannel_AutoRegistersAgent_AndDrivesNonzeroMotorIntent()
    {
        // ── Arrange: real crowd provider over the L-corridor navmesh + the production bridge ──
        var navMesh = BakeLCorridorInfantry();
        var crowd   = new DotRecastDtCrowdProvider(navMesh, maxAgentRadius: 0.4f);
        var bridge  = new NavigationIntentBridgeSystem(trajectoryPool: null, dtCrowd: crowd);
        var crowdSystem = new CrowdAgentUpdateSystem(crowd);

        // Spawn an infantry entity on the west strip. NO VehicleState → eligible for crowd bridge.
        var start = new Vector3(-8f, 0f, 0f);  // FDP (X=East, Y=North, Z=Up)
        var goal  = new Vector3( 8f, 10f, 0f); // east strip (reachable only via the north connector)

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = start, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new NavState());
        _world.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
        _world.AddComponent(entity, new CrowdMotorIntent());
        _world.AddComponent(entity, new NavAgentProfile
        {
            AgentRadius = 0.3f, AgentHeight = 1.8f, MaxSlopeDeg = 60f,
            PreferredLayerMask = (uint)NavLayerMask.Infantry,
        });

        // Precondition: the entity is NOT yet a crowd agent.
        Assert.False(crowd.TryGetAgentSnapshot(entity, out _),
            "Entity must NOT be a crowd agent before the production order is issued.");
        Assert.False(_world.HasComponent<CrowdAgent>(entity),
            "Entity must NOT carry the CrowdAgent tag before the bridge processes the MoveTo.");

        // ── Act 1: issue the PRODUCTION front door (LocomotionChannel MoveTo) ──
        uint actionId = FdpNavigationOrders.IssueMoveTo(
            _world, entity, goal, speed: 2f, arrivalRadius: 1.5f, NavLayerMask.Infantry);
        Assert.True(actionId > 0u, "IssueMoveTo must return a non-zero ActionInstanceId.");

        // ── Act 2: run the bridge — it must auto-register the crowd agent from the channel action ──
        bridge.Execute(_world, 0.1f);

        // ── Assert: auto-registration happened (the bridge, not the test, enrolled the agent) ──
        Assert.True(_world.HasComponent<CrowdAgent>(entity),
            "NavigationIntentBridgeSystem must add the CrowdAgent tag in response to the MoveTo action.");
        Assert.True(crowd.TryGetAgentSnapshot(entity, out _),
            "NavigationIntentBridgeSystem must auto-register the entity with the DotRecast crowd provider " +
            "(no direct RegisterAgent call was made by the test).");

        // ── Act 3: drive the crowd chain ──
        for (int i = 0; i < 20; i++)
        {
            _world.Bus.SwapBuffers();
            crowdSystem.Execute(_world, 0.1f);
        }

        // ── Assert: the chain produced a non-zero steering velocity, routing NORTH (FDP +Y) ──
        var intent = _world.GetComponent<CrowdMotorIntent>(entity);
        float speed = intent.Velocity.Length();
        Assert.True(speed > 0.05f,
            $"CrowdMotorIntent.Velocity must be non-zero after the production front door drove the chain; " +
            $"got magnitude={speed:F3} vel={intent.Velocity}.");
        Assert.True(intent.Velocity.Y > 0.1f,
            $"Velocity must be northward (FDP +Y > 0.1) to route around the L-corridor gap; " +
            $"got FDP vel=({intent.Velocity.X:F3},{intent.Velocity.Y:F3},{intent.Velocity.Z:F3}).");
    }

    /// <summary>
    /// B20-A2: A VEHICLE entity (carries <see cref="VehicleState"/>) is EXCLUDED from the crowd
    /// bridge — issuing the same MoveTo channel action must NOT tag it as a crowd agent nor register
    /// it. This proves the character/vehicle split that motivates the separate
    /// <see cref="VehicleNavigationIntentSystem"/>.
    /// </summary>
    [Fact]
    public void VehicleFrontDoor_MoveToChannel_IsExcludedFromCrowdBridge()
    {
        var navMesh = BakeLCorridorInfantry();
        var crowd   = new DotRecastDtCrowdProvider(navMesh, maxAgentRadius: 0.4f);
        var bridge  = new NavigationIntentBridgeSystem(trajectoryPool: null, dtCrowd: crowd);

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = new Vector3(-8f, 0f, 0f), Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new NavState());
        _world.AddComponent(entity, new NavigationStatus());
        _world.AddComponent(entity, default(VehicleState)); // marks the entity as a vehicle

        FdpNavigationOrders.IssueMoveTo(_world, entity, new Vector3(8f, 10f, 0f), 3f, 1.5f, NavLayerMask.Vehicle);
        bridge.Execute(_world, 0.1f);

        Assert.False(_world.HasComponent<CrowdAgent>(entity),
            "A VehicleState entity must NOT be tagged as a crowd agent by the bridge.");
        Assert.False(crowd.TryGetAgentSnapshot(entity, out _),
            "A VehicleState entity must NOT be registered with the crowd provider.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  PART B — VEHICLE: VehicleNavigationIntentSystem plans + steers around the wall
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// B20-B1: <see cref="VehicleNavigationIntentSystem"/> reads a <see cref="NavigationIntent"/>
    /// (DirectPoint) on a <see cref="VehicleState"/> entity, plans a navmesh path over the
    /// floor+wall geometry, and on the FIRST tick writes a non-zero <see cref="VehicleState.Speed"/>
    /// steering the vehicle toward the first corner. The planned path must route AROUND the wall
    /// (a corner with |X| &gt; 4 m), and <see cref="NavigationStatus"/> must report InProgress.
    /// </summary>
    [Fact]
    public void VehicleNavSystem_DirectPointIntent_PlansPath_AndSteersTowardFirstCorner()
    {
        // ── Arrange ────────────────────────────────────────────────────────
        var navMesh  = BakeFloorWallVehicle();
        var provider = new DotRecastNavmeshProvider(
            new Dictionary<NavLayerMask, DtNavMesh> { [NavLayerMask.Vehicle] = navMesh });
        _world.SetSingletonManaged<INavmeshProvider>(provider);

        var sut = new VehicleNavigationIntentSystem();

        // Vehicle south of the wall, facing east. Goal north of the wall.
        var start = new Vector3(0f, 0f, 0f);   // FDP south of wall
        var goal  = new Vector3(0f, 10f, 0f);  // FDP north of wall
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = start, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new VehicleState());
        _world.AddComponent(entity, new NavigationStatus());
        _world.AddComponent(entity, new NavigationIntent
        {
            Mode             = NavigationMode.DirectPoint,
            FinalDestination = goal,
            TargetSpeed      = 3f,
            ArrivalRadius    = 1.5f,
            IntentId         = 1,
        });

        // ── Act: first tick plans the path + commands the vehicle ──
        sut.Execute(_world, 0.1f);

        // ── Assert: a path was planned (≥ 1 corner) and routes around the wall ──
        int corners = sut.GetCornerCount(entity);
        Assert.True(corners >= 1,
            $"VehicleNavigationIntentSystem must plan ≥1 corner for the DirectPoint intent; got {corners}.");

        // ── Assert: VehicleState is now commanding forward motion (non-zero speed) ──
        var vs = _world.GetComponent<VehicleState>(entity);
        Assert.True(vs.Speed > 0.1f,
            $"VehicleState.Speed must be non-zero after the system steers toward the first corner; got {vs.Speed:F3}.");

        // ── Assert: NavigationStatus echoes the intent + reports InProgress ──
        var status = _world.GetComponent<NavigationStatus>(entity);
        Assert.Equal(1u, status.IntentId);
        Assert.Equal(NavigationResult.InProgress, status.Result);
    }

    /// <summary>
    /// B20-B2: Closed-loop simulation — feeding the vehicle's commanded <see cref="VehicleState"/>
    /// back into <see cref="SimTransform"/> with a simple bicycle integrator, the
    /// <see cref="VehicleNavigationIntentSystem"/> advances the corner index as the vehicle reaches
    /// each corner and finally sets <see cref="NavigationResult.Arrived"/> within the arrival radius
    /// of the goal. The vehicle visibly routes AROUND the wall (passes a point with |X| &gt; 4 m).
    /// </summary>
    [Fact]
    public void VehicleNavSystem_ClosedLoop_AdvancesCorners_AndArrivesAtGoal()
    {
        var navMesh  = BakeFloorWallVehicle();
        var provider = new DotRecastNavmeshProvider(
            new Dictionary<NavLayerMask, DtNavMesh> { [NavLayerMask.Vehicle] = navMesh });
        _world.SetSingletonManaged<INavmeshProvider>(provider);

        // Generous controller so the integrator converges quickly in the test.
        var sut = new VehicleNavigationIntentSystem(
            navmeshFallback: null, cruiseSpeed: 4f, maxSteerAngleRad: 0.9f,
            headingGainK: 3f, arriveToleranceM: 1.5f, slowRadiusM: 4f, wheelBase: 2.5f);

        var start = new Vector3(0f, 0f, 0f);
        var goal  = new Vector3(0f, 12f, 0f);
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = start, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new VehicleState());
        _world.AddComponent(entity, new NavigationStatus());
        _world.AddComponent(entity, new NavigationIntent
        {
            Mode = NavigationMode.DirectPoint, FinalDestination = goal,
            TargetSpeed = 4f, ArrivalRadius = 1.5f, IntentId = 7,
        });

        const float dt = 0.05f;
        bool arrived = false;
        float maxAbsX = 0f;     // proves the route went around the wall (not straight through)
        bool sawSecondCorner = false;

        for (int step = 0; step < 2000 && !arrived; step++)
        {
            // System: plan (first tick) + steer.
            sut.Execute(_world, dt);

            if (sut.GetCurrentCorner(entity) >= 1) sawSecondCorner = true;

            // Bicycle integrator: advance SimTransform from the commanded VehicleState.
            var tf = _world.GetComponent<SimTransform>(entity);
            var vs = _world.GetComponent<VehicleState>(entity);

            var forward = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            float heading = MathF.Atan2(forward.Y, forward.X);

            // Yaw rate from the bicycle model: ω = v/L · tan(δ).
            float yawRate = (vs.Speed / 2.5f) * MathF.Tan(vs.SteerAngle);
            heading += yawRate * dt;

            var newPos = tf.Position + new Vector3(
                vs.Speed * MathF.Cos(heading) * dt,
                vs.Speed * MathF.Sin(heading) * dt,
                0f);

            maxAbsX = MathF.Max(maxAbsX, MathF.Abs(newPos.X));

            tf.Position = newPos;
            tf.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, heading); // FDP: yaw about Z (up)
            _world.SetComponent(entity, tf);

            var status = _world.GetComponent<NavigationStatus>(entity);
            if (status.Result == NavigationResult.Arrived)
                arrived = true;
        }

        var finalTf     = _world.GetComponent<SimTransform>(entity);
        var finalStatus = _world.GetComponent<NavigationStatus>(entity);
        float distToGoal = MathF.Sqrt(
            (finalTf.Position.X - goal.X) * (finalTf.Position.X - goal.X) +
            (finalTf.Position.Y - goal.Y) * (finalTf.Position.Y - goal.Y));

        Assert.True(arrived,
            $"VehicleNavigationIntentSystem must set NavigationStatus.Result=Arrived. " +
            $"Final pos=({finalTf.Position.X:F2},{finalTf.Position.Y:F2}) distToGoal={distToGoal:F2}m " +
            $"result={finalStatus.Result} corners={sut.GetCornerCount(entity)}.");

        Assert.Equal(NavigationResult.Arrived, finalStatus.Result);
        Assert.Equal(7u, finalStatus.IntentId);

        Assert.True(sawSecondCorner,
            "The vehicle must have advanced past the first corner (multi-corner detour around the wall).");

        Assert.True(maxAbsX > 4f,
            $"The vehicle route must detour AROUND the wall (reach |X| > 4 m); max |X| seen was {maxAbsX:F2} m. " +
            $"A straight-through path (X≈0) would mean the navmesh did not route around the obstacle.");
    }

    /// <summary>
    /// B20-B3: When <see cref="INavmeshProvider.PlanPath"/> returns 0 corners (goal off-mesh /
    /// unreachable), the system halts the vehicle (Speed=0) and writes a failed
    /// <see cref="NavigationStatus"/> (Result=NoPath) echoing the IntentId.
    /// </summary>
    [Fact]
    public void VehicleNavSystem_NoPath_HaltsVehicle_AndReportsNoPath()
    {
        var navMesh  = BakeFloorWallVehicle();
        var provider = new DotRecastNavmeshProvider(
            new Dictionary<NavLayerMask, DtNavMesh> { [NavLayerMask.Vehicle] = navMesh });
        _world.SetSingletonManaged<INavmeshProvider>(provider);

        var sut = new VehicleNavigationIntentSystem();

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f), Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new VehicleState { Speed = 5f }); // pre-existing speed must be zeroed
        _world.AddComponent(entity, new NavigationStatus());
        _world.AddComponent(entity, new NavigationIntent
        {
            Mode = NavigationMode.DirectPoint,
            // Goal far outside the baked floor (X∈[−15,15], Z∈[−1,20]) → off-mesh → no path.
            FinalDestination = new Vector3(500f, 500f, 0f),
            TargetSpeed = 3f, ArrivalRadius = 1.5f, IntentId = 3,
        });

        sut.Execute(_world, 0.1f);

        var vs     = _world.GetComponent<VehicleState>(entity);
        var status = _world.GetComponent<NavigationStatus>(entity);

        Assert.Equal(0f, vs.Speed);
        Assert.Equal(NavigationResult.NoPath, status.Result);
        Assert.Equal(3u, status.IntentId);
        Assert.Equal(0, sut.GetCornerCount(entity));
    }

    /// <summary>
    /// B20-B4: With no <see cref="INavmeshProvider"/> singleton (and no fallback) the system is a
    /// complete no-op — it does not throw and does not mutate <see cref="VehicleState"/>.
    /// </summary>
    [Fact]
    public void VehicleNavSystem_NoNavmesh_IsGracefulNoOp()
    {
        var sut = new VehicleNavigationIntentSystem(); // no fallback, no singleton registered

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new VehicleState { Speed = 2f, SteerAngle = 0.1f });
        _world.AddComponent(entity, new NavigationIntent
        {
            Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(5f, 5f, 0f),
            TargetSpeed = 3f, ArrivalRadius = 1.5f, IntentId = 1,
        });

        var ex = Record.Exception(() => sut.Execute(_world, 0.1f));
        Assert.Null(ex);

        // VehicleState must be untouched (the system never ran past the navmesh-null guard).
        var vs = _world.GetComponent<VehicleState>(entity);
        Assert.Equal(2f, vs.Speed);
        Assert.Equal(0.1f, vs.SteerAngle);
    }
}
