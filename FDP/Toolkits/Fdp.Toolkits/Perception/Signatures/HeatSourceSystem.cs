using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐⭐ <c>CE-4658</c> (thermal drivers TD-S2, TD-3 / TD-4; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4e) — THE writer of <see cref="HeatSource"/>, on the node
    /// that moves the entity (the one holding <c>dtHeatSource</c>, the Muscle). Serial; writes only its own entity and only on a CHANGE (one sample per decision).
    /// <para>⭐ The rule is EDGE-triggered, so an authored state holds until something happens (a parked, empty tank stays Off; a convoy authored Running stays
    /// running until it stops):</para>
    /// <list type="bullet">
    /// <item>first sight — an authored <see cref="HeatSource"/> keeps its state; none ⇒ Dead / Running / Idle (crewed) / Off for an engine, Moving / Standing for a body.
    /// Both times are set to −∞ ("as if it always had been"): a convoy authored running reads warmed through, a parked one cold.</item>
    /// <item>it starts moving (above <see cref="MoveOnSpeed"/>) ⇒ an engine Running, a body Moving — <see cref="HeatSource.Since"/> and <see cref="HeatSource.MotionSince"/> now;</item>
    /// <item>it stays below <see cref="MoveOffSpeed"/> for <see cref="EngineStopSeconds"/> (a body <see cref="BodyStopSeconds"/>) ⇒ Idle (crewed) / Off, Standing — stamped at
    /// the moment it stopped;</item>
    /// <item>its crew appears / goes (<see cref="IsCrewed"/>, steady for <see cref="CrewSeconds"/>) while stopped ⇒ Idle / Off;</item>
    /// <item>destroyed ⇒ Dead (an engine; terminal).</item>
    /// </list>
    /// ⭐ When an engine vehicle LEAVES a stand it idled at, it hands the stand to terrain's writer (<see cref="GroundHeatMarkRules.LeaveStand"/>, TD-14): its footprint,
    /// hot end, power class and the seconds its engine ran there (this system's PRIVATE state — never on the wire, §4).
    /// ⚠ Speed is MEASURED here from the position step (every kinematics path counts — a person keeps no velocity on his own node). ⛔ No allocation in the steady state:
    /// the per-entity track grows with new entities only.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class HeatSourceSystem : IEcsModuleSystem
    {
        /// <summary>The <c>dtHeatSource</c> ordinal (the NED <c>EDescriptorType</c>, which the toolkit cannot see; a rail pins the two equal).</summary>
        public const long DescriptorOrdinal = 240;

        /// <summary>Above this (m/s) a stopped entity counts as moving.</summary>
        public const float MoveOnSpeed = 0.5f;
        /// <summary>Below this (m/s) a moving entity counts as stopping.</summary>
        public const float MoveOffSpeed = 0.2f;
        /// <summary>A vehicle stopped this long (s) has stopped — not a pause in traffic.</summary>
        public const double EngineStopSeconds = 3.0;
        /// <summary>A body stopped this long (s) is standing.</summary>
        public const double BodyStopSeconds = 1.0;
        /// <summary>A crew change must hold this long (s) to switch an engine.</summary>
        public const double CrewSeconds = 2.0;

        private static readonly long PackedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
        private static readonly SimInstant Never = (SimInstant)double.NegativeInfinity;

        private struct Track
        {
            public Vector3 Last;
            public bool Moving;
            public double StillSince;        // NaN while it moves above MoveOffSpeed
            public bool Crewed;
            public double CrewChangedAt;     // NaN while the crew matches Crewed
            // the current stand (an engine vehicle)
            public double StandSince;
            public Vector3 StandAt;
            public float StandHeading;
            public float EngineOnSeconds;
        }

        [Fdp.Core.SimulationMemory("per-entity heat track: Moving, StillSince, EngineOnSeconds, CrewChangedAt timers drive HeatSource state")]
        private readonly Dictionary<ulong, Track> _tracks = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<HeatSource>() || !repo.IsComponentTypeRegistered<ThermalState>()) return;
            if (!repo.HasSingletonUnmanaged<GlobalTime>()) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            if (!ReferenceEquals(repo, _queryRepo) || _query == null)
            {
                _query = repo.Query().With<ThermalState>().With<SimTransform>().Build();
                _queryRepo = repo;
            }
            foreach (var e in _query)
            {
                if (!repo.HasAuthority(e, PackedKey)) continue;
                ref readonly var type = ref repo.GetComponentRO<ThermalState>(e);
                ref readonly var xf = ref repo.GetComponentRO<SimTransform>(e);
                Step(repo, e, in type, xf.Position, xf.Rotation, now, deltaTime);
            }
        }

        private void Step(EntityRepository repo, Entity e, in ThermalState type, Vector3 at, Quaternion rotation, double now, float dt)
        {
            bool engine = type.HasPowerPlant;
            bool has = repo.HasComponent<HeatSource>(e);
            bool crewed = engine && IsCrewed(repo, e);
            bool dead = engine && Fdp.Toolkit.Combat.CombatLife.IsDestroyed(repo, e);

            if (!_tracks.TryGetValue(e.PackedValue, out var t))
            {
                // ⭐ first sight: an authored state holds; else decide from what it is doing now. Times −∞ either way.
                var first = has ? repo.GetComponentRO<HeatSource>(e).State : Initial(engine, dead, crewed);
                t = new Track { Last = at, Moving = first is HeatSourceState.Running or HeatSourceState.Moving, StillSince = double.NaN, Crewed = crewed,
                                CrewChangedAt = double.NaN, StandSince = now, StandAt = at, StandHeading = Heading(rotation) };
                _tracks[e.PackedValue] = t;
                Write(repo, e, has, HeatSource.Of(first, Never, Never));
                return;
            }

            float speed = dt > 0f ? Vector3.Distance(at, t.Last) / dt : 0f;
            t.Last = at;
            var current = has ? repo.GetComponentRO<HeatSource>(e) : HeatSource.Of(Initial(engine, dead, crewed), Never, Never);
            var next = current;

            if (engine && dead)
            {
                if (current.State != HeatSourceState.Dead) next = HeatSource.Of(HeatSourceState.Dead, (SimInstant)now, current.MotionSince);
            }
            else if (!t.Moving)
            {
                if (speed > MoveOnSpeed)
                {
                    // ⭐ it leaves its stand: an engine's stand becomes a ground heat mark (terrain decides whether it is worth one)
                    if (engine) LeaveStand(repo, e, in type, in t, now);
                    t.Moving = true; t.StillSince = double.NaN;
                    next = HeatSource.Of(engine ? HeatSourceState.Running : HeatSourceState.Moving, (SimInstant)now, (SimInstant)now);
                }
                else if (engine)
                {
                    if (current.State == HeatSourceState.Idle) t.EngineOnSeconds += dt;
                    // the crew edge, steady for CrewSeconds
                    if (crewed != t.Crewed)
                    {
                        if (double.IsNaN(t.CrewChangedAt)) t.CrewChangedAt = now;
                        else if (now - t.CrewChangedAt >= CrewSeconds)
                        {
                            t.Crewed = crewed; t.CrewChangedAt = double.NaN;
                            var s = crewed ? HeatSourceState.Idle : HeatSourceState.Off;
                            if (current.State != s && current.State != HeatSourceState.Dead) next = HeatSource.Of(s, (SimInstant)now, current.MotionSince);
                        }
                    }
                    else t.CrewChangedAt = double.NaN;
                }
            }
            else
            {
                if (speed >= MoveOffSpeed) t.StillSince = double.NaN;
                else
                {
                    if (double.IsNaN(t.StillSince)) t.StillSince = now;
                    if (now - t.StillSince >= (engine ? EngineStopSeconds : BodyStopSeconds))
                    {
                        var stopped = (SimInstant)t.StillSince;
                        t.Moving = false;
                        t.Crewed = crewed; t.CrewChangedAt = double.NaN;
                        t.StandSince = t.StillSince; t.StandAt = at; t.StandHeading = Heading(rotation); t.EngineOnSeconds = 0f;
                        var s = engine ? (crewed ? HeatSourceState.Idle : HeatSourceState.Off) : HeatSourceState.Standing;
                        next = HeatSource.Of(s, stopped, stopped);
                        if (engine && s == HeatSourceState.Idle) t.EngineOnSeconds = (float)(now - t.StillSince);
                        t.StillSince = double.NaN;
                    }
                }
            }

            _tracks[e.PackedValue] = t;
            if (!has || next.State != current.State || next.Since != current.Since || next.MotionSince != current.MotionSince) Write(repo, e, has, next);
        }

        private static HeatSourceState Initial(bool engine, bool dead, bool crewed)
            => engine ? (dead ? HeatSourceState.Dead : crewed ? HeatSourceState.Idle : HeatSourceState.Off) : HeatSourceState.Standing;

        /// <summary>
        /// ⭐ An engine vehicle is CREWED when someone drives it: it acts by itself (a brain — <see cref="BehaviorState"/>, which every AI unit carries) or a player
        /// possesses it. ⚠ In this simulation an AI vehicle is ONE entity with its own brain, rarely a hull with men aboard ⇒ the brain is the crew. A parked, empty
        /// vehicle is authored Off (an edge-triggered rule keeps it so).
        /// </summary>
        public static bool IsCrewed(EntityRepository repo, Entity e)
            => (repo.IsComponentTypeRegistered<BehaviorState>() && repo.HasComponent<BehaviorState>(e))
               || (repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>() && repo.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(e));

        private static void LeaveStand(EntityRepository repo, Entity e, in ThermalState type, in Track t, double now)
        {
            if (t.EngineOnSeconds <= 0f) return;
            float length = 0f, width = 0f;
            if (repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(e))
            {
                ref readonly var p = ref repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e);
                length = p.Length; width = p.Width;
            }
            if ((length <= 0f || width <= 0f) && repo.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
                && repo.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e))
            {
                float r = repo.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).Radius;
                length = width = 2f * r;
            }
            var mark = new HeatMark(HeatMarkKind.VehicleStand, new Vector2(t.StandAt.X, t.StandAt.Y), t.StandHeading, length, width, (HeatMarkEnd)type.HotEnd,
                                    (SimInstant)t.StandSince, (SimInstant)now, t.EngineOnSeconds, (ThermalPowerClass)type.PowerClass);
            GroundHeatMarkRules.LeaveStand(repo, in mark);
        }

        private static void Write(EntityRepository repo, Entity e, bool has, in HeatSource h)
        {
            if (has) repo.SetComponent(e, h);
            else repo.AddComponent(e, h);   // added with its final value (D-31)
        }

        /// <summary>The yaw (radians, about +Z) of <paramref name="q"/>.</summary>
        private static float Heading(Quaternion q) => MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
    }
}
