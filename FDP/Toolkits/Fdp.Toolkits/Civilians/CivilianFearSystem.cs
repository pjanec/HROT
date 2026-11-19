using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Creatures;
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
                SoundSourceClass.Flame => HeavyWeaponImpulse,   // ⭐ CE-4635 — a flamethrower's roar terrifies
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
            bool fears = repo.IsComponentTypeRegistered<Fear>();   // ⭐ CE-4628 — the ONE shared fear: a civilian's and an animal's
            bool civilians = fears && repo.IsComponentTypeRegistered<CivilianState>();
            bool animals = fears && repo.IsComponentTypeRegistered<Animals.AnimalState>() && repo.IsComponentTypeRegistered<Animals.AnimalTasks>();   // ⭐ CE-4623 AB-3
            if (!civilians && !animals) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;

            if (repo.Bus.IsRegistered<SoundContactEvent>())
                foreach (ref readonly var heard in repo.ReadEvents<SoundContactEvent>())
                {
                    if (animals && repo.IsAlive(heard.Observer) && repo.HasComponent<Animals.AnimalState>(heard.Observer) && repo.HasComponent<Fear>(heard.Observer))
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
                        ref var fear = ref repo.GetComponentRW<Fear>(heard.Observer);   // ⭐ CE-2236 — where it came from
                        fear.FrightFrom = new System.Numerics.Vector3(heard.X, heard.Y, heard.Z);
                        fear.HasFrightFrom = 1;
                        fear.HeardAt = (SimInstant)now;   // ⭐ CE-2237 — heard, not caught from his crowd
                    }
                }

            if (repo.Bus.IsRegistered<SensorChangedEvent>())
                foreach (var change in repo.ReadEvents<SensorChangedEvent>())
                {
                    if (animals && repo.IsAlive(change.Unit) && repo.HasComponent<Animals.AnimalState>(change.Unit) && repo.HasComponent<Fear>(change.Unit))
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
                foreach (var e in repo.Query().With<CivilianState>().With<Fear>().With<Fdp.Toolkit.Combat.EffectState>().Build())
                {
                    ref readonly var fx = ref repo.GetComponentRO<Fdp.Toolkit.Combat.EffectState>(e);
                    double until = Math.Max(fx.StunnedUntil, fx.IrritatedUntil);
                    if (until <= effectsNow || until <= repo.GetComponentRO<Fear>(e).EffectFrightUntil) continue;
                    repo.GetComponentRW<Fear>(e).EffectFrightUntil = (SimInstant)until;
                    Frighten(repo, e, CivilianFear.EffectImpulse, now);
                }
            }

            if (civilians)
            {
                float fade = CivilianFear.DecayPerSecond * deltaTime;
                foreach (var e in repo.Query().With<CivilianState>().With<Fear>().Build())
                {
                    ref var fear = ref repo.GetComponentRW<Fear>(e);
                    if (fear.LastFrightAt == now) continue;   // ⭐ a fright this frame is not faded in the same frame
                    fear.Value = MathF.Max(0f, fear.Value - fade);
                }
            }

            if (animals) StepAnimals(repo, now, deltaTime);
        }

        // ⭐ CE-4623 AB-3 — the frame's threats for the flight zone: reused, grown never shrunk (0 B in the steady state)
        [Fdp.Core.FrameScratch("threat array refilled from n=0 in the gather each run")]
        private Animals.AnimalFear.Threat[] _threats = new Animals.AnimalFear.Threat[64];
        /// <summary>⭐ CE-4649 part D (AN-21f) — the frame's hunters (positions), gathered with the threats: a hunting dog's handler is the nearest one. Reused, grown never shrunk.</summary>
        [Fdp.Core.FrameScratch("this run's hunters: the count reset to 0 each run, refilled from this frame's world")]
        private (Entity Who, System.Numerics.Vector3 At)[] _hunters = new (Entity, System.Numerics.Vector3)[16];
        [Fdp.Core.FrameScratch("hunter count reset to 0 at line 226 each run")]
        private int _nHunters;
        /// <summary>A hunting dog follows a hunter this near (m) and takes a fleeing animal this near for quarry (m).</summary>
        public const float HandlerReachM = 250f, QuarryReachM = 45f;

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-3 (AR-4) — every animal's flight-zone look (when due, ~5 Hz each, staggered), its fade over the species'
        /// recovery time, and the replicated band (written only when it changes). The threats — every person and vehicle in the fight and
        /// not aboard something (a seated man is his carrier) — are gathered once, and only on a frame when some animal looks.
        /// </summary>
        private void StepAnimals(EntityRepository repo, double now, float dt)
        {
            int nThreats = -1;   // gathered lazily
            bool homes = repo.IsComponentTypeRegistered<Home>();
            foreach (var a in repo.Query().With<Fear>().With<Animals.AnimalState>().With<Animals.AnimalTasks>().Build())
            {
                var t = Animals.AnimalFear.TemperamentOf(repo, a);
                ref var f = ref repo.GetComponentRW<Fear>(a);
                ref var tasks = ref repo.GetComponentRW<Animals.AnimalTasks>(a);
                if (now >= tasks.NextZoneLookAt && t != null && repo.HasComponent<SimTransform>(a) && Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, a))
                {
                    tasks.NextZoneLookAt = (SimInstant)(now + Animals.AnimalFear.ZoneLookSeconds);
                    if (nThreats < 0) nThreats = GatherThreats(repo);
                    var here = (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(a).Position;
                    var seen = new ReadOnlySpan<Animals.AnimalFear.Threat>(_threats, 0, nThreats);
                    Animals.AnimalFear.LookRound(ref f, t, here, seen, now);
                    if (t.Ranges)   // ⭐ CE-4649 part D (AN-21f) — the hunting dog's handler and quarry, for its brain
                    {
                        tasks.Handler = NearestHunter(here);
                        tasks.Quarry = NearestQuarry(repo, a, here);
                    }
                    if (t.Territorial)   // ⭐ AB-5 — the guard dog's ground
                    {
                        bool hasHome = homes && repo.HasComponent<Home>(a);
                        var home = hasHome ? repo.GetComponentRO<Home>(a) : default;
                        tasks.Intruder = Animals.AnimalFear.FindIntruder(here, hasHome, in home, seen);
                    }
                }
                Animals.AnimalFear.Fade(ref f, t, now, dt);
                bool bolting = t != null && Animals.AnimalFear.UpdateBolt(in f, ref tasks, t, now);   // ⭐ AB-5 — the ridden horse
                ref readonly var st = ref repo.GetComponentRO<Animals.AnimalState>(a);
                var flags = bolting ? st.Flags | Animals.AnimalFlags.Bolting : st.Flags & ~Animals.AnimalFlags.Bolting;
                byte band = Animals.AnimalFear.Band(f.Value);   // ⭐ CE-4628 §10b — the replicated band stays (the panic breach's input), as before
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

        private Entity NearestHunter(System.Numerics.Vector3 here)
        {
            Entity best = Entity.Null; float bestD = HandlerReachM * HandlerReachM;
            for (int i = 0; i < _nHunters; i++)
            {
                float dx = _hunters[i].At.X - here.X, dy = _hunters[i].At.Y - here.Y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = _hunters[i].Who; }
            }
            return best;
        }

        /// <summary>The nearest animal in FLIGHT (mode Flee) within <see cref="QuarryReachM"/> that is not a hunting dog — what the dog has flushed and may chase a short way.</summary>
        private static Entity NearestQuarry(EntityRepository repo, Entity dog, System.Numerics.Vector3 here)
        {
            Entity best = Entity.Null; float bestD = QuarryReachM * QuarryReachM;
            foreach (var o in repo.Query().With<Animals.AnimalState>().With<SimTransform>().Build())
            {
                if (o == dog) continue;
                ref readonly var st = ref repo.GetComponentRO<Animals.AnimalState>(o);
                if (st.Mode != Animals.AnimalMode.Flee || (Animals.AnimalSpeciesOf.TryOf(repo, o, out var sp) && sp == Animals.AnimalSpecies.HuntingDog)) continue;
                var p = (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(o).Position;
                float dx = p.X - here.X, dy = p.Y - here.Y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = o; }
            }
            return best;
        }

        private int GatherThreats(EntityRepository repo)
        {
            _nHunters = 0;
            if (!repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>()) return 0;
            bool aboardKnown = repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            bool velocities = repo.IsComponentTypeRegistered<SimVelocity>();
            int n = 0;
            foreach (var e in repo.Query().With<global::CarKinem.Core.VehicleParams>().With<SimTransform>().Build())
            {
                var cls = repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Class;
                if (cls == global::CarKinem.Core.VehicleClass.Quadruped)                         // animals do not frighten animals (the dog: AB-5) — ⭐ CE-4649 part D (AN-21f): but a HUNTING dog frightens game
                {
                    if (!repo.IsComponentTypeRegistered<Animals.AnimalState>() || !repo.HasComponent<Animals.AnimalState>(e)
                        || !Animals.AnimalSpeciesOf.TryOf(repo, e, out var species) || species != Animals.AnimalSpecies.HuntingDog) continue;
                }
                if (aboardKnown && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) continue;   // seated: his carrier counts
                if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, e)) continue;                  // a body on the ground frightens nothing
                if (cls == global::CarKinem.Core.VehicleClass.Pedestrian && Animals.Animals.IsHunter(repo, e))   // ⭐ CE-4649 part D — a hunting dog's handler
                {
                    if (_nHunters == _hunters.Length) Array.Resize(ref _hunters, _nHunters * 2);
                    _hunters[_nHunters++] = (e, (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(e).Position);
                }
                if (n == _threats.Length) Array.Resize(ref _threats, n * 2);
                _threats[n++] = new Animals.AnimalFear.Threat
                {
                    Position = (System.Numerics.Vector3)repo.GetComponentRO<SimTransform>(e).Position,
                    Velocity = velocities && repo.HasComponent<SimVelocity>(e) ? repo.GetComponentRO<SimVelocity>(e).Linear : default,
                    IsVehicle = cls != global::CarKinem.Core.VehicleClass.Pedestrian && cls != global::CarKinem.Core.VehicleClass.Quadruped,   // a hunting dog frightens like a man
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
            if (repo.HasComponent<CivilianState>(unit) && repo.HasComponent<Fear>(unit)) return true;
            return (repo.HasComponent<CivilianState>(unit) || Fdp.Toolkit.Combat.Armament.IsCivilianClass(repo.GetDisType(unit))) && CivilianMind.Ensure(repo, unit);
        }

        private static void Frighten(EntityRepository repo, Entity unit, float impulse, double now)
        {
            if (impulse <= 0f) return;
            ref var fear = ref repo.GetComponentRW<Fear>(unit);
            fear.Value = MathF.Min(1f, fear.Value + impulse);
            fear.LastFrightAt = (SimInstant)now;
        }
    }
}
