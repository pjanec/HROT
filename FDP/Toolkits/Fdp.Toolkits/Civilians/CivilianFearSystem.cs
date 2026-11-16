using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2235</c> (CV-2, CV-3; docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — how frightening one thing a civilian heard or felt
    /// is. A shot, a heavy weapon or a blast raises his fear by its class's impulse, less the farther away it seemed (half at the edge of
    /// hearing); a round that passes him or hits him raises it more. Fear falls back by <see cref="DecayPerSecond"/>.
    /// </summary>
    public static class CivilianFear
    {
        /// <summary>The distance at which a sound frightens half as much as one beside him — the civilians' hearing range (CV-2).</summary>
        public const float FalloffMetres = 100f;
        public const float SmallArmsImpulse = 0.45f;
        public const float HeavyWeaponImpulse = 0.6f;
        public const float ExplosionImpulse = 0.8f;
        public const float NearMissImpulse = 0.6f;
        public const float HitImpulse = 1f;
        /// <summary>⭐ <c>CE-2239</c> part 2 (FL-6) — a new stun or irritation (a stun grenade, tear gas): force used on him, as a hit.</summary>
        public const float EffectImpulse = HitImpulse;
        /// <summary>How fast fear fades when nothing new frightens him (per second).</summary>
        public const float DecayPerSecond = 0.05f;

        /// <summary>The fear one heard sound adds: its class's impulse × (1 − ½·distance / <see cref="FalloffMetres"/>); 0 for a sound that
        /// is not violence (footsteps, engines, rotors).</summary>
        public static float Impulse(SoundSourceClass heard, float distanceMetres)
        {
            float impulse = heard switch
            {
                SoundSourceClass.SmallArms => SmallArmsImpulse,
                SoundSourceClass.HeavyWeapon => HeavyWeaponImpulse,
                SoundSourceClass.Explosion => ExplosionImpulse,
                _ => 0f,
            };
            float far = Math.Clamp(distanceMetres / FalloffMetres, 0f, 1f);
            return impulse * (1f - 0.5f * far);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2235</c> (§5d) — every civilian's fear, once a frame, on the node that runs his brain: the sounds he heard
    /// (<see cref="SoundContactEvent"/>, grouped by listener in ONE pass over the frame's events, so the cost is the events, not
    /// civilians × events), the rounds that passed or hit him (<see cref="SensorChangedEvent"/> NearMiss / Hit), then the fade. Only
    /// a unit that carries <see cref="CivilianState"/> (added by his <c>CivilianHsm</c>) is touched. The HSM reads the result.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CivilianFearSystem : IEcsModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            bool civilians = repo.IsComponentTypeRegistered<CivilianState>();
            bool animals = repo.IsComponentTypeRegistered<Animals.Fear>() && repo.IsComponentTypeRegistered<Animals.AnimalState>();   // ⭐ CE-4623 AB-3
            if (!civilians && !animals) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;

            if (repo.Bus.IsRegistered<SoundContactEvent>())
                foreach (ref readonly var heard in repo.ReadEvents<SoundContactEvent>())
                {
                    if (animals && repo.IsAlive(heard.Observer) && repo.HasComponent<Animals.Fear>(heard.Observer))
                    {   // ⭐ CE-4623 AB-3 — an animal: the same table, its temperament (a fearless one only flinches at a blast)
                        float far = DistanceTo(repo, heard.Observer, heard.X, heard.Y);
                        Animals.AnimalFear.Frighten(repo, heard.Observer,
                            Animals.AnimalFear.Impulse(Animals.AnimalFear.TemperamentOf(repo, heard.Observer), (SoundSourceClass)heard.SourceClass, far),
                            now, new System.Numerics.Vector3(heard.X, heard.Y, heard.Z));
                        continue;
                    }
                    if (!civilians || !IsAfraidKind(repo, heard.Observer)) continue;
                    float distance = 0f;
                    if (repo.HasComponent<SimTransform>(heard.Observer))
                    {
                        var me = repo.GetComponentRO<SimTransform>(heard.Observer).Position;
                        float dx = heard.X - me.X, dy = heard.Y - me.Y;
                        distance = MathF.Sqrt(dx * dx + dy * dy);
                    }
                    float impulse = CivilianFear.Impulse((SoundSourceClass)heard.SourceClass, distance);
                    Frighten(repo, heard.Observer, impulse, now);
                    if (impulse > 0f)
                    {
                        ref var mind = ref repo.GetComponentRW<CivilianState>(heard.Observer);   // ⭐ CE-2236 — where it came from
                        mind.FrightFrom = new System.Numerics.Vector3(heard.X, heard.Y, heard.Z);
                        mind.HasFrightFrom = 1;
                        mind.HeardAt = (SimInstant)now;   // ⭐ CE-2237 — heard, not caught from his crowd
                    }
                }

            if (repo.Bus.IsRegistered<SensorChangedEvent>())
                foreach (var change in repo.ReadEvents<SensorChangedEvent>())
                {
                    if (animals && repo.IsAlive(change.Unit) && repo.HasComponent<Animals.Fear>(change.Unit))
                    {   // ⭐ CE-4623 AB-3 — a round past it or into it (a fearless one too: it was HIT)
                        if (change.What == SensorChange.NearMiss && !(Animals.AnimalFear.TemperamentOf(repo, change.Unit)?.Fearless ?? false))
                            Animals.AnimalFear.Frighten(repo, change.Unit, CivilianFear.NearMissImpulse, now, null);
                        else if (change.What == SensorChange.Hit) Animals.AnimalFear.Frighten(repo, change.Unit, CivilianFear.HitImpulse, now, null);
                        continue;
                    }
                    if (!civilians || !IsAfraidKind(repo, change.Unit)) continue;
                    if (change.What == SensorChange.NearMiss) Frighten(repo, change.Unit, CivilianFear.NearMissImpulse, now);
                    else if (change.What == SensorChange.Hit) Frighten(repo, change.Unit, CivilianFear.HitImpulse, now);
                }

            // ⭐ CE-2239 part 2 (FL-6, §5j) — a stun grenade or tear gas hurts nobody and is not heard as violence, yet it is force used on
            //   him: a NEW stun or irritation frightens him as a hit does (once per effect; one that grows longer frightens again), so gas
            //   and a stun grenade break a riot as the baton round does.
            if (civilians && repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.EffectState>())
            {
                double effectsNow = Fdp.Toolkit.Combat.HitModel.Now(repo);
                foreach (var e in repo.Query().With<CivilianState>().With<Fdp.Toolkit.Combat.EffectState>().Build())
                {
                    ref readonly var fx = ref repo.GetComponentRO<Fdp.Toolkit.Combat.EffectState>(e);
                    double until = Math.Max(fx.StunnedUntil, fx.IrritatedUntil);
                    if (until <= effectsNow || until <= repo.GetComponentRO<CivilianState>(e).EffectFrightUntil) continue;
                    repo.GetComponentRW<CivilianState>(e).EffectFrightUntil = (SimInstant)until;
                    Frighten(repo, e, CivilianFear.EffectImpulse, now);
                }
            }

            if (civilians)
            {
                float fade = CivilianFear.DecayPerSecond * deltaTime;
                foreach (var e in repo.Query().With<CivilianState>().Build())
                {
                    ref var mind = ref repo.GetComponentRW<CivilianState>(e);
                    if (mind.LastFrightAt == now) continue;   // ⭐ a fright this frame is not faded in the same frame
                    mind.Fear = MathF.Max(0f, mind.Fear - fade);
                }
            }

            if (animals) StepAnimals(repo, now, deltaTime);
        }

        // ⭐ CE-4623 AB-3 — the frame's threats for the flight zone: reused, grown never shrunk (0 B in the steady state)
        private Animals.AnimalFear.Threat[] _threats = new Animals.AnimalFear.Threat[64];

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-3 (AR-4) — every animal's flight-zone look (when due, ~5 Hz each, staggered), its fade over the species'
        /// recovery time, and the replicated band (written only when it changes). The threats — every person and vehicle in the fight and
        /// not aboard something (a seated man is his carrier) — are gathered once, and only on a frame when some animal looks.
        /// </summary>
        private void StepAnimals(EntityRepository repo, double now, float dt)
        {
            int nThreats = -1;   // gathered lazily
            bool homes = repo.IsComponentTypeRegistered<Animals.AnimalHome>();
            foreach (var a in repo.Query().With<Animals.Fear>().With<Animals.AnimalState>().Build())
            {
                var t = Animals.AnimalFear.TemperamentOf(repo, a);
                ref var f = ref repo.GetComponentRW<Animals.Fear>(a);
                if (now >= f.NextZoneLookAt && t != null && repo.HasComponent<SimTransform>(a) && Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, a))
                {
                    f.NextZoneLookAt = now + Animals.AnimalFear.ZoneLookSeconds;
                    if (nThreats < 0) nThreats = GatherThreats(repo);
                    var here = (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(a).Position;
                    var seen = new ReadOnlySpan<Animals.AnimalFear.Threat>(_threats, 0, nThreats);
                    Animals.AnimalFear.LookRound(ref f, t, here, seen, now);
                    if (t.Territorial)   // ⭐ AB-5 — the guard dog's ground
                    {
                        bool hasHome = homes && repo.HasComponent<Animals.AnimalHome>(a);
                        var home = hasHome ? repo.GetComponentRO<Animals.AnimalHome>(a) : default;
                        f.Intruder = Animals.AnimalFear.FindIntruder(here, hasHome, in home, seen);
                    }
                }
                Animals.AnimalFear.Fade(ref f, t, now, dt);
                byte band = Animals.AnimalFear.Band(f.Value);
                bool bolting = t != null && Animals.AnimalFear.UpdateBolt(ref f, t, now);   // ⭐ AB-5 — the ridden horse
                ref readonly var st = ref repo.GetComponentRO<Animals.AnimalState>(a);
                var flags = bolting ? st.Flags | Animals.AnimalFlags.Bolting : st.Flags & ~Animals.AnimalFlags.Bolting;
                if (st.FearBand != band || st.Flags != flags)
                {
                    ref var w = ref repo.GetComponentRW<Animals.AnimalState>(a);
                    w.FearBand = band;
                    if (w.Flags != flags)
                    {
                        w.Flags = flags;   // ⭐ replicated: every node's HUD shows the bolt; the local action row says why the controls went
                        if (bolting) Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.Locomotion(repo, a, 0, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Bolting,
                            0f, Animals.AnimalFear.BoltSeconds);
                    }
                }
            }
        }

        private int GatherThreats(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) return 0;
            bool aboardKnown = repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            bool velocities = repo.IsComponentTypeRegistered<SimVelocity>();
            int n = 0;
            foreach (var e in repo.Query().With<global::CarKinem.Core.VehicleParams>().With<SimTransform>().Build())
            {
                var cls = repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Class;
                if (cls == global::CarKinem.Core.VehicleClass.Quadruped) continue;              // animals do not frighten animals (the dog: AB-5)
                if (aboardKnown && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;   // seated: his carrier counts
                if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, e)) continue;                  // a body on the ground frightens nothing
                if (n == _threats.Length) Array.Resize(ref _threats, n * 2);
                _threats[n++] = new Animals.AnimalFear.Threat
                {
                    Position = (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(e).Position,
                    Velocity = velocities && repo.HasComponent<SimVelocity>(e) ? repo.GetComponentRO<SimVelocity>(e).Linear : default,
                    IsVehicle = cls != global::CarKinem.Core.VehicleClass.Pedestrian,
                    Entity = e,
                };
            }
            return n;
        }

        private static float DistanceTo(EntityRepository repo, Entity unit, float x, float y)
        {
            if (!repo.HasComponent<SimTransform>(unit)) return 0f;
            var me = repo.GetComponentRO<SimTransform>(unit).Position;
            float dx = x - me.X, dy = y - me.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// ⭐ <c>CE-2322</c> (AQ97 D1, R-2062) — a man whose fear this system keeps: one who already has a <see cref="CivilianState"/>, or a
        /// person of the <c>Civilian</c> class, whose state it CREATES here. ⛔ Before, only the civilian's HSM created it (its first tick), so a
        /// civilian on any other task — ordered to walk somewhere — heard gunfire and felt nothing.
        /// </summary>
        private static bool IsAfraidKind(EntityRepository repo, Entity unit)
        {
            if (!repo.IsAlive(unit)) return false;
            if (repo.HasComponent<CivilianState>(unit)) return true;
            return Fdp.Toolkit.Combat.Armament.IsCivilianClass(repo.GetDisType(unit)) && CivilianMind.Ensure(repo, unit);
        }

        private static void Frighten(EntityRepository repo, Entity unit, float impulse, double now)
        {
            if (impulse <= 0f) return;
            ref var mind = ref repo.GetComponentRW<CivilianState>(unit);
            mind.Fear = MathF.Min(1f, mind.Fear + impulse);
            mind.LastFrightAt = (SimInstant)now;
        }
    }
}
