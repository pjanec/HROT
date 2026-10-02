#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace Hrot.Stride.Core;

/// <summary>
/// <see cref="IDtCrowdProvider"/> backed by DotRecast <c>DtCrowd</c> local avoidance/steering.
/// Drop-in replacement for <c>FakeDtCrowdProvider</c>.
///
/// <para>
/// <b>Coordinate convention.</b>
/// The crowd simulation operates in <em>navmesh-query space</em>: X=East, Y=altitude(up), Z=North
/// — the same as Stride world space and the baked <see cref="DtNavMesh"/>.
/// FDP world positions (X=East, Y=North, Z=Up) are converted to crowd space by swizzling
/// <c>(fdp.X, fdp.Z, fdp.Y)</c> via <see cref="FdpStrideTransform.ToStridePosition"/> and
/// converting back via <see cref="FdpStrideTransform.ToFdpPosition"/>.
/// </para>
///
/// <para>
/// <b>Usage.</b>
/// Construct with a baked <see cref="DtNavMesh"/> from <see cref="StrideNavmeshBaker"/>.
/// Call <see cref="IDtCrowdProvider.Update"/> once per sim tick; then read per-agent
/// velocities via <see cref="IDtCrowdProvider.GetAgentVelocity"/>.
/// </para>
///
/// <para>
/// <b>Agent velocity source.</b>
/// After each <c>DtCrowd.Update</c> the agent's <c>vel</c> field (actual velocity after
/// obstacle avoidance) is stored.  <c>dvel</c> (desired velocity before avoidance) is
/// exposed in <see cref="IDtCrowdProvider.TryGetAgentSnapshot"/>.
/// </para>
/// </summary>
public sealed class DotRecastDtCrowdProvider : IDtCrowdProvider
{
    // ── DtCrowd configuration ────────────────────────────────────────────────

    /// <summary>Maximum number of agents the crowd can manage (static upper bound).</summary>
    public const int MaxAgents = 128;

    /// <summary>Search extents used when projecting a target onto the navmesh (metres).</summary>
    private static readonly RcVec3f TargetSearchExtents = new(2f, 4f, 2f);

    // ── Per-agent bookkeeping ────────────────────────────────────────────────

    private sealed class AgentEntry
    {
        public Entity        Entity;
        public DtCrowdAgent  Agent = null!;
        /// <summary>Last computed velocity in FDP space (X=East, Y=North, Z=Up).</summary>
        public Vector3       VelocityFdp;
        public Vector3       TargetFdp;
        public bool          HasTarget;
    }

    // ── Fields ───────────────────────────────────────────────────────────────

    private DtCrowd?                        _crowd;
    private DtNavMeshQuery?                 _navQuery;
    private readonly DtQueryDefaultFilter   _filter = new();
    private readonly Dictionary<int, AgentEntry> _agents = new(); // key = entity.Index
    private readonly float                  _maxAgentRadius;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a crowd provider over the given baked navmesh.
    /// </summary>
    /// <param name="navMesh">
    /// Baked <see cref="DtNavMesh"/> from <see cref="StrideNavmeshBaker"/>.
    /// Must not be null.
    /// </param>
    /// <param name="maxAgentRadius">
    /// Maximum agent radius used to configure DtCrowd's internal proximity grid.
    /// Defaults to 2 m (enough for vehicle agents).
    /// </param>
    public DotRecastDtCrowdProvider(DtNavMesh navMesh, float maxAgentRadius = 2f)
    {
        if (navMesh == null) throw new ArgumentNullException(nameof(navMesh));

        _maxAgentRadius = maxAgentRadius;
        var config = new DtCrowdConfig(maxAgentRadius);
        _crowd    = new DtCrowd(config, navMesh);
        _navQuery = new DtNavMeshQuery(navMesh);
    }

    /// <summary>
    /// Constructs a deferred crowd provider that acts as a no-op until
    /// <see cref="TryInitializeNavMesh"/> is called with a real baked navmesh.
    ///
    /// <para>
    /// This constructor supports the deferred-bake pattern: the provider is created and
    /// passed to simulation systems at kernel-init time; the navmesh is supplied later
    /// (after scene geometry is available, e.g. in <c>BeginRun</c>) via
    /// <see cref="TryInitializeNavMesh"/>.
    /// </para>
    /// </summary>
    /// <param name="maxAgentRadius">
    /// Maximum agent radius passed to <see cref="DtCrowdConfig"/> when the navmesh is
    /// supplied.  Infantry default: 0.4 m (slightly larger than the 0.3 m agent radius
    /// to give the proximity grid a comfortable margin).
    /// </param>
    public DotRecastDtCrowdProvider(float maxAgentRadius = 0.4f)
    {
        // _crowd and _navQuery remain null until TryInitializeNavMesh is called.
        _maxAgentRadius = maxAgentRadius;
        _crowd    = null;
        _navQuery = null;
    }

    /// <summary>
    /// True once a real <see cref="DtNavMesh"/> has been supplied via the constructor
    /// or <see cref="TryInitializeNavMesh"/>.
    /// </summary>
    public bool IsInitialized => _crowd != null;

    /// <summary>
    /// Supplies the baked <see cref="DtNavMesh"/> to a provider constructed with
    /// the deferred constructor.  No-op (returns false) if already initialized.
    /// </summary>
    /// <param name="navMesh">The baked Infantry (or other) navmesh. Must not be null.</param>
    /// <returns>True on first initialization; false if already initialized.</returns>
    public bool TryInitializeNavMesh(DtNavMesh navMesh)
    {
        if (navMesh == null) throw new ArgumentNullException(nameof(navMesh));
        if (_crowd != null)  return false; // already initialized

        var config = new DtCrowdConfig(_maxAgentRadius);
        _crowd    = new DtCrowd(config, navMesh);
        _navQuery = new DtNavMeshQuery(navMesh);
        return true;
    }

    // ── On-navmesh snap ──────────────────────────────────────────────────────

    /// <summary>
    /// Projects <paramref name="fdpPos"/> onto the nearest navmesh polygon using
    /// <see cref="DtNavMeshQuery.FindNearestPoly"/> with the standard
    /// <see cref="TargetSearchExtents"/> half-extents.
    ///
    /// <para>
    /// Returns <c>true</c> and sets <paramref name="snappedFdp"/> to the snapped FDP position
    /// when a polygon is found. Returns <c>false</c> (and leaves <paramref name="snappedFdp"/>
    /// equal to <paramref name="fdpPos"/>) when the navmesh is not yet initialized or no
    /// polygon lies within the search extents.
    /// </para>
    ///
    /// <para>
    /// Use this before <see cref="RegisterAgent"/> and <see cref="SetAgentTarget"/> to ensure
    /// start/goal positions are on the baked navmesh.  The snap distance can be large when the
    /// original position is far off-mesh; callers should log the input and snapped values.
    /// </para>
    /// </summary>
    /// <param name="fdpPos">Input position in FDP world space (X=East, Y=North, Z=Up).</param>
    /// <param name="snappedFdp">
    /// Output: nearest on-mesh point in FDP world space. Set to <paramref name="fdpPos"/>
    /// when no polygon is found (caller sees the raw unsnapped position).
    /// </param>
    /// <returns>True when a polygon was found and snapping succeeded; false otherwise.</returns>
    public bool TrySnapToNavmesh(Vector3 fdpPos, out Vector3 snappedFdp)
    {
        if (_navQuery == null)
        {
            snappedFdp = fdpPos;
            return false;
        }

        RcVec3f crowdPos = ToRcVec(fdpPos);
        _navQuery.FindNearestPoly(
            crowdPos, TargetSearchExtents, _filter,
            out long polyRef, out RcVec3f nearestPt, out _);

        if (polyRef == 0)
        {
            snappedFdp = fdpPos;
            return false;
        }

        snappedFdp = ToFdpVec(nearestPt);
        return true;
    }

    // ── IDtCrowdProvider ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Maps <see cref="CrowdAgentParams"/> → <see cref="DtCrowdAgentParams"/>.
    /// The agent's initial position is set to (0,0,0) in crowd space; it will be
    /// corrected on the first <see cref="Update"/> call when the crowd reads the
    /// entity's <see cref="SimTransform"/>. Use the overload
    /// <see cref="RegisterAgent(Entity, in CrowdAgentParams, Vector3)"/> to place
    /// the agent at a snapped start position from registration frame 1.
    /// Returns false without throwing when the navmesh has not yet been supplied
    /// (deferred-init mode).
    /// </remarks>
    public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters)
        => RegisterAgentCore(entity, parameters, null);

    /// <summary>
    /// Registers <paramref name="entity"/> as a crowd agent, placing it at the nearest
    /// navmesh polygon to <paramref name="startPositionFdp"/>.
    ///
    /// <para>
    /// Prefer this overload over <see cref="RegisterAgent(Entity, in CrowdAgentParams)"/>
    /// when the entity's <see cref="SimTransform"/> may not yet be on the navmesh surface.
    /// The start position is snapped via <see cref="TrySnapToNavmesh"/> before being passed to
    /// <see cref="DtCrowd.AddAgent"/>, so the agent can receive a valid path immediately.
    /// </para>
    ///
    /// <para>
    /// The caller is responsible for logging the snap result (call <see cref="TrySnapToNavmesh"/>
    /// separately to get the snapped position and on-mesh flag for diagnostic purposes).
    /// </para>
    /// </summary>
    /// <param name="entity">The FDP entity to enroll.</param>
    /// <param name="parameters">Crowd agent parameters (radius, height, speed, etc.).</param>
    /// <param name="startPositionFdp">
    /// Desired initial position in FDP world space (X=East, Y=North, Z=Up).
    /// Will be snapped to the nearest navmesh polygon before passing to DtCrowd.
    /// </param>
    /// <returns>True when the agent was successfully added; false if the navmesh is not
    /// yet initialized or the entity was already registered.</returns>
    public bool RegisterAgent(Entity entity, in CrowdAgentParams parameters,
        Vector3 startPositionFdp)
        => RegisterAgentCore(entity, parameters, startPositionFdp);

    private bool RegisterAgentCore(Entity entity, in CrowdAgentParams parameters,
        Vector3? startPositionFdp)
    {
        // Deferred-init guard: silently decline if the navmesh isn't ready yet.
        if (_crowd == null) return false;

        if (_agents.ContainsKey(entity.Index))
            return false;

        var ap = new DtCrowdAgentParams
        {
            radius                = parameters.Radius,
            height                = parameters.Height,
            maxAcceleration       = parameters.MaxAcceleration,
            maxSpeed              = parameters.MaxSpeed,
            collisionQueryRange   = parameters.Radius * 12f,
            pathOptimizationRange = parameters.Radius * 30f,
            separationWeight      = parameters.SeparationWeight,
            obstacleAvoidanceType = 3, // high quality
            updateFlags           =
                DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS |
                DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS     |
                DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO    |
                DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE |
                DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION,
        };

        // Snap start position to navmesh when provided; fall back to (0,0,0) otherwise.
        // Placing the agent at a valid navmesh polygon on creation avoids the one-frame
        // delay where the crowd cannot find a path because the agent is off-mesh.
        RcVec3f crowdStartPos = RcVec3f.Zero;
        if (startPositionFdp.HasValue)
        {
            // Snap to nearest polygon; if no poly found within extents, use unsnapped pos.
            _ = TrySnapToNavmesh(startPositionFdp.Value, out Vector3 snappedFdp);
            crowdStartPos = ToRcVec(snappedFdp);
        }

        var agent = _crowd.AddAgent(crowdStartPos, ap);

        _agents[entity.Index] = new AgentEntry
        {
            Entity      = entity,
            Agent       = agent,
            VelocityFdp = Vector3.Zero,
            HasTarget   = false,
        };
        return true;
    }

    /// <inheritdoc/>
    public void UnregisterAgent(Entity entity)
    {
        if (_crowd == null) return;
        if (!_agents.TryGetValue(entity.Index, out var entry))
            return;
        _crowd.RemoveAgent(entry.Agent);
        _agents.Remove(entity.Index);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Projects the FDP-space target onto the navmesh via
    /// <see cref="DtNavMeshQuery.FindNearestPoly"/> and calls
    /// <see cref="DtCrowd.RequestMoveTarget"/>.
    /// No-op when in deferred-init mode (navmesh not yet supplied).
    /// </remarks>
    public void SetAgentTarget(Entity entity, Vector3 target)
    {
        if (_crowd == null || _navQuery == null) return;
        if (!_agents.TryGetValue(entity.Index, out var entry))
            return;

        entry.TargetFdp = target;
        entry.HasTarget = true;

        // Convert FDP (X=East, Y=North, Z=Up) → crowd space (X=East, Y=altitude, Z=North).
        RcVec3f crowdTarget = ToRcVec(target);

        _navQuery.FindNearestPoly(
            crowdTarget, TargetSearchExtents, _filter,
            out long targetRef, out RcVec3f nearestPt, out _);

        if (targetRef != 0)
        {
            _crowd.RequestMoveTarget(entry.Agent, targetRef, nearestPt);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For each registered agent, reads the entity's <see cref="SimTransform"/> from
    /// <paramref name="view"/> (FDP space), converts to crowd space, teleports the
    /// agent's <c>npos</c> to keep the crowd in sync with ECS authority, then steps
    /// <see cref="DtCrowd.Update"/> and stores the resulting <c>vel</c> back to FDP.
    /// </remarks>
    public void Update(float dt, ISimulationView view)
    {
        // Deferred-init guard: silently skip until the navmesh is supplied.
        if (_crowd == null) return;
        if (dt <= 0f || _agents.Count == 0) return;

        // Sync crowd agent positions from ECS SimTransform (ECS is authoritative).
        foreach (var kv in _agents)
        {
            var entry = kv.Value;
            if (!view.IsAlive(entry.Entity)) continue;
            if (!view.HasComponent<SimTransform>(entry.Entity)) continue;

            var tf        = view.GetComponentRO<SimTransform>(entry.Entity);
            var crowdPos  = ToRcVec(tf.Position);

            // Teleport the agent's current position to match ECS authority.
            // DtCrowdAgent.npos is the authoritative position inside DtCrowd.
            entry.Agent.npos = crowdPos;
        }

        // Advance the crowd simulation.
        _crowd.Update(dt, null);

        // Harvest output velocities.
        foreach (var kv in _agents)
        {
            var entry = kv.Value;
            // agent.vel = actual velocity after avoidance (DtCrowd post-Update).
            entry.VelocityFdp = ToFdpVec(entry.Agent.vel);
        }
    }

    /// <inheritdoc/>
    public Vector3 GetAgentVelocity(Entity entity)
        => _agents.TryGetValue(entity.Index, out var e) ? e.VelocityFdp : Vector3.Zero;

    /// <inheritdoc/>
    public bool TryGetAgentSnapshot(Entity entity, out CrowdAgentSnapshot snapshot)
    {
        if (!_agents.TryGetValue(entity.Index, out var entry))
        {
            snapshot = default;
            return false;
        }

        var agent = entry.Agent;
        snapshot = new CrowdAgentSnapshot
        {
            // Position is kept in crowd (navmesh) space; convert back to FDP.
            Position        = ToFdpVec(agent.npos),
            Velocity        = ToFdpVec(agent.vel),
            Target          = entry.HasTarget ? entry.TargetFdp : Vector3.Zero,
            DesiredVelocity = ToFdpVec(agent.dvel),
            ReachedTarget   = entry.HasTarget &&
                              Vector3.DistanceSquared(ToFdpVec(agent.npos), entry.TargetFdp) < 0.01f,
            NearbyAgentCount = agent.nneis,
        };
        return true;
    }

    // ── Coordinate helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Converts an FDP world position (X=East, Y=North, Z=Up) to a DotRecast
    /// <see cref="RcVec3f"/> in crowd/navmesh space (X=East, Y=altitude, Z=North).
    /// Swizzle: crowd = (fdp.X, fdp.Z, fdp.Y) — matches
    /// <see cref="FdpStrideTransform.ToStridePosition"/>.
    /// </summary>
    private static RcVec3f ToRcVec(Vector3 fdp) => new(fdp.X, fdp.Z, fdp.Y);

    /// <summary>
    /// Converts a DotRecast <see cref="RcVec3f"/> in crowd/navmesh space back to
    /// an FDP world position (X=East, Y=North, Z=Up).
    /// Inverse swizzle: fdp = (crowd.X, crowd.Z, crowd.Y).
    /// </summary>
    private static Vector3 ToFdpVec(RcVec3f crowd) => new(crowd.X, crowd.Z, crowd.Y);
}
