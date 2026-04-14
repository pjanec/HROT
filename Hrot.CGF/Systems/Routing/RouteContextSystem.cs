using System;
using System.Text.Json;
using Hrot.Map.Common.Components;
using Hrot.CGF.Brains;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;
using ModuleHost.Core.Abstractions;

namespace Hrot.CGF.Systems.Routing;

/// <summary>
/// Low-frequency system that reads per-waypoint <see cref="RouteWaypoint.ExtensionJson"/>
/// for each vehicle that is actively following a route and writes recognised key values
/// into the vehicle's <see cref="BrainBlackboard"/> (ROUTES1-T014).
///
/// <para>
/// Runs in <see cref="SystemPhase.Simulation"/> but is throttled by
/// <see cref="TickIntervalSeconds"/> (default 0.5 s) so it only executes once per
/// interval rather than every physics tick.
/// </para>
///
/// <para>
/// Recognised JSON keys:
/// <list type="table">
///   <item><term><c>"dangerLevel"</c></term>
///         <description>Byte written to <see cref="BlackboardOffsets.ExpectedThreatLevel"/>.</description>
///   </item>
/// </list>
/// Unrecognised keys are silently ignored. Malformed JSON triggers a warning log and is
/// skipped without throwing.
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class RouteContextSystem : ComponentSystem
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Minimum elapsed time (seconds) between consecutive evaluations.</summary>
    public float TickIntervalSeconds { get; set; } = 0.5f;

    private float _elapsed;

    // ── Query cache (CT-3) ────────────────────────────────────────────────────

    // Queries are built once in OnCreate() and reused every tick to avoid
    // per-frame heap allocations.
    private EntityQuery _vehicleQuery = null!;
    private EntityQuery _routeQuery   = null!;

    // ── ISimulationView cache ─────────────────────────────────────────────────

    private static readonly JsonDocumentOptions JsonDocOpts = new()
    {
        AllowTrailingCommas = true,
    };

    // ── ComponentSystem ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnCreate()
    {
        _vehicleQuery = World.Query()
            .With<NavigationIntent>()
            .With<NavigationStatus>()
            .With<BrainBlackboard>()
            .Build();

        _routeQuery = World.Query()
            .With<RouteTrajectoryCache>()
            .WithManaged<RoutePlan>()
            .Build();
    }

    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        _elapsed += DeltaTime;
        if (_elapsed < TickIntervalSeconds)
            return;
        _elapsed = 0f;

        var view = (ISimulationView)World;

        // ── Query vehicles following a custom trajectory with a blackboard ────
        foreach (var vehicleEntity in _vehicleQuery)
        {
            var intent = view.GetComponentRO<NavigationIntent>(vehicleEntity);
            if (intent.Mode != NavigationMode.FollowRoute || intent.TrajectoryId <= 0)
                continue;

            var status = view.GetComponentRO<NavigationStatus>(vehicleEntity);

            // Resolve the RoutePlan for this vehicle.
            RoutePlan? plan = null;

            // Personal route takes priority.
            if (view.HasComponent<PersonalRouteRef>(vehicleEntity))
            {
                ref readonly var routeRef = ref view.GetComponentRO<PersonalRouteRef>(vehicleEntity);
                if (view.IsAlive(routeRef.RouteEntity)
                 && view.HasManagedComponent<RoutePlan>(routeRef.RouteEntity))
                {
                    plan = view.GetManagedComponentRO<RoutePlan>(routeRef.RouteEntity);
                }
            }

            // Fall back to shared route matching by TrajectoryId.
            if (plan == null)
            {
                foreach (var routeEntity in _routeQuery)
                {
                    ref readonly var cache = ref view.GetComponentRO<RouteTrajectoryCache>(routeEntity);
                    if (cache.TrajectoryId == intent.TrajectoryId)
                    {
                        plan = view.GetManagedComponentRO<RoutePlan>(routeEntity);
                        break;
                    }
                }
            }

            if (plan == null || plan.Waypoints == null || plan.Waypoints.Count == 0)
                continue;

            // Determine which waypoint segment the vehicle is currently on.
            int segmentIndex = ResolveSegmentIndex(plan, status.ProgressS);
            if (segmentIndex < 0 || segmentIndex >= plan.Waypoints.Count)
                continue;

            var extensionJson = plan.Waypoints[segmentIndex].ExtensionJson;
            if (string.IsNullOrEmpty(extensionJson))
                continue;

            // Parse the JSON and apply recognised keys to the blackboard.
            ApplyExtensionJson(vehicleEntity, extensionJson, view);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Estimates the current waypoint segment index using the arc-length progress value.
    /// Uses a linear approximation: distributes total progress uniformly across segments.
    /// </summary>
    private static int ResolveSegmentIndex(RoutePlan plan, float progressS)
    {
        int n = plan.Waypoints.Count;
        if (n <= 1) return 0;

        // Build cumulative segment lengths to find which segment progressS falls in.
        float totalLength = 0f;
        var segLengths = new float[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            var a = plan.Waypoints[i].Position;
            var b = plan.Waypoints[i + 1].Position;
            segLengths[i] = System.Numerics.Vector3.Distance(a, b);
            totalLength  += segLengths[i];
        }

        if (totalLength < float.Epsilon) return 0;

        float remaining = System.Math.Clamp(progressS, 0f, totalLength);
        for (int i = 0; i < segLengths.Length; i++)
        {
            remaining -= segLengths[i];
            if (remaining <= 0f) return i;
        }

        return n - 1;
    }

    /// <summary>
    /// Parses <paramref name="extensionJson"/> and writes recognised key values
    /// into the vehicle's <see cref="BrainBlackboard"/>.
    /// </summary>
    private unsafe void ApplyExtensionJson(Entity vehicleEntity, string extensionJson, ISimulationView view)
    {
        try
        {
            using var doc  = JsonDocument.Parse(extensionJson, JsonDocOpts);
            var       root = doc.RootElement;

            ref var blackboard = ref World.GetComponentRW<BrainBlackboard>(vehicleEntity);

            if (root.TryGetProperty("dangerLevel", out var dangerEl)
             && dangerEl.TryGetInt32(out int dangerValue))
            {
                blackboard.Memory[BlackboardOffsets.ExpectedThreatLevel] = (byte)System.Math.Clamp(dangerValue, 0, 255);
            }
        }
        catch (JsonException ex)
        {
            FdpLog<RouteContextSystem>.Warn(
                "[ROUTE-CTX] Malformed ExtensionJson -- skipping vehicle {0}: {1}",
                vehicleEntity, ex.Message);
        }
    }
}
