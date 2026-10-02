#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.Core.Network;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless regression tests for STR-D21 (BATCH-24):
/// F6 bridge-retry fix and F7 VehicleNavSystem pre-motor tick fix.
///
/// <para>
/// <b>F6 — bridge retry on deferred crowd:</b>
/// <see cref="NavigationIntentBridgeSystem"/> must NOT cache the ActionInstanceId when
/// <see cref="IDtCrowdProvider.RegisterAgent"/> returns false (crowd not yet initialized).
/// On the next tick (after the crowd is initialized) it must retry and successfully
/// register the agent.
/// </para>
///
/// <para>
/// <b>F7 — VehicleNavigationIntentSystem pre-motor:</b>
/// <see cref="EditorStrideSubsystem.Tick"/> must call
/// <see cref="VehicleNavigationIntentSystem.Execute"/> at Step 2b (BEFORE
/// <c>KinematicVehicleMotor.Execute</c>) so the motor reads freshly-written
/// <see cref="VehicleState"/> on the same frame it was planned, eliminating the 1-tick
/// lag that caused the APC to stay frozen when the physics body was in deferred-init mode.
/// </para>
/// </summary>
public sealed class StrD21NavigationFixTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public StrD21NavigationFixTests()
    {
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();
    }

    public void Dispose() => _sut.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Entity SpawnAndPump(long tkbType, Vector3 pos)
    {
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = tkbType,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = pos },
                new SimVelocity(),
            },
        });
        // Pump 3 frames to materialise.
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);

        var entity = _sut.World.Query().With<SimTransform>().Build().FirstOrNull();
        Assert.False(entity == Entity.Null, "Entity must have spawned.");
        return entity;
    }

    // ── STR-D21-F6-1: Bridge retries when crowd not initialized ────────────────

    /// <summary>
    /// When <see cref="IDtCrowdProvider"/> returns false from
    /// <see cref="IDtCrowdProvider.RegisterAgent"/> (deferred init — crowd = no-op),
    /// the bridge must NOT cache the <c>ActionInstanceId</c>.
    /// On the next tick the bridge must process the same action again.
    ///
    /// <para>
    /// This test uses a <see cref="FakeDtCrowdProvider"/> that starts returning false and
    /// is then switched to return true, simulating the BakeNavmesh() initialization sequence.
    /// </para>
    /// </summary>
    [Fact]
    public void BridgeRetry_WhenCrowdNotInitialized_RetriesOnNextTick()
    {
        // Arrange: set up a spy crowd that first returns false, then true.
        var spyCrowd = new SpyDeferredCrowd();
        var bridge   = new NavigationIntentBridgeSystem(null, spyCrowd);

        var repo = new EntityRepository();
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CrowdAgent>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });

        // Issue a MoveTo (ActionInstanceId = 1).
        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(10f, 10f, 0f), speed: 5f, arrivalRadius: 1.5f);

        // Act, tick 1: crowd not yet initialized → RegisterAgent returns false.
        spyCrowd.ShouldReturnTrue = false;
        bridge.Execute(repo, 1f / 60f);

        // Assert: agent NOT yet registered, and the call was attempted.
        Assert.True(spyCrowd.RegisterAttempts >= 1,
            "Bridge must have attempted RegisterAgent on tick 1.");
        Assert.False(spyCrowd.TryGetAgentSnapshot(entity, out _),
            "Agent must NOT be in crowd after tick 1 (crowd returned false).");

        // Act, tick 2: crowd now initialized → RegisterAgent returns true.
        spyCrowd.ShouldReturnTrue = true;
        bridge.Execute(repo, 1f / 60f);

        // Assert: agent IS now registered (bridge retried on tick 2).
        Assert.True(spyCrowd.RegisterAttempts >= 2,
            "Bridge must have retried RegisterAgent on tick 2.");
        Assert.True(spyCrowd.TryGetAgentSnapshot(entity, out _),
            "Agent must be in crowd after tick 2 (crowd initialized, bridge retried).");
    }

    // ── STR-D21-F6-2: Bridge does NOT retry when already registered ────────────

    /// <summary>
    /// When the crowd returns false because the entity is ALREADY registered
    /// (second call for the same entity), the bridge must still update the target
    /// but must NOT create duplicate registrations.
    /// </summary>
    [Fact]
    public void BridgeRetry_WhenAlreadyRegistered_UpdatesTargetWithoutDuplicating()
    {
        var spyCrowd = new SpyDeferredCrowd { ShouldReturnTrue = true };
        var bridge   = new NavigationIntentBridgeSystem(null, spyCrowd);

        var repo = new EntityRepository();
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CrowdAgent>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });

        // Issue first MoveTo (ActionInstanceId = 1).
        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(5f, 5f, 0f), speed: 5f, arrivalRadius: 1.5f);
        bridge.Execute(repo, 1f / 60f);

        Assert.Equal(1, spyCrowd.RegisterAttempts);
        Assert.True(spyCrowd.TryGetAgentSnapshot(entity, out _));

        // Issue second MoveTo (ActionInstanceId = 2, new goal).
        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(10f, 10f, 0f), speed: 5f, arrivalRadius: 1.5f);
        // Make RegisterAgent return false (already registered).
        spyCrowd.ShouldReturnTrue = false;
        bridge.Execute(repo, 1f / 60f);

        // Target must be updated, agent count must remain 1.
        Assert.Equal(1, spyCrowd.AgentCount);
        Assert.Equal(new Vector3(10f, 10f, 0f), spyCrowd.LastSetAgentTarget);
    }

    // ── STR-D21-F6-3: Bridge skips caching for vehicles (no VehicleState guard) ──

    /// <summary>
    /// Entities WITH <see cref="VehicleState"/> must NOT be crowd-registered by the bridge
    /// (infantry-only path). The bridge must cache their ActionInstanceId normally
    /// (they are processed for route publishing, just not crowd-enrolled).
    /// </summary>
    [Fact]
    public void Bridge_VehicleEntity_SkipsCrowdRegistration()
    {
        var spyCrowd = new SpyDeferredCrowd { ShouldReturnTrue = true };
        var bridge   = new NavigationIntentBridgeSystem(null, spyCrowd);

        var repo = new EntityRepository();
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<VehicleState>();
        repo.RegisterComponent<CrowdAgent>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        repo.AddComponent(entity, new VehicleState());   // marks as vehicle

        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(8f, 8f, 0f), speed: 3f, arrivalRadius: 1.5f);

        bridge.Execute(repo, 1f / 60f);

        // Vehicle entity must NOT have been registered in the crowd.
        Assert.Equal(0, spyCrowd.RegisterAttempts);
        Assert.False(spyCrowd.TryGetAgentSnapshot(entity, out _));
    }

    // ── STR-D20/BATCH-25-B1: Infantry without VehicleState IS registered by bridge ────

    /// <summary>
    /// Root-cause regression test for STR-D20 / BATCH-25 Part B:
    /// when an infantry entity does NOT carry <see cref="VehicleState"/> (after the
    /// harness strips it), <see cref="NavigationIntentBridgeSystem"/> MUST crowd-register it.
    ///
    /// <para>
    /// Before the fix, <c>VehicleKinematicsTkbTranslator</c> injected <see cref="VehicleState"/>
    /// on every TKB-spawned entity (incl. infantry), and the bridge excluded them with
    /// <c>!HasComponent&lt;VehicleState&gt;</c> — so the mannequin was never enrolled.
    /// </para>
    /// </summary>
    [Fact]
    public void Bridge_InfantryWithoutVehicleState_IsCrowdRegistered()
    {
        var spyCrowd = new SpyDeferredCrowd { ShouldReturnTrue = true };
        var bridge   = new NavigationIntentBridgeSystem(null, spyCrowd);

        var repo = new EntityRepository();
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CrowdAgent>();
        // NOTE: VehicleState is intentionally NOT registered here (stripped by harness).

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });

        // Issue MoveTo — no VehicleState on the entity.
        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(10f, 10f, 0f), speed: 2f, arrivalRadius: 1.5f);

        bridge.Execute(repo, 1f / 60f);

        // Without VehicleState the bridge must crowd-register the infantry entity.
        Assert.Equal(1, spyCrowd.RegisterAttempts);
        Assert.True(spyCrowd.TryGetAgentSnapshot(entity, out _),
            "Infantry entity without VehicleState must be crowd-registered by the bridge.");
    }

    // ── STR-D20/BATCH-25-B2: Infantry WITH VehicleState is still excluded ────────

    /// <summary>
    /// Complementary check: an infantry entity that still carries <see cref="VehicleState"/>
    /// (the old broken state) must NOT be crowd-registered — confirming the guard works.
    /// </summary>
    [Fact]
    public void Bridge_InfantryWithVehicleState_IsNotCrowdRegistered()
    {
        var spyCrowd = new SpyDeferredCrowd { ShouldReturnTrue = true };
        var bridge   = new NavigationIntentBridgeSystem(null, spyCrowd);

        var repo = new EntityRepository();
        repo.RegisterComponent<LocomotionChannel>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<VehicleState>();  // present = old broken state
        repo.RegisterComponent<CrowdAgent>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        repo.AddComponent(entity, new VehicleState()); // marks as "vehicle" to the bridge

        FdpNavigationOrders.IssueMoveTo(repo, entity,
            new Vector3(10f, 10f, 0f), speed: 2f, arrivalRadius: 1.5f);

        bridge.Execute(repo, 1f / 60f);

        // Entity with VehicleState must NOT be crowd-registered.
        Assert.Equal(0, spyCrowd.RegisterAttempts);
        Assert.False(spyCrowd.TryGetAgentSnapshot(entity, out _),
            "Infantry entity WITH VehicleState must NOT be crowd-registered (bridge's vehicle guard).");
    }

    // ── BATCH-25-C1: NavigationExecutionSystem skips VehicleState entities ────────

    /// <summary>
    /// Root-cause regression test for STR-D21 BATCH-25 Part C:
    /// <see cref="NavigationExecutionSystem"/> must NOT apply the frustration-tick guard
    /// to entities that carry <see cref="VehicleState"/>.
    ///
    /// <para>
    /// Without the fix, an APC with a freshly-set <see cref="NavigationIntent"/> would
    /// accumulate <see cref="FrustrationTicks"/> while <c>rb.Simulation == null</c> (Bullet
    /// body not yet in simulation → <c>SetLinearVelocityXZ</c> no-op → <c>SimVelocity=0</c>),
    /// and after 120 ticks the system would write <see cref="NavigationResult.FailedBlocked"/>,
    /// halting the vehicle permanently.
    /// </para>
    ///
    /// <para>
    /// With the fix (<c>.Without&lt;VehicleState&gt;()</c> in the query), the vehicle entity
    /// is excluded and its <see cref="NavigationStatus"/> must remain
    /// <see cref="NavigationResult.InProgress"/> for 200 consecutive ticks of zero velocity.
    /// </para>
    /// </summary>
    [Fact]
    public void NavExecSystem_VehicleEntity_IsNotFrustrationBlocked()
    {
        // Arrange
        var navExec = new NavigationExecutionSystem();
        var repo    = new EntityRepository();

        repo.RegisterComponent<NavigationIntent>();
        repo.RegisterComponent<NavigationStatus>();
        repo.RegisterComponent<FrustrationTicks>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<SimVelocity>();
        repo.RegisterComponent<VehicleState>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
        repo.AddComponent(entity, new SimVelocity());          // velocity = (0,0,0) → "stuck"
        repo.AddComponent(entity, new VehicleState());         // marks as vehicle
        repo.AddComponent(entity, new FrustrationTicks());
        repo.AddComponent(entity, new NavigationStatus { IntentId = 0, Result = NavigationResult.InProgress });
        repo.AddComponent(entity, new NavigationIntent
        {
            IntentId         = 1,
            Mode             = NavigationMode.DirectPoint,
            FinalDestination = new Vector3(20f, 20f, 0f),
            ArrivalRadius    = 1.5f,
        });

        // Act: pump 200 ticks with SimVelocity=0 (body not in simulation → stuck appearance).
        // FrustrationTickLimit is 120; without the fix this would fire FailedBlocked at tick 121.
        const float dt = 1f / 60f;
        for (int i = 0; i < 200; i++)
            navExec.Execute(repo, dt);

        // Assert: vehicle entity must NOT have been marked FailedBlocked.
        var status = repo.GetComponent<NavigationStatus>(entity);
        Assert.NotEqual(NavigationResult.FailedBlocked, status.Result);
        Assert.Equal(NavigationResult.InProgress, status.Result);

        // Confirm frustration ticks also not accumulated (entity was skipped entirely).
        var frustration = repo.GetComponent<FrustrationTicks>(entity);
        Assert.Equal(0, frustration.Ticks);
    }

    // ── STR-D21-F7-1: VehicleNavIntentSystem is non-null after Initialize ──────

    /// <summary>
    /// <see cref="EditorStrideSubsystem.VehicleNavIntentSystem"/> must be wired after
    /// Initialize. This is required for the pre-motor step-2b execution added by the fix.
    /// </summary>
    [Fact]
    public void VehicleNavIntentSystem_IsNonNull_AfterInitialize()
    {
        Assert.NotNull(_sut.VehicleNavIntentSystem);
    }

    // ── STR-D21-F7-2: VehicleNavIntentSystem writes VehicleState on the first tick ──

    /// <summary>
    /// After setting a <see cref="NavigationIntent"/> with <c>Mode=DirectPoint</c> on a
    /// vehicle entity and pumping one tick, <see cref="VehicleState.Speed"/> must be
    /// non-zero — proving the system ran <em>before</em> the tick returned (the Step 2b
    /// pre-motor execution delivers freshly-planned VehicleState within the same frame).
    ///
    /// <para>
    /// This test uses a <see cref="SpyNavmeshProvider"/> (returns a straight-line single
    /// corner) so the planner succeeds without a real baked navmesh.
    /// </para>
    /// </summary>
    [Fact]
    public void VehicleNavIntentSystem_WritesVehicleState_OnFirstTick_WithFakeNavmesh()
    {
        // Arrange: create a standalone VehicleNavigationIntentSystem with a spy navmesh.
        var spyNavmesh = new SpyNavmeshProvider();
        var navSystem  = new VehicleNavigationIntentSystem(navmeshFallback: spyNavmesh);

        var repo = new EntityRepository();
        repo.RegisterComponent<NavigationIntent>();
        repo.RegisterComponent<VehicleState>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NavigationStatus>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
        repo.AddComponent(entity, new VehicleState());
        repo.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });

        // Set a DirectPoint NavigationIntent.
        var intent = new NavigationIntent
        {
            IntentId         = 1,
            Mode             = NavigationMode.DirectPoint,
            FinalDestination = new Vector3(10f, 10f, 0f),
            TargetSpeed      = 3f,
            ArrivalRadius    = 1.5f,
        };
        repo.AddComponent(entity, intent);

        // Act: execute the system (simulates the Step 2b pre-motor call).
        navSystem.Execute(repo, 1f / 60f);

        // Assert: VehicleState.Speed must be non-zero — the system wrote steering output.
        var vs = repo.GetComponent<VehicleState>(entity);
        Assert.True(vs.Speed > 0f || vs.SteerAngle != 0f,
            $"VehicleState must be non-zero after VehicleNavSystem.Execute: " +
            $"speed={vs.Speed:F3} steer={vs.SteerAngle:F3}. " +
            "If this fails the system was not called before the motor.");
    }

    // ── STR-D21-F7-3: VehicleNavIntentSystem corners planned and progressing ──

    /// <summary>
    /// After a <see cref="NavigationIntent"/> is set and several ticks are pumped,
    /// the corner index must advance (proving the system keeps steering tick-by-tick)
    /// and <see cref="VehicleState.Speed"/> must remain non-zero until arrival.
    /// </summary>
    [Fact]
    public void VehicleNavIntentSystem_AdvancesCorners_AcrossMultipleTicks()
    {
        var spyNavmesh = new SpyNavmeshProvider(
            new Vector3(3f, 0f, 0f),   // corner 1
            new Vector3(10f, 10f, 0f)  // goal (corner 2)
        );
        var navSystem = new VehicleNavigationIntentSystem(
            navmeshFallback: spyNavmesh,
            arriveToleranceM: 0.5f);

        var repo = new EntityRepository();
        repo.RegisterComponent<NavigationIntent>();
        repo.RegisterComponent<VehicleState>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NavigationStatus>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
        repo.AddComponent(entity, new VehicleState());
        repo.AddComponent(entity, new NavigationStatus { Result = NavigationResult.InProgress });
        repo.AddComponent(entity, new NavigationIntent
        {
            IntentId         = 1,
            Mode             = NavigationMode.DirectPoint,
            FinalDestination = new Vector3(10f, 10f, 0f),
            TargetSpeed      = 5f,
            ArrivalRadius    = 1.5f,
        });

        // Pump several ticks, manually advancing the entity position toward corner 1.
        const float dt = 1f / 20f;

        navSystem.Execute(repo, dt);
        var vsFirst = repo.GetComponent<VehicleState>(entity);
        Assert.True(vsFirst.Speed > 0f,
            "Speed must be non-zero after first Execute.");

        // Move entity to corner 1 (simulate physics driven by motor).
        var tf = repo.GetComponent<SimTransform>(entity);
        tf.Position = new Vector3(3f, 0f, 0f);
        repo.SetComponent(entity, tf);

        navSystem.Execute(repo, dt);

        // Corner index must have advanced past corner 0.
        int corner = navSystem.GetCurrentCorner(entity);
        Assert.True(corner >= 1,
            $"Corner index must advance after entity reaches corner 1; got {corner}.");
    }
}

// ── Spy/Fake crowd provider ──────────────────────────────────────────────────

/// <summary>
/// Spy <see cref="IDtCrowdProvider"/> for STR-D21 F6 tests.
/// When <see cref="ShouldReturnTrue"/> is false, <see cref="RegisterAgent"/> returns false
/// without registering (simulates deferred crowd not yet initialized).
/// When true, it stores the entity and returns true.
/// </summary>
internal sealed class SpyDeferredCrowd : IDtCrowdProvider
{
    public bool ShouldReturnTrue { get; set; }
    public int  RegisterAttempts { get; private set; }
    public int  AgentCount => _agents.Count;
    public Vector3 LastSetAgentTarget { get; private set; }

    private readonly Dictionary<int, bool> _agents = new();

    public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters)
    {
        RegisterAttempts++;
        if (!ShouldReturnTrue)
            return false;
        if (_agents.ContainsKey(entity.Index))
            return false;
        _agents[entity.Index] = true;
        return true;
    }

    /// <inheritdoc/>
    public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters, Vector3 startPositionFdp)
        => RegisterAgent(entity, in parameters);

    public void SetAgentTarget(Entity entity, Vector3 targetFdp)
        => LastSetAgentTarget = targetFdp;

    public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
    {
        snapshot = default;
        return _agents.ContainsKey(entity.Index);
    }

    public Vector3 GetAgentVelocity(Entity entity) => Vector3.Zero;
    public void Update(float dt, Fdp.ModuleHost.Abstractions.ISimulationView view) { }
    public void UnregisterAgent(Entity entity) => _agents.Remove(entity.Index);
}

// ── Spy/Fake navmesh provider ────────────────────────────────────────────────

/// <summary>
/// Spy <see cref="INavmeshProvider"/> that returns a fixed corner list.
/// Lets <see cref="VehicleNavigationIntentSystem"/> plan without a real baked navmesh.
/// </summary>
internal sealed class SpyNavmeshProvider : INavmeshProvider
{
    private readonly Vector3[] _corners;

    /// <summary>Single-corner provider: returns a straight line to the goal.</summary>
    public SpyNavmeshProvider(params Vector3[] corners)
    {
        _corners = corners.Length > 0
            ? corners
            : new[] { new Vector3(5f, 5f, 0f) };  // default single waypoint
    }

    public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
    {
        int count = Math.Min(_corners.Length, waypoints.Length);
        for (int i = 0; i < count; i++)
            waypoints[i] = new NavWaypoint { Position = _corners[i] };
        return count;
    }

    // Minimal stubs for unused interface members.
    public bool  IsWalkable(Vector3 pos, uint layerMask = 0xFFFFFFFF) => true;
    public bool  ProjectToNavmesh(Vector3 pos, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
    { snapped = pos; return true; }
    public int   SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
    public bool  PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;
    public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => 0f;
    public uint  QueryVersion() => 1u;
}
