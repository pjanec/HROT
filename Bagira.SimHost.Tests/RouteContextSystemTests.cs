using System.Numerics;
using Bagira.Map.Common.Components;
using Bagira.SimHost.Brains;
using Bagira.SimHost.Systems.Routing;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Tests;

/// <summary>
/// Unit tests for <see cref="RouteContextSystem"/> — ROUTES1-T014.
///
/// Validates:
/// <list type="bullet">
///   <item><c>"dangerLevel"</c> JSON key is read and written to
///         <see cref="BrainBlackboard.Memory"/> at <see cref="BlackboardOffsets.ExpectedThreatLevel"/>.</item>
///   <item>Malformed ExtensionJson does not throw.</item>
///   <item>The throttle interval prevents double-processing within a single tick cycle.</item>
/// </list>
///
/// <see cref="RouteContextSystem.TickIntervalSeconds"/> is set to 0 so the throttle
/// is bypassed on every call to <c>Run()</c> (DeltaTime defaults to 0 via direct
/// <see cref="ComponentSystem.Run"/> without a stepping kernel).
/// </summary>
public class RouteContextSystemTests
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly EntityRepository    _repo;
    private readonly RouteContextSystem  _system;

    public RouteContextSystemTests()
    {
        _repo   = CreateWorld();
        _system = new RouteContextSystem { TickIntervalSeconds = 0f };
        _system.Create(_repo);
    }

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NavState>();
        repo.RegisterComponent<BrainBlackboard>();
        repo.RegisterComponent<PersonalRouteRef>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        return repo;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Entity CreateVehicle(int trajectoryId = 1, string? personalRouteJson = null)
    {
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState
        {
            Mode         = KinematicsMode.CustomTrajectory,
            TrajectoryId = trajectoryId,
            ProgressS    = 5f,
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Danger level write
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the vehicle's personal route waypoint has <c>"dangerLevel":42</c> in
    /// <see cref="RouteWaypoint.ExtensionJson"/>, the system must write 42 to
    /// <c>BrainBlackboard.Memory[<see cref="BlackboardOffsets.ExpectedThreatLevel"/>]</c>.
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_DangerLevelInExtensionJson_WritesToBlackboard()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":42}");

        _system.Run();

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(42, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    /// <summary>
    /// <c>"dangerLevel"</c> value 255 must be clamped to 255 (max byte) and written
    /// correctly.
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_DangerLevel255_ClampedAtMaxByte()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":255}");

        _system.Run();

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(255, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    /// <summary>
    /// <c>"dangerLevel"</c> value −10 must be clamped to 0.
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_NegativeDangerLevel_ClampedToZero()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":-10}");

        _system.Run();

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Malformed JSON
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Malformed <see cref="RouteWaypoint.ExtensionJson"/> must not throw; the
    /// blackboard must remain at its default value (0).
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_MalformedJson_DoesNotThrow_BlackboardUnchanged()
    {
        var vehicle = CreateVehicle(personalRouteJson: "{ not valid json !!!");

        var ex = Record.Exception(() => _system.Run());

        Assert.Null(ex);
        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Throttle interval
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When <see cref="RouteContextSystem.TickIntervalSeconds"/> is greater than the
    /// accumulated DeltaTime (0 in direct-Run mode), the system skips its payload.
    /// Setting the interval to 1 second and calling Run() without advancing the clock
    /// must leave the blackboard at 0.
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_ThrottleInterval_SkipsPayloadBeforeIntervalElapsed()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":77}");

        // Override: set a 1-second throttle. DeltaTime=0 <  1.0 → system skips.
        _system.TickIntervalSeconds = 1f;

        _system.Run(); // DeltaTime=0 → _elapsed=0 → 0 < 1.0 → skip

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(0, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Empty world safety
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Running <see cref="RouteContextSystem"/> on an empty world must not throw.
    /// </summary>
    [Fact]
    public void OnUpdate_EmptyWorld_DoesNotThrow()
    {
        var ex = Record.Exception(() => _system.Run());

        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Cached query correctness (CT-3, ROUTES1-BATCH-04)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a vehicle uses the shared-route fallback path (no PersonalRouteRef,
    /// matching via <see cref="RouteTrajectoryCache"/>), the cached
    /// <c>_routeQuery</c> must produce the same danger-level result as the
    /// formerly per-tick-built query.
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_SharedRouteFallback_CachedQueryWritesDangerLevelToBlackboard()
    {
        // ── Arrange ───────────────────────────────────────────────────────────
        const int trajectoryId = 42;

        // Shared route entity: RouteTrajectoryCache + RoutePlan (no PersonalRouteRef).
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

        // Vehicle with matching TrajectoryId — no PersonalRouteRef so it falls
        // through to the shared-route (_routeQuery) lookup.
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState
        {
            Mode         = KinematicsMode.CustomTrajectory,
            TrajectoryId = trajectoryId,
            ProgressS    = 1f,
        });
        _repo.AddComponent(vehicle, new BrainBlackboard());

        // ── Act ───────────────────────────────────────────────────────────────
        _system.Run();

        // ── Assert ────────────────────────────────────────────────────────────
        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(7, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }

    /// <summary>
    /// Calling <see cref="RouteContextSystem.Run"/> multiple consecutive times must
    /// produce stable results — proving the cached <c>_vehicleQuery</c> and
    /// <c>_routeQuery</c> remain functional across repeated ticks (CT-3).
    /// </summary>
    [Fact]
    public unsafe void OnUpdate_MultipleConsecutiveRuns_CachedQueriesRetainCorrectBehavior()
    {
        var vehicle = CreateVehicle(personalRouteJson: @"{""dangerLevel"":55}");

        // Run three times — each should succeed because the cached queries are still valid.
        _system.Run();
        _system.Run();
        _system.Run();

        var bb = _repo.GetComponent<BrainBlackboard>(vehicle);
        Assert.Equal(55, (int)bb.Memory[BlackboardOffsets.ExpectedThreatLevel]);
    }
}
