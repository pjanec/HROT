using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-1 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md RM-1, KV-4) — a VEHICLE HURTS A PERSON BY IMPACT: the ground
    /// twin of <see cref="DroneImpactSystem"/>. Each frame every moving ground vehicle's hull (its footprint, turned by its heading) is swept forward by the
    /// distance it travelled (<see cref="Intersection2D.SweptFrontMeetsCircle"/>, beside the flyer's <see cref="Intersection2D.SegmentInCircle"/>); a person or
    /// an animal whose body the FRONT reaches is struck: a <see cref="HitEffect.Impact"/> <see cref="DamageAssessedEvent"/> (knocked down AND lethal —
    /// ⛔ was <c>Blunt</c>, the <c>Melee.Strike</c> precedent, whose non-lethal floor capped every vehicle hit at the incapacitated line, CE-1967; Source = the vehicle) of a Health FRACTION in bands of the CLOSING speed × the vehicle's mass, with a seeded spread (<see cref="HarmFraction"/>).
    /// Riders (embarked) never; a closing speed under <see cref="HarmlessBelowMps"/> never (a man boarding a stopped vehicle, a nudge); a pair once per
    /// <see cref="RepeatSeconds"/>. For every vehicle, AI or a player's. Serial, after <c>CarKinematicsSystem</c>, on the node that moves the vehicles;
    /// the damage reaches the victim's Health owner through the existing chain. ⛔ It moves nobody (no knock-back) — the damage chain's knock-down does.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class VehicleImpactSystem : IEcsModuleSystem
    {
        /// <summary>No harm below this closing speed (m/s): a man walking into a parked car, a car creeping through a crowd's edge.</summary>
        public const float HarmlessBelowMps = 2f;
        /// <summary>A vehicle meets the same body at most once in this long (s) — it pushes through it, it does not hit it every frame.</summary>
        public const float RepeatSeconds = 2f;
        /// <summary>The reference mass (kg) the bands are stated for — a car; a heavier hull hurts more (√ of the ratio, capped).</summary>
        public const float ReferenceMassKg = 1500f;
        /// <summary>The knock-down a struck person suffers (s), the damage chain's Blunt effect.</summary>
        public const float KnockdownSeconds = 3f;
        /// <summary>A body this far above / below the hull is on another level (a bridge, a roof).</summary>
        public const float LevelBandM = 2.5f;
        /// <summary>A person's body radius when it has no collider (m).</summary>
        public const float PersonRadiusM = 0.3f;
        /// <summary>
        /// ⭐⭐ <c>CE-1967</c> (R-2138) — RUN OVER: a hull this heavy (kg) or heavier that passes OVER a body (its centre inside the hull's footprint) crushes
        /// it — killed (<see cref="CrushFraction"/>), at any speed it moves. 📐 Below it (a car 1.5–2.4 t, a pickup 2.1 t) nothing more than the strike: with
        /// no knock-back a car "passes over" every man it strikes, and that would rewrite every ramming result. Every armoured or engineering hull in the
        /// catalogue is 17.5 t or more.
        /// </summary>
        public const float CrushMassKg = 3500f;
        /// <summary>The Health fraction a crushed body loses — killed (the top strike band's 120 %).</summary>
        public const float CrushFraction = 1.2f;
        /// <summary>A heavy hull moving at least this fast (m/s) crushes what it drives over — a creeping dozer's tracks too.</summary>
        public const float CrushBelowMps = 0.3f;

        private struct Body { public Entity E; public Vector2 P; public float Z; public float R; public Vector2 V; }
        [Fdp.Core.FrameScratch("per-run body array filled from n=0 in the scan, read in the same Execute")]
        private Body[] _bodies = new Body[64];
        [Fdp.Core.SimulationMemory("per-pair strike/crush cooldown (RepeatSeconds); stale entries swept every 256 frames")]
        private readonly Dictionary<long, double> _lastHit = new();
        [Fdp.Core.FrameScratch("list of expired keys: cleared at 177 and filled in the same sweep")]
        private readonly List<long> _stale = new();
        [Fdp.Core.WorldDerived("frame counter used only to schedule the stale sweep every 256 frames")]
        private int _frame;

        /// <summary>Strikes this process raised (rails).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of strikes raised (rails)")]
        public int Strikes { get; private set; }

        /// <summary>
        /// The Health FRACTION a person loses when a hull of <paramref name="massKg"/> strikes him at <paramref name="closingMps"/>, given a seeded
        /// <paramref name="spread01"/> ∈ [0, 1): the effective speed <c>closing × √(mass / 1500)</c> (capped ×0.5…×2) falls in a band — under 2 m/s
        /// nothing; 2–5 knocked down and bruised (10–35 %); 5–9 badly hurt (35–75 %); 9–15 likely killed (75–110 %); above it killed (120 %) — and
        /// the spread moves it ±15 % within reach. Starter values, the builder's (§4 of the design); a person is hurt more the faster and heavier.
        /// </summary>
        public static float HarmFraction(float closingMps, float massKg, float spread01)
        {
            if (closingMps < HarmlessBelowMps) return 0f;
            float m = massKg > 0f ? Math.Clamp(MathF.Sqrt(massKg / ReferenceMassKg), 0.5f, 2f) : 1f;
            float e = closingMps * m;
            float f = e < 2f ? 0f
                    : e < 5f ? Lerp(0.10f, 0.35f, (e - 2f) / 3f)
                    : e < 9f ? Lerp(0.35f, 0.75f, (e - 5f) / 4f)
                    : e < 15f ? Lerp(0.75f, 1.10f, (e - 9f) / 6f)
                    : 1.20f;
            return f * (1f + 0.15f * (2f * Math.Clamp(spread01, 0f, 0.999999f) - 1f));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || deltaTime <= 0f || !repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>()
                || !repo.IsComponentTypeRegistered<Health>() || !repo.Bus.IsRegistered<DamageAssessedEvent>()) return;
            _frame++;
            double now = repo.SimulationTime;
            bool embarked = repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            bool colliders = repo.IsComponentTypeRegistered<PhysicsCollider>();
            bool velocities = repo.IsComponentTypeRegistered<SimVelocity>();

            // the bodies a hull can strike: persons and animals on foot, alive, not aboard anything — gathered once (a reused buffer, 0 B steady)
            int n = 0;
            foreach (var e in repo.Query().With<Health>().With<SimTransform>().Build())
            {
                var cls = Locomotions.Of(repo, e);
                if (cls is not (LocomotionClass.Pedestrian or LocomotionClass.Quadruped)) continue;
                if (embarked && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;
                if (repo.GetComponentRO<Health>(e).Current <= 0f) continue;
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                if (n == _bodies.Length) Array.Resize(ref _bodies, n * 2);
                _bodies[n++] = new Body
                {
                    E = e, P = new Vector2(p.X, p.Y), Z = p.Z,
                    R = colliders && repo.HasComponent<PhysicsCollider>(e) ? MathF.Max(0.1f, repo.GetComponentRO<PhysicsCollider>(e).Radius) : PersonRadiusM,
                    V = velocities && repo.HasComponent<SimVelocity>(e) ? new Vector2(repo.GetComponentRO<SimVelocity>(e).Linear.X, repo.GetComponentRO<SimVelocity>(e).Linear.Y) : Vector2.Zero,
                };
            }
            if (n == 0) return;

            foreach (var v in repo.Query().With<global::CarKinem.Core.VehicleState>().With<SimTransform>().Build())
            {
                float speed = repo.GetComponentRO<global::CarKinem.Core.VehicleState>(v).Speed;
                if (MathF.Abs(speed) < CrushBelowMps) continue;   // ⭐ CE-1967: a slow heavy hull still crushes; the strike keeps its own 2 m/s floor below
                var cls = Locomotions.Of(repo, v);
                if (cls is not (LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Amphibious)) continue;
                if (embarked && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(v)) continue;   // carried: its carrier is the body
                ref readonly var t = ref repo.GetComponentRO<SimTransform>(v);
                var f3 = Vector3.Transform(Vector3.UnitX, t.Rotation);
                var fwd = new Vector2(f3.X, f3.Y);
                if (fwd.LengthSquared() < 1e-6f) continue;
                fwd = Vector2.Normalize(fwd);
                if (speed < 0f) fwd = -fwd;   // reversing: the rear leads
                float sp = MathF.Abs(speed);
                Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.Dimensions(repo, v, out float len, out float wid);
                float halfL = MathF.Max(0.5f, len * 0.5f), halfW = MathF.Max(0.4f, wid * 0.5f), reach = halfL + halfW + sp * deltaTime + 2f;
                var c = new Vector2(t.Position.X, t.Position.Y);
                float mass = -1f;
                for (int i = 0; i < n; i++)
                {
                    ref var b = ref _bodies[i];
                    if (MathF.Abs(b.P.X - c.X) > reach || MathF.Abs(b.P.Y - c.Y) > reach || MathF.Abs(b.Z - t.Position.Z) > LevelBandM) continue;
                    if (!Intersection2D.SweptFrontMeetsCircle(c, fwd, halfL, halfW, sp * deltaTime, b.P, b.R, out _))
                    {
                        // ⭐⭐ CE-1967 (R-2138) — RUN OVER: the front did not reach it this frame, but its centre is UNDER a heavy hull ⇒ crushed, once
                        var d = b.P - c;
                        if (MathF.Abs(Vector2.Dot(d, fwd)) <= halfL && MathF.Abs(d.X * fwd.Y - d.Y * fwd.X) <= halfW)
                        {
                            if (mass < 0f) mass = MassOf(repo, v);
                            long crushKey = (((long)v.Index << 32) | (uint)b.E.Index) | long.MinValue;
                            if (mass >= CrushMassKg && !(_lastHit.TryGetValue(crushKey, out double crushedAt) && now - crushedAt < RepeatSeconds))
                            {
                                _lastHit[crushKey] = now;
                                float max0 = MathF.Max(1f, repo.GetComponentRO<Health>(b.E).Max);
                                repo.Bus.Publish(new DamageAssessedEvent
                                {
                                    HitEntity = b.E, TotalDamage = CrushFraction * max0, Effect = HitEffect.Impact, EffectSeconds = (SimDuration)KnockdownSeconds, Source = v,
                                });
                                Strikes++;
                            }
                        }
                        continue;
                    }
                    if (sp < HarmlessBelowMps) continue;   // the strike's floor (a nudge, a man boarding) — the crush above has none
                    float closing = sp - Vector2.Dot(b.V, fwd);
                    if (closing < HarmlessBelowMps) continue;
                    long key = ((long)v.Index << 32) | (uint)b.E.Index;
                    if (_lastHit.TryGetValue(key, out double last) && now - last < RepeatSeconds) continue;
                    _lastHit[key] = now;
                    if (mass < 0f) mass = MassOf(repo, v);
                    float frac = HarmFraction(closing, mass, Spread(v, b.E, now));
                    if (frac <= 0f) continue;
                    float max = MathF.Max(1f, repo.GetComponentRO<Health>(b.E).Max);
                    repo.Bus.Publish(new DamageAssessedEvent
                    {
                        HitEntity = b.E, TotalDamage = frac * max, Effect = HitEffect.Impact, EffectSeconds = (SimDuration)KnockdownSeconds, Source = v,
                    });
                    Strikes++;
                }
            }

            // forget old pairs now and then (a dictionary walk, not every frame)
            if ((_frame & 255) == 0 && _lastHit.Count > 0)
            {
                _stale.Clear();
                foreach (var (k, at) in _lastHit) if (now - at > RepeatSeconds) _stale.Add(k);
                for (int i = 0; i < _stale.Count; i++) _lastHit.Remove(_stale[i]);
            }
        }

        /// <summary>The seeded spread ∈ [0, 1) of one strike: the same pair at the same sim time gives the same number on every node and thread count.</summary>
        internal static float Spread(Entity vehicle, Entity body, double now)
        {
            uint h = (uint)vehicle.Index * 0x9E3779B1u ^ (uint)body.Index * 0x85EBCA77u ^ (uint)(long)Math.Floor(now * 10d) * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>The vehicle's mass (kg) from its TKB template's <see cref="VehicleParametersDto"/> (as <c>ContactBreach</c> and the mine trigger read it); 0 unknown.</summary>
        public static float MassOf(EntityRepository repo, Entity vehicle)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, vehicle, out var template) && template.GetDescriptor<VehicleParametersDto>() is { } dto ? dto.Mass : 0f;
    }
}
