#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// <b>VehicleNavigationIntentSystem</b> — production navmesh navigation for VEHICLE entities
/// driven by the FDP <see cref="NavigationIntent"/> command front door (BATCH-20, STR-D19).
///
/// <para>
/// <b>Why this system exists.</b>
/// <c>NavigationIntentBridgeSystem</c> auto-registers a DotRecast crowd agent only for entities
/// <em>without</em> a <see cref="VehicleState"/> (infantry).  Vehicles are explicitly excluded
/// from the crowd bridge — they have no production navmesh navigation path; the only existing
/// vehicle motion is direct <see cref="VehicleState"/> commanding (e.g. the F3 waypoint demo).
/// This system fills that gap: it consumes the same production <see cref="NavigationIntent"/>
/// command that the rest of the pipeline uses, plans a path over the live DotRecast navmesh,
/// and steers the vehicle along the resulting corners via <see cref="VehicleWaypointController"/>.
/// </para>
///
/// <para>
/// <b>Query.</b> Entities carrying <see cref="NavigationIntent"/> + <see cref="VehicleState"/> +
/// <see cref="SimTransform"/> — i.e. exactly the vehicles the crowd bridge skips.
/// </para>
///
/// <para>
/// <b>Per-intent (IntentId changed, Mode = DirectPoint).</b>
/// Calls <see cref="INavmeshProvider.PlanPath"/> on the <see cref="NavLayerMask.Vehicle"/> layer
/// from the vehicle's current position to <see cref="NavigationIntent.FinalDestination"/>.
/// Inputs/outputs are converted FDP↔navmesh-query space (the provider operates in Stride/navmesh
/// space, X=East, Y=Up, Z=North; FDP is X=East, Y=North, Z=Up — swizzle via
/// <see cref="FdpStrideTransform"/>).  The resulting corner list (2-D FDP X/Y) and a
/// current-corner index are stored in a small managed dictionary keyed by the full
/// <see cref="Entity"/> handle.  On a 0-corner result (no path) the system writes a failed
/// <see cref="NavigationStatus"/>, halts the vehicle, and logs loudly.
/// </para>
///
/// <para>
/// <b>Each tick.</b> Picks the current corner, runs <see cref="VehicleWaypointController.Compute"/>
/// from the vehicle's actual pose, writes <see cref="VehicleState.Speed"/>/<see cref="VehicleState.SteerAngle"/>,
/// and advances the corner index on arrival.  On reaching the final corner it sets Speed = 0 and
/// writes <see cref="NavigationStatus"/> (Result = Arrived, echoing the IntentId).  A movement-based
/// stuck guard (no displacement over a window) advances past a wedged corner so the demo never freezes.
/// </para>
///
/// <para>
/// <b>Run-order.</b> Registered in the Simulation phase AFTER <c>NavigationExecutionSystem</c>
/// and BEFORE the physics step / motors, so the <see cref="VehicleState"/> it writes is consumed
/// the same frame by <c>KinematicVehicleMotor</c> (which runs pre-physics in
/// <c>EditorStrideSubsystem.Tick</c>).
/// </para>
///
/// <para>
/// <b>Graceful degradation.</b> If no <see cref="INavmeshProvider"/> singleton is registered the
/// system is a complete no-op (it never throws).  This keeps headless compositions without a baked
/// navmesh harmless.
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class VehicleNavigationIntentSystem : IEcsModuleSystem
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("VehicleNavigationIntentSystem");

    // ── Steering controller defaults (match the F4 Navmesh Drive demo) ───────
    private const float DefaultCruiseSpeed   = 3.0f;   // m/s
    private const float DefaultMaxSteerRad   = 0.7f;   // ~40°
    private const float DefaultHeadingGain   = 2.0f;
    private const float DefaultArriveTolM    = 1.5f;   // corner arrival tolerance (m)
    private const float DefaultSlowRadiusM   = 4.0f;
    private const float DefaultWheelBase     = 2.5f;

    // ── Movement-based stuck guard ───────────────────────────────────────────
    private const float StuckDisplacementThresholdM = 0.3f;  // min displacement over the window
    private const float StuckWindowSec               = 3.0f; // window length (s)

    // ── Throttled VehicleState diagnostic (STR-D21 F7 fix) ──────────────────
    // Emit once per ~0.5 s per entity to confirm speed/steer are being written.
    private const float VehicleNavDiagIntervalSec = 0.5f;
    private readonly Dictionary<Entity, float> _vehicleNavDiagAccum = new();

    /// <summary>Max corners stored per planned path (the PlanPath span size).</summary>
    public const int MaxCorners = 256;

    private readonly INavmeshProvider? _navmeshFallback;
    private readonly VehicleWaypointController _controller;
    private readonly bool _useNavmesh;

    /// <summary>Per-entity navigation route state, keyed by the full generation-safe handle.</summary>
    private sealed class RouteState
    {
        /// <summary>The IntentId this route was planned for (detects new orders).</summary>
        public uint PlannedIntentId;

        /// <summary>Planned corner list in FDP 2-D (X=East, Y=North).</summary>
        public Vector2[] Corners = Array.Empty<Vector2>();

        /// <summary>Index of the corner currently being driven toward.</summary>
        public int CurrentCorner;

        /// <summary>True once Arrived (final corner reached) — prevents re-issuing commands.</summary>
        public bool Completed;

        /// <summary>True when PlanPath returned 0 corners (no path); vehicle halted + failure reported.</summary>
        public bool Failed;

        // ── Movement-based stuck tracking ────────────────────────────────────
        public Vector3 StuckWindowStartPos;
        public float   StuckWindowElapsed;
    }

    private readonly Dictionary<Entity, RouteState> _routes = new();

    /// <summary>
    /// Constructs the system.  The navmesh provider is normally read from the world's
    /// <see cref="INavmeshProvider"/> singleton each tick; pass <paramref name="navmeshFallback"/>
    /// only for headless tests that do not register the singleton.
    /// </summary>
    /// <param name="navmeshFallback">
    /// Optional explicit provider used when the world has no <see cref="INavmeshProvider"/>
    /// singleton.  When both are present the singleton takes precedence.
    /// </param>
    /// <param name="cruiseSpeed">Cruise speed (m/s) for the steering controller.</param>
    /// <param name="maxSteerAngleRad">Max steer angle (radians).</param>
    /// <param name="headingGainK">Proportional heading gain.</param>
    /// <param name="arriveToleranceM">Per-corner arrival tolerance (m).</param>
    /// <param name="slowRadiusM">Distance (m) at which speed begins ramping down.</param>
    /// <param name="wheelBase">Wheelbase (m) — determines R_min.</param>
    public VehicleNavigationIntentSystem(
        INavmeshProvider? navmeshFallback = null,
        float cruiseSpeed      = DefaultCruiseSpeed,
        float maxSteerAngleRad = DefaultMaxSteerRad,
        float headingGainK     = DefaultHeadingGain,
        float arriveToleranceM = DefaultArriveTolM,
        float slowRadiusM      = DefaultSlowRadiusM,
        float wheelBase        = DefaultWheelBase)
    {
        _navmeshFallback = navmeshFallback;
        _useNavmesh = string.Equals(
            Environment.GetEnvironmentVariable("STRIDE_VEHICLE_NAVMESH"), "1",
            StringComparison.Ordinal);
        _controller = new VehicleWaypointController(
            cruiseSpeed:      cruiseSpeed,
            maxSteerAngleRad: maxSteerAngleRad,
            headingGainK:     headingGainK,
            arriveToleranceM: arriveToleranceM,
            slowRadiusM:      slowRadiusM,
            slowMinFrac:      0.2f,
            wheelBase:        wheelBase);
    }

    /// <summary>The minimum turning radius of the steering controller (diagnostics).</summary>
    public float MinTurningRadiusM => _controller.MinTurningRadiusM;

    /// <summary>
    /// Returns the planned corner count for <paramref name="entity"/>, or 0 if no route is
    /// active.  Exposed for diagnostics / tests.
    /// </summary>
    public int GetCornerCount(Entity entity)
        => _routes.TryGetValue(entity, out var r) ? r.Corners.Length : 0;

    /// <summary>
    /// Returns the index of the corner currently being driven toward for
    /// <paramref name="entity"/>, or -1 if no route is active.  Exposed for tests.
    /// </summary>
    public int GetCurrentCorner(Entity entity)
        => _routes.TryGetValue(entity, out var r) ? r.CurrentCorner : -1;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(VehicleNavigationIntentSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        // Required component types must be registered, else there is nothing to drive.
        if (!repo.IsComponentTypeRegistered<NavigationIntent>()
            || !repo.IsComponentTypeRegistered<VehicleState>()
            || !repo.IsComponentTypeRegistered<SimTransform>())
            return;

        // Resolve the navmesh provider: world singleton wins, fall back to the injected one.
        INavmeshProvider? navmesh =
            repo.HasSingletonManaged<INavmeshProvider>()
                ? repo.GetSingletonManaged<INavmeshProvider>()
                : _navmeshFallback;

        // Graceful no-op when no navmesh is available.
        if (navmesh == null)
            return;

        var query = repo.Query()
            .With<NavigationIntent>()
            .With<VehicleState>()
            .With<SimTransform>()
            .Build();

        foreach (var entity in query)
        {
            var intent = repo.GetComponent<NavigationIntent>(entity);

            // Only DirectPoint intents are handled. Any other mode (None, RoadGraph, FollowRoute)
            // is left to the existing pipeline; drop any stale route for this entity.
            if (intent.Mode != NavigationMode.DirectPoint)
            {
                _routes.Remove(entity);
                continue;
            }

            var simTf  = repo.GetComponent<SimTransform>(entity);
            var curPos = simTf.Position; // FDP (X=East, Y=North, Z=Up)

            // ── New order? (re)plan ────────────────────────────────────────
            if (!_routes.TryGetValue(entity, out var route)
                || route.PlannedIntentId != intent.IntentId)
            {
                route = PlanRoute(navmesh, entity, curPos, intent);
                _routes[entity] = route;

                if (route.Failed)
                {
                    HaltVehicle(repo, entity);
                    WriteStatus(repo, entity, intent.IntentId, NavigationResult.NoPath,
                        NavigationPhase.Idle);
                    continue;
                }

                WriteStatus(repo, entity, intent.IntentId, NavigationResult.InProgress,
                    NavigationPhase.Following);
            }

            if (route.Failed || route.Completed)
            {
                // Keep the vehicle stopped after completion/failure; do not re-command.
                HaltVehicle(repo, entity);
                continue;
            }

            // ── Steer toward the current corner ─────────────────────────────
            var forward  = Vector3.Transform(Vector3.UnitX, simTf.Rotation);
            float heading = MathF.Atan2(forward.Y, forward.X);

            var corner = route.Corners[route.CurrentCorner];
            var output = _controller.Compute(curPos.X, curPos.Y, heading, corner.X, corner.Y);

            // ── Movement-based stuck guard ──────────────────────────────────
            route.StuckWindowElapsed += deltaTime;
            float displacement = (curPos - route.StuckWindowStartPos).Length();
            if (displacement >= StuckDisplacementThresholdM)
            {
                route.StuckWindowStartPos = curPos;
                route.StuckWindowElapsed  = 0f;
                displacement              = 0f;
            }
            bool isStuck = !output.Arrived
                           && route.Corners.Length > 1          // never fake-advance a single-corner direct route
                           && route.StuckWindowElapsed >= StuckWindowSec
                           && displacement < StuckDisplacementThresholdM;

            if (isStuck)
            {
                Log.Warn("[VehicleNav] entity #{0} STUCK before corner {1}/{2} ({3:F1},{4:F1}) " +
                         "at pos=({5:F2},{6:F2}) — advancing to next corner.",
                    entity.Index, route.CurrentCorner, route.Corners.Length - 1,
                    corner.X, corner.Y, curPos.X, curPos.Y);
                AdvanceCorner(repo, entity, route, intent, curPos, arrivedNaturally: false);
                if (route.Completed) HaltVehicle(repo, entity);
                continue;
            }

            // ── Corner arrival ──────────────────────────────────────────────
            if (output.Arrived)
            {
                AdvanceCorner(repo, entity, route, intent, curPos, arrivedNaturally: true);
                if (route.Completed) HaltVehicle(repo, entity);
                continue;
            }

            // ── Command the vehicle this tick ───────────────────────────────
            ref var vs = ref repo.GetComponentRW<VehicleState>(entity);
            vs.Speed      = output.Speed;
            vs.SteerAngle = output.SteerAngle;

            // Throttled diagnostic (STR-D21 F7 fix confirmation).
            // [VehicleNav] tag: GPU operator should see speed/steer > 0 each ~0.5 s,
            // confirming VehicleState is being written and the motor should drive the body.
            if (!_vehicleNavDiagAccum.TryGetValue(entity, out float diagAccum))
                diagAccum = 0f;
            diagAccum += deltaTime;
            _vehicleNavDiagAccum[entity] = diagAccum;
            if (diagAccum >= VehicleNavDiagIntervalSec)
            {
                _vehicleNavDiagAccum[entity] = 0f;
                Log.Info("[VehicleNav] entity #{0} corner={1}/{2} " +
                         "cmd spd={3:F2} steer={4:F3} pos=({5:F2},{6:F2})",
                    entity.Index,
                    route.CurrentCorner, route.Corners.Length - 1,
                    output.Speed, output.SteerAngle,
                    curPos.X, curPos.Y);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Plans a route from <paramref name="curPos"/> to <c>intent.FinalDestination</c> over the
    /// Vehicle navmesh layer and returns a fresh <see cref="RouteState"/>.
    /// </summary>
    private RouteState PlanRoute(
        INavmeshProvider navmesh, Entity entity, Vector3 curPos, in NavigationIntent intent)
    {
        if (!_useNavmesh)
        {
            // Direct straight-line steer: single virtual corner at the destination (FDP X/Y).
            // Bypasses the navmesh entirely (see BATCH-S2-J).
            var dest = intent.FinalDestination;
            Log.Info("[VehicleNav] entity #{0} DIRECT steer to FDP ({1:F1},{2:F1}) for IntentId={3} " +
                     "(navmesh bypassed; set STRIDE_VEHICLE_NAVMESH=1 to re-enable navmesh).",
                entity.Index, dest.X, dest.Y, intent.IntentId);
            return new RouteState
            {
                PlannedIntentId     = intent.IntentId,
                Corners             = new[] { new Vector2(dest.X, dest.Y) },
                CurrentCorner       = 0,
                StuckWindowStartPos = curPos,
                StuckWindowElapsed  = 0f,
            };
        }

        // PlanPath operates in navmesh-query (= Stride) space. Convert FDP→Stride; the provider's
        // Vector3 contract is (X=East, Y=Up, Z=North) which equals FdpStrideTransform.ToStridePosition.
        var startStride = FdpStrideTransform.ToStridePosition(curPos);
        var goalStride  = FdpStrideTransform.ToStridePosition(intent.FinalDestination);

        var startNav = new Vector3(startStride.X, startStride.Y, startStride.Z);
        var goalNav  = new Vector3(goalStride.X,  goalStride.Y,  goalStride.Z);

        var buf   = new NavWaypoint[MaxCorners];
        int count = navmesh.PlanPath(startNav, goalNav, buf.AsSpan(), (uint)NavLayerMask.Vehicle);

        if (count == 0)
        {
            Log.Warn("[VehicleNav] entity #{0} PlanPath returned 0 corners (NO PATH) " +
                     "from FDP ({1:F1},{2:F1}) to ({3:F1},{4:F1}). Vehicle halted, NavigationStatus=NoPath.",
                entity.Index, curPos.X, curPos.Y,
                intent.FinalDestination.X, intent.FinalDestination.Y);
            return new RouteState
            {
                PlannedIntentId = intent.IntentId,
                Failed          = true,
            };
        }

        // Convert corners back to FDP 2-D (X=East, Y=North). Skip the first corner when it is the
        // start position itself (within tolerance) so the controller does not "arrive" instantly.
        var corners = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            var nav = buf[i].Position; // Stride/navmesh space (East, Up, North)
            var fdp = FdpStrideTransform.ToFdpPosition(new SMath.Vector3(nav.X, nav.Y, nav.Z));
            corners.Add(new Vector2(fdp.X, fdp.Y));
        }

        // Drop a leading corner coincident with the start (FindStraightPath always emits the start).
        if (corners.Count > 1)
        {
            float dx = corners[0].X - curPos.X;
            float dy = corners[0].Y - curPos.Y;
            if (MathF.Sqrt(dx * dx + dy * dy) < DefaultArriveTolM)
                corners.RemoveAt(0);
        }

        Log.Info("[VehicleNav] entity #{0} planned {1} corner(s) for IntentId={2} " +
                 "from FDP ({3:F1},{4:F1}) to ({5:F1},{6:F1}).",
            entity.Index, corners.Count, intent.IntentId, curPos.X, curPos.Y,
            intent.FinalDestination.X, intent.FinalDestination.Y);

        return new RouteState
        {
            PlannedIntentId     = intent.IntentId,
            Corners             = corners.ToArray(),
            CurrentCorner       = 0,
            StuckWindowStartPos = curPos,
            StuckWindowElapsed  = 0f,
        };
    }

    /// <summary>
    /// Advances the route to the next corner, or marks it completed (and writes an Arrived
    /// <see cref="NavigationStatus"/>) when the final corner is reached.
    /// </summary>
    private void AdvanceCorner(
        EntityRepository repo, Entity entity, RouteState route, in NavigationIntent intent,
        Vector3 curPos, bool arrivedNaturally)
    {
        bool wasLast = route.CurrentCorner >= route.Corners.Length - 1;

        if (arrivedNaturally)
            Log.Info("[VehicleNav] entity #{0} reached corner {1}/{2}.",
                entity.Index, route.CurrentCorner, route.Corners.Length - 1);

        if (wasLast)
        {
            route.Completed = true;
            WriteStatus(repo, entity, intent.IntentId, NavigationResult.Arrived,
                NavigationPhase.Completed);
            Log.Info("[VehicleNav] entity #{0} ARRIVED at goal via {1} corner(s). " +
                     "NavigationStatus=Arrived IntentId={2}.",
                entity.Index, route.Corners.Length, intent.IntentId);
            return;
        }

        route.CurrentCorner++;
        route.StuckWindowStartPos = curPos;
        route.StuckWindowElapsed  = 0f;
    }

    /// <summary>Sets <see cref="VehicleState.Speed"/>/SteerAngle to zero.</summary>
    private static void HaltVehicle(EntityRepository repo, Entity entity)
    {
        ref var vs = ref repo.GetComponentRW<VehicleState>(entity);
        vs.Speed      = 0f;
        vs.SteerAngle = 0f;
    }

    /// <summary>
    /// Writes a <see cref="NavigationStatus"/> echoing <paramref name="intentId"/> with the given
    /// result/phase.  Adds the component if absent (only when the type is registered).
    /// </summary>
    private static void WriteStatus(
        EntityRepository repo, Entity entity, uint intentId,
        NavigationResult result, NavigationPhase phase)
    {
        if (!repo.IsComponentTypeRegistered<NavigationStatus>())
            return;

        var status = repo.HasComponent<NavigationStatus>(entity)
            ? repo.GetComponent<NavigationStatus>(entity)
            : default;

        status.IntentId = intentId;
        status.Result   = result;
        status.Phase    = phase;

        if (repo.HasComponent<NavigationStatus>(entity))
            repo.SetComponent(entity, status);
        else
            repo.AddComponent(entity, status);
    }
}
