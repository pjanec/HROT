using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4660</c> (thermal drivers TD-S4, TD-6 / TD-16; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §3b, §3c, §4g) — FIRE as simulation state, on the
    /// node that owns the Health (<see cref="HealthOwnership.AppliesHere"/>), by the two TD-16 rules:
    /// <list type="number">
    /// <item><b>damage IGNITES</b> — a type that burns (TKB <c>ThermalSignatureDto.FireSeconds</c> &gt; 0, else its body's default — a wheeled hull 10 min,
    /// a tracked one 30 min) destroyed ⇒ <see cref="FireState"/> ADDED with its final value (D-31): one spot on the hull, its radius the hull's
    /// half-length + <see cref="ReachM"/>, burning for its <c>FireSeconds</c>. ONCE per death — a crossing (<see cref="DeathCrossings"/>, the rule the
    /// wreck obstacle uses): a wreck a scenario starts with (Health 0, no fire) is never lit. ⏳ An incendiary hit (CE-4635 / CE-4637) — not built.</item>
    /// <item><b>fire DAMAGES</b> — every whole <see cref="StepSeconds"/> of sim time, a BODY (a person, an animal) inside a burning spot loses
    /// <see cref="BurnPerSecond"/> × intensity of its maximum, and a holder still alive burns too; written only where that body's Health is owned, one
    /// sample per step (the <c>BleedOutSystem</c> pattern). 0 ⇒ the ordinary death (<see cref="CombatLife.ApplyHitCapabilities"/>).</item>
    /// </list>
    /// ⭐ Parallel contract: a serial system in the <c>HealthApplication</c> capability (after the wreck obstacle); it writes the dead hull's own
    /// <see cref="FireState"/> and the Health of bodies whose Health this node owns. ⛔ No allocation per steady frame (the maps grow on first sight).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class BurnSystem : IEcsModuleSystem
    {
        /// <summary>Fire damage is taken in whole steps of this many seconds (one Health sample per step).</summary>
        public const double StepSeconds = 1.0;
        /// <summary>The share of a body's maximum health a full-intensity fire takes per second (starter: ~10 s to death).</summary>
        public const float BurnPerSecond = 0.1f;
        /// <summary>How far past the hull's half-length a wreck's fire reaches (m) — a man next to it burns.</summary>
        public const float ReachM = 1.5f;

        private readonly DeathCrossings _deaths = new();
        [Fdp.Core.WorldDerived("TKB type to fire-seconds memo, cleared on world change (57)")]
        private readonly Dictionary<long, float> _fireSecondsOf = new();
        private EntityQuery? _bodies, _holders, _fires;
        [Fdp.Core.WorldDerived("step-gate (floor(now/StepSeconds)) reset on world change; derived from sim time")]
        private long _lastStep = long.MinValue;

        /// <summary>Fires this node lit (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of ignitions")]
        public long Ignited { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Health>() || !repo.IsComponentTypeRegistered<FireState>()
                || !repo.IsComponentTypeRegistered<TkbIdentity>()) return;
            if (_deaths.Begin(repo, out bool replaying) || _bodies == null)
            {
                _bodies = repo.Query().With<Health>().With<TkbIdentity>().With<SimTransform>().Build();
                _holders = repo.Query().With<FireState>().With<SimTransform>().Build();
                _fires = _holders;
                _fireSecondsOf.Clear();
                _lastStep = long.MinValue;
            }
            if (replaying || !repo.HasSingletonUnmanaged<GlobalTime>()) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            var tkb = repo.HasSingletonManaged<ITkbDatabase>() ? repo.GetSingletonManaged<ITkbDatabase>() : null;

            // ① damage ignites — at the death crossing, on the Health owner
            foreach (var e in _bodies)
            {
                bool crossed = _deaths.Crossed(e, repo.GetComponentRO<Health>(e).Current > 0f);
                if (!crossed || repo.HasComponent<FireState>(e) || !HealthOwnership.AppliesHere(repo, e)) continue;
                float seconds = FireSecondsOf(tkb, repo.GetComponentRO<TkbIdentity>(e).TkbType);
                if (seconds <= 0f) continue;
                var fire = new FireState();
                fire.TryAdd(new FireSpot(0f, 0f, 1f, HullHalfLength(repo, e) + ReachM, (SimInstant)now, seconds, 1f), now);
                repo.AddComponent(e, fire);   // added with its final value (D-31)
                Ignited++;
            }

            // ② fire damages — one whole step at a time
            long step = (long)Math.Floor(now / StepSeconds);
            if (step == _lastStep) return;
            bool first = _lastStep == long.MinValue;
            _lastStep = step;
            if (first) return;   // the first step starts the clock
            foreach (var holder in _fires!)
            {
                ref readonly var fire = ref repo.GetComponentRO<FireState>(holder);
                if (!fire.IsBurning(now)) continue;
                ref readonly var xf = ref repo.GetComponentRO<SimTransform>(holder);
                for (int i = 0; i < fire.Count; i++)
                {
                    var spot = fire.Get(i);
                    if (!spot.IsBurning(now)) continue;
                    var centre = xf.Position + Vector3.Transform(new Vector3(spot.X, spot.Y, spot.Z), xf.Rotation);
                    float r2 = spot.Radius * spot.Radius;
                    foreach (var body in _bodies)
                    {
                        if (body != holder && !IsBody(repo, body)) continue;
                        if (body != holder)
                        {
                            var at = repo.GetComponentRO<SimTransform>(body).Position;
                            float dx = at.X - centre.X, dy = at.Y - centre.Y;
                            if (dx * dx + dy * dy > r2) continue;
                        }
                        Burn(repo, body, spot.Intensity);
                    }
                }
            }
        }

        private static void Burn(EntityRepository repo, Entity body, float intensity)
        {
            if (repo.GetComponentRO<Health>(body).Current <= 0f || !HealthOwnership.AppliesHere(repo, body)) return;
            ref var hp = ref repo.GetComponentRW<Health>(body);
            hp.Current = MathF.Max(0f, hp.Current - BurnPerSecond * intensity * hp.Max * (float)StepSeconds);
            if (hp.Current <= 0f) CombatLife.ApplyHitCapabilities(repo, body, in hp);
        }

        /// <summary>A person or an animal (a body a fire burns), not a vehicle or an aircraft.</summary>
        private static bool IsBody(EntityRepository repo, Entity e)
            => Fdp.Toolkit.Motion.Locomotions.Of(repo, e) is LocomotionClass.Pedestrian or LocomotionClass.Quadruped;

        private static float HullHalfLength(EntityRepository repo, Entity e)
        {
            if (repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(e)
                && repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Length is > 0f and var l) return l * 0.5f;
            return Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.Radius(repo, e) is > 0f and var r ? r : 2f;
        }

        /// <summary>How long a destroyed one of this type burns: its TKB's <c>FireSeconds</c>, else its body's default (<see cref="ThermalDefaults"/>); 0 = none.</summary>
        private float FireSecondsOf(ITkbDatabase? tkb, long type)
        {
            if (_fireSecondsOf.TryGetValue(type, out float s)) return s;
            s = 0f;
            if (tkb != null && tkb.TryGetByType(type, out var t)) s = ThermalDefaults.Of(t)?.FireSeconds ?? 0f;   // ⭐ the ONE thermal-data rule
            return _fireSecondsOf[type] = s;
        }
    }
}
