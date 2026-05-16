using System.Numerics;
using Hrot.Map.Common.Components;
using Hrot.CGF.Systems.Routing;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Unit tests for <see cref="RouteContextSystem"/> -- ROUTES1-T014 / PACK-N004.
///
/// The system is a pure Brain-tier system: it queries <see cref="NavigationIntent"/>,
/// <see cref="NavigationStatus"/>, and <see cref="BrainBlackboard"/>. It does NOT
/// query <see cref="CarKinem.Core.NavState"/> (Muscle-tier) after the PACK-N004 refactor.
///
/// <see cref="RouteContextSystem.TickIntervalSeconds"/> is set to 0 so the throttle
/// is bypassed on every call to <c>Execute()</c>.
/// </summary>
public class RouteContextSystemTests
{
    // -- Infrastructure -------------------------------------------------------

    private readonly EntityRepository    _repo;
    private readonly RouteContextSystem  _system;

    public RouteContextSystemTests()
    {
        _repo   = CreateWorld();
        _system = new RouteContextSystem { TickIntervalSeconds = 0f };
    }

    // -- World factory --------------------------------------------------------

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NavigationIntent>();
        repo.RegisterComponent<NavigationStatus>();
        repo.RegisterComponent<BrainBlackboard>();
        repo.RegisterComponent<PersonalRouteRef>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        return repo;
    }

    // -- Helpers --------------------------------------------------------------

    /// <summary>
    /// Creates a vehicle entity with <see cref="NavigationIntent"/> (FollowRoute),
    /// <see cref="NavigationStatus"/> with the supplied <paramref name="progressS"/>,
    /// and a <see cref="BrainBlackboard"/>.
    /// Optionally attaches a personal route with the given ExtensionJson.
    /// </summary>
    private Entity CreateVehicle(
        int    trajectoryId    = 1,
        string? personalRouteJson = null,
        float   progressS      = 5f)
    {
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavigationIntent
        {
            Mode         = NavigationMode.FollowRoute,
            TrajectoryId = trajectoryId,
            IntentId     = 1u,
        });
        _repo.AddComponent(vehicle, new NavigationStatus
        {
            ProgressS = progressS,
            IntentId  = 1u,
        });
        _repo.AddComponent(vehicle, new BrainBlackboard());

        if (personalRouteJson != null)
        {
            var routeEntity = _repo.CreateEntity();
            var plan = new RoutePlan();
            plan.Mutate(wps =>
            {
                wps.Add(new RouteWaypoint
                {
                    Position      = new Vector3(0f, 0f, 0f),
                    ExtensionJson = personalRouteJson,
                });
                wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 0f) });
            });
            _repo.SetManagedComponent(routeEntity, plan);
            _repo.AddComponent(vehicle, new PersonalRouteRef { RouteEntity = routeEntity });
        }

        return vehicle;
    }

    // =========================================================================
    // Danger level write
    // =========================================================================

    /// <summary>
    /// When the vehicle's personal route waypoint has <c>"dangerLevel":42</c> in
    /// <see cref="RouteWaypoint.ExtensionJson"/>, the system must write 42 to
    /// <c>BrainBlackboard.ExpectedThreatLevel</c>.
    /// </summary>
    [Fact]
    public void OnUpdate_DangerLevelInExtensionJson_WritesToBlackboard()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":42}");

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(42, (int)bb.ExpectedThreatLevel);
    }

    /// <summary>
    /// <c>"dangerLevel"</c> value 255 must be clamped to 255 (max byte) and written correctly.
    /// </summary>
    [Fact]
    public void OnUpdate_DangerLevel255_ClampedAtMaxByte()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":255}");

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(255, (int)bb.ExpectedThreatLevel);
    }

    /// <summary>
    /// <c>"dangerLevel"</c> value less than 0 must be clamped to 0.
    /// </summary>
    [Fact]
    public void OnUpdate_NegativeDangerLevel_ClampedToZero()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":-10}");

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.ExpectedThreatLevel);
    }

    // =========================================================================
    // Malformed JSON
    // =========================================================================

    /// <summary>
    /// Malformed <see cref="RouteWaypoint.ExtensionJson"/> must not throw; the
    /// blackboard must remain at its default value (0).
    /// </summary>
    [Fact]
    public void OnUpdate_MalformedJson_DoesNotThrow_BlackboardUnchanged()
    {
        var vehicle = CreateVehicle(personalRouteJson: "{ not valid json !!!");

        var ex = Record.Exception(() => _system.Execute(_repo, 0.016f));

        Assert.Null(ex);
        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.ExpectedThreatLevel);
    }

    // =========================================================================
    // Throttle interval
    // =========================================================================

    /// <summary>
    /// When <see cref="RouteContextSystem.TickIntervalSeconds"/> is greater than the
    /// accumulated DeltaTime, the system skips its payload.
    /// </summary>
    [Fact]
    public void OnUpdate_ThrottleInterval_SkipsPayloadBeforeIntervalElapsed()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":77}");

        _system.TickIntervalSeconds = 1f;

        _system.Execute(_repo, 0f); // deltaTime=0 => _elapsed=0 => 0 < 1.0 => skip

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.ExpectedThreatLevel);
    }

    // =========================================================================
    // Empty world safety
    // =========================================================================

    [Fact]
    public void OnUpdate_EmptyWorld_DoesNotThrow()
    {
        var ex = Record.Exception(() => _system.Execute(_repo, 0.016f));
        Assert.Null(ex);
    }

    // =========================================================================
    // Cached query correctness (CT-3, ROUTES1-BATCH-04)
    // =========================================================================

    /// <summary>
    /// When a vehicle uses the shared-route fallback path (no PersonalRouteRef,
    /// matching via <see cref="RouteTrajectoryCache"/>), the system must still write
    /// the danger level to the blackboard.
    /// </summary>
    [Fact]
    public void OnUpdate_SharedRouteFallback_CachedQueryWritesDangerLevelToBlackboard()
    {
        const int trajectoryId = 42;

        var routeEntity = _repo.CreateEntity();
        _repo.AddComponent(routeEntity, new RouteTrajectoryCache
        {
            TrajectoryId    = trajectoryId,
            CompiledVersion = 1,
        });
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint
            {
                Position      = new Vector3(0f,   0f, 0f),
                ExtensionJson = @"{""dangerLevel"":7}",
            });
            wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 0f) });
        });
        _repo.SetManagedComponent(routeEntity, plan);

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavigationIntent
        {
            Mode         = NavigationMode.FollowRoute,
            TrajectoryId = trajectoryId,
            IntentId     = 1u,
        });
        _repo.AddComponent(vehicle, new NavigationStatus { ProgressS = 1f, IntentId = 1u });
        _repo.AddComponent(vehicle, new BrainBlackboard());

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(7, (int)bb.ExpectedThreatLevel);
    }

    [Fact]
    public void OnUpdate_MultipleConsecutiveRuns_CachedQueriesRetainCorrectBehavior()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":55}");

        _system.Execute(_repo, 0.016f);
        _system.Execute(_repo, 0.016f);
        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(55, (int)bb.ExpectedThreatLevel);
    }

    // =========================================================================
    // PACK-N004: Brain-tier only -- NavigationIntent + NavigationStatus
    // =========================================================================

    /// <summary>
    /// PACK-N004 SC-1: Positive path -- entity with NavigationIntent (FollowRoute),
    /// NavigationStatus.ProgressS, and a RoutePlan with ExtensionJson encoding a
    /// known danger level must have the blackboard written correctly.
    /// </summary>
    [Fact]
    public void PackN004_PositivePath_NavigationIntentAndStatus_WriteBlackboard()
    {
        const int trajectoryId = 99;
        const int expectedDanger = 15;

        // Shared route entity with threat-level JSON.
        var routeEntity = _repo.CreateEntity();
        _repo.AddComponent(routeEntity, new RouteTrajectoryCache
        {
            TrajectoryId    = trajectoryId,
            CompiledVersion = 1,
        });
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint
            {
                Position      = new Vector3(0f, 0f, 0f),
                ExtensionJson = $@"{{""dangerLevel"":{expectedDanger}}}",
            });
            wps.Add(new RouteWaypoint { Position = new Vector3(200f, 0f, 0f) });
        });
        _repo.SetManagedComponent(routeEntity, plan);

        // Vehicle with NavigationIntent (FollowRoute) + NavigationStatus.ProgressS=1f.
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavigationIntent
        {
            Mode         = NavigationMode.FollowRoute,
            TrajectoryId = trajectoryId,
            IntentId     = 1u,
        });
        _repo.AddComponent(vehicle, new NavigationStatus { ProgressS = 1f, IntentId = 1u });
        _repo.AddComponent(vehicle, new BrainBlackboard());

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(expectedDanger, (int)bb.ExpectedThreatLevel);
    }

    /// <summary>
    /// PACK-N004 SC-2: No NavState component is required -- the system must tick and
    /// write the blackboard with only NavigationIntent + NavigationStatus present.
    /// </summary>
    [Fact]
    public void PackN004_NoNavStateRequired_SystemTicksCorrectly()
    {
        // Note: NavState is NOT registered in this world (uses the shared CreateWorld above).
        // Creating a vehicle without NavState proves the system does not require it.
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":33}");

        // Assert the entity has no NavState component AND the system still works.
        Assert.False(_repo.HasComponent<CarKinem.Core.NavState>(vehicle),
            "NavState must not be present -- world does not register it (Brain-tier test).");

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(33, (int)bb.ExpectedThreatLevel);
    }

    /// <summary>
    /// PACK-N004 SC-3: When <see cref="NavigationIntent.Mode"/> is not
    /// <see cref="NavigationMode.FollowRoute"/>, the blackboard must NOT be mutated.
    /// </summary>
    [Fact]
    public void PackN004_InactiveRoute_BlackboardNotMutated()
    {
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavigationIntent
        {
            Mode         = NavigationMode.None,   // inactive -- system should skip
            TrajectoryId = 1,
            IntentId     = 1u,
        });
        _repo.AddComponent(vehicle, new NavigationStatus { ProgressS = 5f, IntentId = 1u });
        _repo.AddComponent(vehicle, new BrainBlackboard());

        // Attach a route with danger level so we'd detect mutation if it happened.
        var routeEntity = _repo.CreateEntity();
        _repo.AddComponent(routeEntity, new RouteTrajectoryCache
        {
            TrajectoryId    = 1,
            CompiledVersion = 1,
        });
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = Vector3.Zero, ExtensionJson = @"{""dangerLevel"":99}" });
            wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 0f) });
        });
        _repo.SetManagedComponent(routeEntity, plan);

        _system.Execute(_repo, 0.016f);

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.ExpectedThreatLevel);
    }
}
