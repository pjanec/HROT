using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4635</c> FL-1 (R-3111; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §3 F-1…F-4, F-12, §4) — what a lit FLAME JET sets alight, on the node
    /// that owns the target's Health. Each tick, for every unit whose jet is lit (<see cref="FlameJets.TryGet"/> — the posture, the burst record, the
    /// munition: no component of its own), every man and vehicle inside the <see cref="FlameCone"/> with a clear sight line from the shooter's eye (a
    /// wall or a hull stops the flame, an opening passes it) gains EXPOSURE (seconds × the jet's intensity); at its kind's <see cref="Ignition"/>
    /// threshold a MAN is set alight (<see cref="EffectState.BurningUntil"/>, <see cref="HitEffect.Burning"/>) and a soft VEHICLE takes a
    /// <see cref="FireState"/> spot (the hull's own frame, as <see cref="BurnSystem"/> lights a wreck). <see cref="BurnSystem"/> then burns both.
    /// <para>⭐ Exposure is NOT a component (user, `2026-11-19`): this system's own buffer, cleared on a new world, not recorded — a seek in the middle
    /// of a burst restarts a count of ≤ 2 s; the fires and burning men it causes ARE recorded. A stay broken for over
    /// <see cref="Ignition.ExposureGapSeconds"/> starts again from 0.</para>
    /// <para>⭐ Parallel contract (F-12): a SERIAL system in the <c>HealthApplication</c> capability, before <see cref="BurnSystem"/>; it writes only
    /// the components of targets whose Health this node owns (added with their final value, D-31). ⛔ No allocation per steady frame (the buffer grows
    /// on first sight).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class FlameSystem : IEcsModuleSystem
    {
        /// <summary>How far past a hull's half-length a vehicle's fire reaches (m) — as a wreck's (<see cref="BurnSystem.ReachM"/>).</summary>
        public const float ReachM = BurnSystem.ReachM;

        private struct Exposure { public float Seconds; public double Last; }

        [Fdp.Core.WorldDerived("per-target flame exposure, cleared on a new world; not recorded by design (DESIGN_Flame_Weapons F-12)")]
        private readonly Dictionary<Entity, Exposure> _exposure = new();
        private EntityQuery? _throwers, _targets;
        [Fdp.Core.WorldDerived("the world epoch last seen")]
        private int _epoch = -1;

        /// <summary>Men and vehicles this node set alight (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of ignitions")]
        public long Ignited { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<WeaponPosture>() || !repo.IsComponentTypeRegistered<WeaponAmmo>()
                || !repo.IsComponentTypeRegistered<Health>() || !repo.IsComponentTypeRegistered<FireState>() || !repo.IsComponentTypeRegistered<EffectState>()) return;
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch) || _throwers == null)
            {
                _throwers = repo.Query().With<WeaponPosture>().With<WeaponAmmo>().With<SimTransform>().Build();
                _targets = repo.Query().With<Health>().With<SimTransform>().Build();
                _exposure.Clear();
            }
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view) || deltaTime <= 0f) return;
            double now = HitModel.Now(repo);
            Fdp.Toolkit.Spatial.Eqs.ILosService? sight = null;
            bool sightAsked = false;

            foreach (var thrower in _throwers)
            {
                if (!FlameJets.TryGet(repo, thrower, now, out var jet)) continue;
                var origin = FlameJets.Origin(repo, thrower, in jet);   // ⭐ CE-4634 DR-5 — from the mouth / nozzle when the type places one
                var toAim = (Vector3)jet.AimPoint - origin;
                if (toAim.LengthSquared() < 1e-6f) continue;
                var dir = Vector3.Normalize(toAim);
                foreach (var target in _targets!)
                {
                    if (target == thrower || repo.GetComponentRO<Health>(target).Current <= 0f) continue;
                    if (FlameJets.RidesOn(repo, target, thrower)) continue;   // ⭐ CE-4634 DR-6 — never its own riders
                    var kind = Ignition.Of(repo, target);
                    if (kind == Ignition.Kind.None) continue;
                    var at = repo.GetComponentRO<SimTransform>(target).Position;
                    var point = at + new Vector3(0f, 0f, 1f);   // the body's middle; a hull's flank
                    if (!FlameCone.Contains(origin, dir, jet.RangeM, jet.HalfAngleRad, point)) continue;
                    if (!sightAsked) { sight = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.Sight(repo, null); sightAsked = true; }
                    if (sight != null && !sight.HasLineOfSight(origin, SightEnd(repo, origin, point, kind, target))) continue;
                    if (!HealthOwnership.AppliesHere(repo, target)) continue;
                    if (Expose(target, deltaTime * jet.Intensity, now) >= Ignition.Threshold(kind)) Ignite(repo, target, kind, in jet, now);
                }
            }
        }

        /// <summary>Adds <paramref name="amount"/> to <paramref name="target"/>'s stay in the flame; the stay's exposure so far.</summary>
        private float Expose(Entity target, float amount, double now)
        {
            _exposure.TryGetValue(target, out var e);
            if (now - e.Last > Ignition.ExposureGapSeconds) e.Seconds = 0f;   // a new stay
            e.Seconds += amount;
            e.Last = now;
            _exposure[target] = e;
            return e.Seconds;
        }

        /// <summary>Where the sight line to <paramref name="point"/> ends: at a man's middle; short of a vehicle's own box (its box would hide itself).</summary>
        private static Vector3 SightEnd(EntityRepository repo, Vector3 origin, Vector3 point, Ignition.Kind kind, Entity target)
        {
            if (kind != Ignition.Kind.SoftVehicle) return point;
            var d = point - origin;
            float len = d.Length();
            float back = HullHalfLength(repo, target) + 0.5f;
            return len <= back ? origin : origin + d * ((len - back) / len);
        }

        private void Ignite(EntityRepository repo, Entity target, Ignition.Kind kind, in FlameJets.Jet jet, double now)
        {
            if (kind == Ignition.Kind.Person)
            {
                // ⭐ a man STAYING in the flame burns on: each tick past the threshold extends his time (📌 measured in fl-flamethrower-cone: lit once,
                //   he burned 6 s and lived while the jet still played on him); the ignition counts once
                if (repo.HasComponent<EffectState>(target))
                {
                    ref var fx = ref repo.GetComponentRW<EffectState>(target);
                    bool already = fx.BurningUntil > now;
                    Effects.Apply(ref fx, HitEffect.Burning, Ignition.BurningManSeconds, now);
                    if (already) return;
                }
                else
                {
                    var state = new EffectState();
                    Effects.Apply(ref state, HitEffect.Burning, Ignition.BurningManSeconds, now);
                    repo.AddComponent(target, state);   // added with its final value (D-31)
                }
                Ignited++;
                return;
            }
            float seconds = jet.BurnSeconds > 0f ? jet.BurnSeconds : Ignition.DefaultVehicleBurnSeconds;
            var spot = new FireSpot(0f, 0f, 1f, HullHalfLength(repo, target) + ReachM, (SimInstant)now, seconds, jet.Intensity);
            if (repo.HasComponent<FireState>(target))
            {
                ref var fire = ref repo.GetComponentRW<FireState>(target);
                if (fire.IsBurning(now) || !fire.TryAdd(in spot, now)) return;
            }
            else
            {
                var fire = new FireState();
                fire.TryAdd(in spot, now);
                repo.AddComponent(target, fire);   // added with its final value (D-31)
            }
            Ignited++;
        }

        private static float HullHalfLength(EntityRepository repo, Entity e)
        {
            if (repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(e)
                && repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Length is > 0f and var l) return l * 0.5f;
            return Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.Radius(repo, e) is > 0f and var r ? r : 2f;
        }
    }
}
