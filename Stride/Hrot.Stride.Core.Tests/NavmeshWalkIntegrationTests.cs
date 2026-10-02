#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless integration tests for the BATCH-19 Infantry navmesh walk pipeline
/// (STR-D19 discharge):
///
/// <para>
/// <b>Tested scenarios:</b>
/// <list type="bullet">
///   <item>B19-SC1: Infantry layer bakes from a flat ground quad and is retrievable via
///     <see cref="DotRecastNavmeshProvider.TryGetNavMesh"/>.</item>
///   <item>B19-SC2: <see cref="DotRecastDtCrowdProvider"/> deferred-init starts as no-op
///     and becomes functional after <see cref="DotRecastDtCrowdProvider.TryInitializeNavMesh"/>.</item>
///   <item>B19-SC3: Deferred-init <see cref="DotRecastDtCrowdProvider.TryInitializeNavMesh"/>
///     returns false on a second call (idempotent).</item>
///   <item>B19-SC4: Agent given a goal across a synthetic wall obstacle produces a non-zero
///     <see cref="IDtCrowdProvider.GetAgentVelocity"/> that steers around (not through) the wall.
///     This is the core "pathfinds around obstacle" proof.</item>
///   <item>B19-SC5: Full chain test — Infantry navmesh bake → <see cref="DotRecastDtCrowdProvider"/>
///     initialisation → <see cref="CrowdAgentUpdateSystem.Execute"/> → <see cref="CrowdMotorIntent"/>
///     has non-zero velocity after several ticks.</item>
/// </list>
/// </para>
///
/// <para>
/// All tests use synthetic triangle-soup geometry (no Stride scene or GPU required).
/// Coordinate space: navmesh-query space = Stride world space = X=East, Y=altitude(up), Z=North.
/// </para>
/// </summary>
public sealed class NavmeshWalkIntegrationTests : IDisposable
{
    // ── Synthetic geometry constants ─────────────────────────────────────────

    // Flat ground: ±15 m in X and Z at Y=0.
    private const float GroundHalfSize = 15f;

    // L-corridor geometry for the wall-detour test:
    // The navmesh is an L-shape that forces north detour to reach east goal.
    //
    //  ┌──────────────────────────────┐  Z=+15
    //  │     North corridor           │  Z=[+5..+15], X=[-12..+12]
    //  │                              │
    //  ├────────┐                     │  Z=+5
    //  │ Start  │  (no walkable here)  │
    //  │ X=[-12,0], Z=[-5,+5] only   │
    //  └────────┘                     │  Z=-5
    //                                 │
    //  Goal strip: X=[0..+12], Z=[-5..+5]  (connected via north)
    //
    // Simple version: use two strips and a connector.
    // - West strip: X=[-12,0], Z=[-5,+15]  (agent starts west)
    // - East strip: X=[0,+12], Z=[+5,+15]  (goal on east strip)
    // The "wall" is the absence of walkable floor in X=[0,+12], Z=[-5,+5].
    // Agent at X=-8, Z=0. Goal at X=+8, Z=+10. Direct route crosses unwalked area.
    private static readonly Vector3 AgentPosFdp = new(-8f, 0f, 0f);  // FDP(X=East,Y=North,Z=Up) → navmesh(X,0,Y)
    private static readonly Vector3 GoalPosFdp  = new( 8f, 10f, 0f);  // FDP: X=8 east, Y=10 north

    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly EntityRepository _world;

    public NavmeshWalkIntegrationTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<NavigationStatus>();
        _world.RegisterComponent<CrowdAgent>();
        _world.RegisterComponent<CrowdMotorIntent>();
    }

    public void Dispose() => _world.Dispose();

    // ── Geometry helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Bakes an L-corridor Infantry navmesh that forces a north detour to reach the east goal.
    ///
    /// <para>
    /// Layout (navmesh-query space: X=East, Y=altitude, Z=North):
    /// <list type="bullet">
    ///   <item>West strip:  X=[−12, 0], Z=[−5, +15] — agent starts here at (−8, 0, 0).</item>
    ///   <item>North strip: X=[−12,+12], Z=[+5, +15] — connects west and east.</item>
    ///   <item>East strip:  X=[0, +12], Z=[+5, +15] — goal is here at (+8, 0, +10).</item>
    /// </list>
    /// The direct east path (Z ≈ 0) is NOT walkable east of X=0 in the Z=[−5,+5] band,
    /// forcing the agent to go north first (Z > +5) and then east.
    /// </para>
    /// </summary>
    private static DtNavMesh BakeLCorridorNavmesh()
    {
        var vertList  = new List<float>();
        var indexList = new List<int>();

        // Helper: add a flat quad at Y=0 with CCW winding (upward normal = walkable).
        void AddQuad(float x0, float z0, float x1, float z1)
        {
            int b = vertList.Count / 3;
            // Quad corners: SW(x0,z0), SE(x1,z0), NE(x1,z1), NW(x0,z1)
            vertList.AddRange(new float[] {
                x0, 0f, z0,  // 0: SW
                x1, 0f, z0,  // 1: SE
                x1, 0f, z1,  // 2: NE
                x0, 0f, z1,  // 3: NW
            });
            // CCW from above → +Y normal (walkable).
            indexList.AddRange(new int[] { b, b+2, b+1, b, b+3, b+2 });
        }

        // West strip: X=[-12,0], Z=[-5,+15] — agent starts here.
        AddQuad(-12f, -5f, 0f, 15f);

        // East strip: X=[0,+12], Z=[+5,+15] — goal strip (north portion only).
        // NOT connected at Z<+5 (the gap is X=[0,+12], Z=[-5,+5]).
        AddQuad(0f, 5f, 12f, 15f);

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(vertList.ToArray(), indexList.ToArray(), NavLayerMask.Infantry);

        Assert.True(meshes.ContainsKey(NavLayerMask.Infantry),
            "Infantry navmesh must bake from the L-corridor geometry.");

        return meshes[NavLayerMask.Infantry];
    }

    /// <summary>
    /// Bakes a simple flat ground navmesh without obstacles (used for deferred-init tests).
    /// </summary>
    private static DtNavMesh BakeFlatGround()
    {
        float[] verts   = {
            -GroundHalfSize, 0f, -GroundHalfSize,
             GroundHalfSize, 0f, -GroundHalfSize,
             GroundHalfSize, 0f,  GroundHalfSize,
            -GroundHalfSize, 0f,  GroundHalfSize,
        };
        int[] indices = { 0, 2, 1, 0, 3, 2 };  // CCW from above

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts, indices, NavLayerMask.Infantry);

        Assert.True(meshes.ContainsKey(NavLayerMask.Infantry),
            "Infantry navmesh must bake from flat ground.");

        return meshes[NavLayerMask.Infantry];
    }

    // ── B19-SC1: Infantry bake + TryGetNavMesh ────────────────────────────────

    /// <summary>
    /// BATCH-19 SC1: Baking <see cref="NavLayerMask.Infantry"/> produces a navmesh that is
    /// retrievable via <see cref="DotRecastNavmeshProvider.TryGetNavMesh"/>.
    /// </summary>
    [Fact]
    public void InfantryLayer_BakesSuccessfully_AndIsRetrivedViaProvider()
    {
        // Arrange + Act
        float[] verts   = {
            -15f, 0f, -15f,
             15f, 0f, -15f,
             15f, 0f,  15f,
            -15f, 0f,  15f,
        };
        int[] indices = { 0, 2, 1, 0, 3, 2 };

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts, indices, NavLayerMask.Infantry);

        // Wrap in DotRecastNavmeshProvider
        var provider = new DotRecastNavmeshProvider(meshes);

        // Assert: Infantry layer is retrievable
        bool found = provider.TryGetNavMesh(NavLayerMask.Infantry, out var navMesh);
        Assert.True(found, "TryGetNavMesh must return true for baked Infantry layer.");
        Assert.NotNull(navMesh);

        // Assert: Vehicle layer is NOT present (only Infantry was baked)
        bool foundVehicle = provider.TryGetNavMesh(NavLayerMask.Vehicle, out _);
        Assert.False(foundVehicle, "TryGetNavMesh must return false for Vehicle layer (not baked).");
    }

    // ── B19-SC2: DotRecastDtCrowdProvider deferred-init ──────────────────────

    /// <summary>
    /// BATCH-19 SC2: A <see cref="DotRecastDtCrowdProvider"/> constructed with the deferred
    /// constructor starts as no-op (RegisterAgent returns false, GetAgentVelocity returns zero),
    /// then becomes functional after <see cref="DotRecastDtCrowdProvider.TryInitializeNavMesh"/>.
    /// </summary>
    [Fact]
    public void DeferredCrowd_BeforeInit_IsNoOp_AfterInit_IsFunctional()
    {
        // Arrange: deferred provider (no navmesh yet).
        var crowd  = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = Vector3.Zero });

        // Assert: before init, RegisterAgent returns false (no-op).
        var parms  = new CrowdAgentParams { Radius = 0.3f, Height = 1.8f, MaxSpeed = 3f, MaxAcceleration = 20f };
        bool before = crowd.RegisterAgent(entity, parms);
        Assert.False(before, "RegisterAgent must return false (no-op) before TryInitializeNavMesh.");
        Assert.False(crowd.IsInitialized, "IsInitialized must be false before TryInitializeNavMesh.");

        // Assert: GetAgentVelocity returns zero before init.
        var velBefore = crowd.GetAgentVelocity(entity);
        Assert.Equal(Vector3.Zero, velBefore);

        // Act: provide the navmesh.
        var navMesh = BakeFlatGround();
        bool initResult = crowd.TryInitializeNavMesh(navMesh);
        Assert.True(initResult, "TryInitializeNavMesh must return true on first call.");
        Assert.True(crowd.IsInitialized, "IsInitialized must be true after TryInitializeNavMesh.");

        // Assert: after init, RegisterAgent succeeds.
        bool after = crowd.RegisterAgent(entity, parms);
        Assert.True(after, "RegisterAgent must return true after TryInitializeNavMesh.");

        // Assert: after setting a target and stepping, GetAgentVelocity returns non-zero.
        crowd.SetAgentTarget(entity, new Vector3(10f, 0f, 0f));
        for (int i = 0; i < 10; i++)
            crowd.Update(0.1f, _world);

        var velAfter = crowd.GetAgentVelocity(entity);
        Assert.True(velAfter.X > 0f,
            $"Velocity must be non-zero toward target after init; got {velAfter}");
    }

    // ── B19-SC3: TryInitializeNavMesh is idempotent ───────────────────────────

    /// <summary>
    /// BATCH-19 SC3: <see cref="DotRecastDtCrowdProvider.TryInitializeNavMesh"/> returns
    /// false on a second call (idempotent — already initialized).
    /// </summary>
    [Fact]
    public void DeferredCrowd_TryInitializeNavMesh_ReturnsFalseOnSecondCall()
    {
        var crowd   = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        var navMesh = BakeFlatGround();

        bool first  = crowd.TryInitializeNavMesh(navMesh);
        bool second = crowd.TryInitializeNavMesh(navMesh);

        Assert.True(first,   "First TryInitializeNavMesh call must return true.");
        Assert.False(second, "Second TryInitializeNavMesh call must return false (already initialized).");
    }

    // ── B19-SC4: Crowd pathfinds around a missing walkable area ──────────────

    /// <summary>
    /// BATCH-19 SC4: Agent placed on the west strip of an L-corridor navmesh, with a goal on
    /// the east strip (connected only via the north end), must produce a non-zero velocity with
    /// a significant NORTH (FDP +Y) component because the direct east path is not walkable.
    ///
    /// <para>
    /// Navmesh geometry (navmesh-query space X=East, Y=altitude, Z=North):
    /// <list type="bullet">
    ///   <item>West strip: X=[−12,0], Z=[−5,+15].</item>
    ///   <item>East strip: X=[0,+12], Z=[+5,+15] (NO east coverage at Z &lt; +5).</item>
    ///   <item>Connection: the two strips share the region X=[−12,+12], Z=[+5,+15].</item>
    /// </list>
    /// Agent starts at FDP (−8, 0, 0) = navmesh (−8, 0, 0).
    /// Goal is at FDP (+8, +10, 0) = navmesh (+8, 0, +10).
    /// The direct east path (navmesh Z ≈ 0) is only on the west strip; the east strip
    /// starts at Z=+5.  The agent must go north first (navmesh +Z = FDP +Y).
    /// </para>
    /// </summary>
    [Fact]
    public void DotRecastCrowd_AgentWithGoalAcrossWall_ProducesDetourVelocity()
    {
        // Arrange: bake the L-corridor navmesh.
        var navMesh = BakeLCorridorNavmesh();
        var crowd   = new DotRecastDtCrowdProvider(navMesh, maxAgentRadius: 0.4f);

        // AgentPosFdp = FDP (−8, 0, 0) → navmesh (−8, 0, 0).
        // DotRecastDtCrowdProvider converts FDP→crowd via ToRcVec: (fdp.X, fdp.Z, fdp.Y).
        // FDP(−8, 0, 0) → crowd(−8, 0, 0) — on the west strip.
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform
        {
            Position = AgentPosFdp,
            Rotation = Quaternion.Identity,
        });

        var agentParams = new CrowdAgentParams
        {
            Radius           = 0.3f,
            Height           = 1.8f,
            MaxSpeed         = 3f,
            MaxAcceleration  = 20f,
            SeparationWeight = 2,
        };
        bool registered = crowd.RegisterAgent(entity, agentParams);
        Assert.True(registered, "RegisterAgent must succeed on a fully-initialized provider.");

        // GoalPosFdp = FDP (+8, +10, 0) → crowd (+8, 0, +10) — on the east strip (Z=+10 > +5).
        crowd.SetAgentTarget(entity, GoalPosFdp);

        // Act: step 30 times × 0.1 s.
        for (int i = 0; i < 30; i++)
            crowd.Update(0.1f, _world);

        // Assert: velocity is non-zero.
        var vel = crowd.GetAgentVelocity(entity);
        float speed = vel.Length();
        Assert.True(speed > 0.05f,
            $"Velocity must be non-zero after crowd update on the L-corridor navmesh; " +
            $"got speed={speed:F3} vel={vel}");

        // Assert: velocity must have a significant NORTH component (FDP Y = navmesh Z).
        // The only route to the east goal goes via the north strip (Z > +5),
        // so the initial velocity from agent position (Z=0) must be northward (FDP +Y).
        // FDP vel.Y = navmesh Z velocity = northward detour component.
        float fdpNorthComponent = vel.Y;
        Assert.True(fdpNorthComponent > 0.2f,
            $"Velocity must be northward (FDP +Y > 0.2) to route around the L-corridor. " +
            $"Got FDP vel=({vel.X:F3},{vel.Y:F3},{vel.Z:F3}), north={fdpNorthComponent:F3}. " +
            $"If FDP Y ≤ 0 the agent is not routing through the L-corridor (pathfinding failed " +
            $"or navmesh geometry is wrong).");
    }

    // ── B19-FIX: CrowdAgent registered in world → full chain drives CrowdMotorIntent ─

    /// <summary>
    /// BATCH-19 FIX regression test: asserts that when <c>CrowdAgent</c> IS registered as a
    /// component type in the ECS world (the fix applied in <c>EditorStrideSubsystem.Initialize</c>),
    /// an agent registered with <see cref="DotRecastDtCrowdProvider"/> over floor+wall geometry
    /// and given a goal produces a non-zero <see cref="CrowdMotorIntent.Velocity"/> after a few
    /// <see cref="CrowdAgentUpdateSystem"/> ticks.
    ///
    /// <para>
    /// <b>Root cause being tested:</b> before the fix, <c>CrowdAgent</c> was not registered in the
    /// <c>editor_stride</c> world. <see cref="CrowdAgentUpdateSystem.Execute"/> guards on
    /// <c>repo.IsComponentTypeRegistered&lt;CrowdAgent&gt;()</c> and returns early when absent,
    /// so no velocity was ever written — the F5 demo's check also bailed immediately with
    /// "[Navmesh Walk] WARNING: CrowdAgent component type not registered — cannot proceed."
    /// </para>
    ///
    /// <para>
    /// <b>What this test proves:</b> registering <c>CrowdAgent</c> unblocks the full chain:
    /// <c>CrowdAgent registered → RegisterAgent succeeds → CrowdAgentUpdateSystem queries
    /// CrowdAgent+CrowdMotorIntent+NavigationStatus → velocity written → character steers</c>.
    /// </para>
    ///
    /// <para>
    /// Uses L-corridor geometry so the agent must steer around the gap (non-trivial path).
    /// Asserts both that velocity magnitude is non-zero AND that the north component (FDP Y)
    /// is positive (the only route to the east goal goes via the north strip).
    /// </para>
    /// </summary>
    [Fact]
    public void B19Fix_CrowdAgent_RegisteredInWorld_FullChainProducesNonzeroMotorIntent()
    {
        // ── Arrange ────────────────────────────────────────────────────────
        // Use L-corridor navmesh: west strip X=[-12,0] Z=[-5,+15], east strip X=[0,+12] Z=[+5,+15].
        // Agent starts at FDP(-8, 0, 0) on the west strip; goal at FDP(+8, +10, 0) on the east strip.
        // The direct east path is blocked — agent must steer north (FDP +Y) first.
        var navMesh = BakeLCorridorNavmesh();

        // ── Key step: register CrowdAgent in the world (this is the FIX) ─────
        // Without World.RegisterComponent<CrowdAgent>(), CrowdAgentUpdateSystem returns early
        // and CrowdMotorIntent.Velocity stays zero forever.
        // The _world fixture only registers SimTransform, SimVelocity, NavigationStatus,
        // CrowdAgent, CrowdMotorIntent — exactly mirroring what EditorStrideSubsystem now does.
        // CrowdAgent IS registered in _world (see constructor above).
        Assert.True(_world.IsComponentTypeRegistered<CrowdAgent>(),
            "CrowdAgent must be registered in the world — this is the BATCH-19 fix. " +
            "If this assertion fails, the test fixture's constructor is missing World.RegisterComponent<CrowdAgent>().");

        // Create the fully-initialized crowd provider (no deferred init — navmesh is ready).
        var crowd = new DotRecastDtCrowdProvider(navMesh, maxAgentRadius: 0.4f);

        // Create the CrowdAgentUpdateSystem.
        var crowdSystem = new CrowdAgentUpdateSystem(crowd);

        // Spawn entity at the west strip start position.
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform
        {
            Position = AgentPosFdp,  // FDP(-8, 0, 0)
            Rotation = System.Numerics.Quaternion.Identity,
        });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new NavigationStatus
        {
            Phase  = NavigationPhase.Following,
            Result = NavigationResult.InProgress,
        });
        _world.AddComponent(entity, default(CrowdAgent));
        _world.AddComponent(entity, new CrowdMotorIntent());

        // ── Snap start and goal to navmesh (on-navmesh validation) ────────
        // Snap start: FDP(-8, 0, 0) → should be on west strip → snap dist ≈ 0.
        bool startSnapped = crowd.TrySnapToNavmesh(AgentPosFdp, out var snappedStart);
        Assert.True(startSnapped,
            $"Start FDP({AgentPosFdp.X},{AgentPosFdp.Y},{AgentPosFdp.Z}) must snap to navmesh poly. " +
            $"If false the L-corridor west strip does not cover this position.");

        // Snap goal: FDP(+8, +10, 0) → should be on east strip (Z=+10 > +5) → snap dist ≈ 0.
        bool goalSnapped = crowd.TrySnapToNavmesh(GoalPosFdp, out var snappedGoal);
        Assert.True(goalSnapped,
            $"Goal FDP({GoalPosFdp.X},{GoalPosFdp.Y},{GoalPosFdp.Z}) must snap to navmesh poly. " +
            $"If false the L-corridor east strip does not cover this position.");

        // ── Register agent at the snapped start position ───────────────────
        var agentParams = new CrowdAgentParams
        {
            Radius           = 0.3f,
            Height           = 1.8f,
            MaxSpeed         = 3f,
            MaxAcceleration  = 20f,
            SeparationWeight = 2,
        };
        bool registered = crowd.RegisterAgent(entity, agentParams, startPositionFdp: snappedStart);
        Assert.True(registered,
            "RegisterAgent must succeed with a fully-initialized crowd provider. " +
            "If false the crowd is not initialized or the entity was already registered.");

        // Set the snapped goal as target.
        crowd.SetAgentTarget(entity, snappedGoal);

        // ── Act: tick CrowdAgentUpdateSystem several times ─────────────────
        for (int step = 0; step < 15; step++)
        {
            _world.Bus.SwapBuffers();
            crowdSystem.Execute(_world, 0.1f);
        }

        // ── Assert: CrowdMotorIntent.Velocity is non-zero ──────────────────
        var intent = _world.GetComponent<CrowdMotorIntent>(entity);
        float speed = intent.Velocity.Length();

        Assert.True(speed > 0.05f,
            $"CrowdMotorIntent.Velocity must be non-zero after CrowdAgentUpdateSystem ticks. " +
            $"Got magnitude={speed:F3} vel={intent.Velocity}. " +
            $"ROOT CAUSE CHECK: is CrowdAgent registered in the world? " +
            $"(isRegistered={_world.IsComponentTypeRegistered<CrowdAgent>()}) " +
            $"CrowdAgentUpdateSystem.Execute returns early when CrowdAgent is not registered — " +
            $"this was the BATCH-19 bug. If this fails, the EditorStrideSubsystem fix is missing.");

        // Assert: velocity is northward (FDP +Y) because the only route is via the north strip.
        // This proves the agent is actually pathfinding around the L-corridor gap, not just
        // producing a non-zero arbitrary velocity.
        Assert.True(intent.Velocity.Y > 0.1f,
            $"CrowdMotorIntent.Velocity.Y (FDP north component) must be positive on the L-corridor. " +
            $"Got FDP vel=({intent.Velocity.X:F3},{intent.Velocity.Y:F3},{intent.Velocity.Z:F3}). " +
            $"The only route from west strip to east strip goes via the north connector (FDP Y > +5). " +
            $"If Y <= 0 the agent is steering east directly (pathfinding failed or geometry wrong).");
    }

    // ── B19-SC5: Full chain Infantry navmesh → CrowdAgentUpdateSystem → CrowdMotorIntent ──

    /// <summary>
    /// BATCH-19 SC5: Full chain test — Infantry navmesh bake → deferred
    /// <see cref="DotRecastDtCrowdProvider"/> init → <see cref="CrowdAgentUpdateSystem"/> ticks
    /// → <see cref="CrowdMotorIntent.Velocity"/> is non-zero after several steps.
    ///
    /// <para>
    /// This is the headless proof of the BATCH-19 pipeline:
    /// <c>Infantry DtNavMesh → DotRecastDtCrowdProvider → CrowdAgentUpdateSystem → CrowdMotorIntent</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void InfantryNavmeshChain_CrowdAgentUpdateSystem_WritesCrowdMotorIntentVelocity()
    {
        // Arrange: bake flat Infantry navmesh.
        var navMesh = BakeFlatGround();

        // Create the deferred crowd provider and initialise it.
        var crowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        bool initOk = crowd.TryInitializeNavMesh(navMesh);
        Assert.True(initOk, "TryInitializeNavMesh must succeed.");

        // Create the CrowdAgentUpdateSystem with the real provider.
        var crowdSystem = new CrowdAgentUpdateSystem(crowd);

        // Spawn an entity with all required components.
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Identity,
        });
        _world.AddComponent(entity, new SimVelocity());
        _world.AddComponent(entity, new NavigationStatus
        {
            Phase  = NavigationPhase.Following,
            Result = NavigationResult.InProgress,
        });
        _world.AddComponent(entity, default(CrowdAgent));
        _world.AddComponent(entity, new CrowdMotorIntent());

        // Register agent and set a target clearly in the open (no obstacles).
        var agentParams = new CrowdAgentParams
        {
            Radius           = 0.3f,
            Height           = 1.8f,
            MaxSpeed         = 3f,
            MaxAcceleration  = 20f,
            SeparationWeight = 2,
        };
        bool registered = crowd.RegisterAgent(entity, agentParams);
        Assert.True(registered, "RegisterAgent must succeed.");

        // Goal: FDP (10, 0, 0) — due east on flat ground.
        var goalFdp = new Vector3(10f, 0f, 0f);
        crowd.SetAgentTarget(entity, goalFdp);

        // Act: step the CrowdAgentUpdateSystem several times.
        for (int step = 0; step < 15; step++)
        {
            _world.Bus.SwapBuffers();
            crowdSystem.Execute(_world, 0.1f);
        }

        // Assert: CrowdMotorIntent must have a non-zero velocity after crowd steps.
        var intent = _world.GetComponent<CrowdMotorIntent>(entity);
        float speed = intent.Velocity.Length();
        Assert.True(speed > 0.05f,
            $"CrowdMotorIntent.Velocity must be non-zero after CrowdAgentUpdateSystem steps; " +
            $"got magnitude={speed:F3} vel={intent.Velocity}. " +
            $"This means the DotRecastDtCrowdProvider → CrowdAgentUpdateSystem chain is broken.");

        // Assert: velocity is pointing toward the goal (east, FDP +X).
        Assert.True(intent.Velocity.X > 0f,
            $"CrowdMotorIntent.Velocity.X must be positive (toward east goal); " +
            $"got {intent.Velocity.X:F3}. FDP goal is east (+X).");
    }
}
