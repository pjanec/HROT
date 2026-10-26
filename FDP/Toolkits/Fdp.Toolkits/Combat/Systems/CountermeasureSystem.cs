using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (<c>docs/designs/brain-split/BS-1-DESIGN.md</c> §5.4c; Missiles &amp; Ammo §3c) — flares and chaff
    /// on the SimHost. A release is an ORDINARY fire: a <see cref="WeaponFireIntent"/> (the brain's <see cref="Countermeasures.Release"/>,
    /// local or over the wire) on a <see cref="WeaponCategory.Countermeasure"/> mount, its <c>AmmoGuid</c> an expendable.
    /// <see cref="FireProcessingSystem"/> skips such a mount; this system throws ONE <see cref="Decoy"/> on <see cref="DecoyFlight.At"/>,
    /// lets every homing round of the seeker it fools (a flare an <see cref="GuidanceKind.InfraRed"/> round, chaff a
    /// <see cref="GuidanceKind.Radar"/> one) that sees it inside its cone draw once (<see cref="SimRng"/> — deterministic: at or above its
    /// <see cref="GuidedRound.Rejection"/> it takes the decoy as its target), and publishes the release as a
    /// <see cref="WeaponFireNotification"/> (<c>Quantity</c> 1, the expendable's <c>AmmoGuid</c>) — the event every viewer draws it from.
    /// ⛔ The count is NOT kept here: the brain spent the round when it released. Runs before <see cref="GuidedRoundSystem"/>.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class CountermeasureSystem : IEcsModuleSystem
    {
        /// <summary>The seduction draw's salt (one site, one salt — <see cref="SimRng.FromSim"/>).</summary>
        public const int SeductionSalt = 0x2188;

        private EntityQuery? _decoys, _rounds;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Decoy>()) return;
            double now = HitModel.Now(repo);

            // the decoys fly (THE path — DecoyFlight) and burn out
            _decoys = repo.Query().With<Decoy>().With<SimTransform>().Build();
            foreach (var e in _decoys)
            {
                ref readonly var d = ref repo.GetComponentRO<Decoy>(e);
                if (now >= d.Until) { repo.DestroyEntity(e); continue; }
                repo.GetComponentRW<SimTransform>(e).Position = DecoyFlight.At(d.Origin, d.Velocity, d.SinkMps, (float)(now - d.FiredAt));
            }

            if (!repo.Bus.IsRegistered<WeaponFireIntent>()) return;
            foreach (ref readonly var fire in view.ReadEvents<WeaponFireIntent>())
                if (repo.IsAlive(fire.Shooter) && Countermeasures.IsDispenser(repo, fire.Shooter, fire.WeaponIndex))
                    Throw(repo, fire.Shooter, fire.WeaponIndex, fire.AmmoGuid, now);
        }

        /// <summary>One decoy of the expendable <paramref name="ammo"/> from <paramref name="unit"/>, the seduction draw and the fire
        /// notification. False — nothing happens — when <paramref name="ammo"/> is not a flare or chaff.</summary>
        public bool Throw(EntityRepository repo, Entity unit, int mountIndex, long ammo, double now)
        {
            if (!repo.IsAlive(unit) || !repo.HasComponent<SimTransform>(unit)) return false;
            var tkb = Munitions.DbOf(repo);
            if (Munitions.DecoyOf(tkb, ammo) is not { } kind || Munitions.ExpendableOf(tkb, ammo) is not { } exp) return false;

            var at = repo.GetComponentRO<SimTransform>(unit).Position;
            Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, unit, out _, out var velocity);
            var decoy = repo.CreateEntity();
            repo.AddComponent(decoy, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            repo.AddComponent(decoy, new Decoy
            {
                Kind = kind, Owner = unit, Until = now + Math.Max(0.1f, exp.LifeSeconds),
                Origin = at, Velocity = velocity, FiredAt = now, SinkMps = exp.SinkMps,
            });
            Seduce(repo, kind, decoy, now);

            if (repo.Bus.IsRegistered<WeaponFireNotification>())
                repo.Bus.Publish(new WeaponFireNotification
                {
                    Shooter = unit, Target = Entity.Null, WeaponIndex = mountIndex,
                    AmmoGuid = ammo, Round = Entity.Null, Origin = at, Velocity = velocity, Quantity = 1, FiredAt = now,
                });
            return true;
        }

        /// <summary>
        /// Every homing round the <paramref name="kind"/> fools, not already on a decoy, with <paramref name="decoy"/> in its cone: one
        /// draw — at or above its <see cref="GuidedRound.Rejection"/> it re-targets the decoy. ⭐ One draw per decoy, so a salvo of N
        /// releases is N chances (the salvo is the behaviour's count — §5.4c).
        /// </summary>
        private void Seduce(EntityRepository repo, DecoyKind kind, Entity decoy, double now)
        {
            if (!repo.IsComponentTypeRegistered<GuidedRound>()) return;
            var fooled = kind == DecoyKind.Chaff ? GuidanceKind.Radar : GuidanceKind.InfraRed;
            var p = repo.GetComponentRO<SimTransform>(decoy).Position;
            _rounds = repo.Query().With<GuidedRound>().With<SimTransform>().Build();
            foreach (var r in _rounds)
            {
                ref var g = ref repo.GetComponentRW<GuidedRound>(r);
                if (g.Guidance != fooled || g.Phase == GuidedPhase.Lost) continue;
                if (repo.IsAlive(g.Target) && repo.HasComponent<Decoy>(g.Target)) continue;
                var pos = repo.GetComponentRO<SimTransform>(r).Position;
                var vel = repo.HasComponent<SimVelocity>(r) ? repo.GetComponentRO<SimVelocity>(r).Linear : Vector3.Zero;
                if (!GuidedRoundSystem.InCone(in g, pos, vel, p)) continue;
                var rng = SimRng.FromSim(r.Index, SeductionSalt + decoy.Index, (float)now);
                if (rng.NextSingle() < g.Rejection) continue;   // the seeker rejected this decoy
                g.Target = decoy;
                g.LastSeen = p;
            }
        }
    }
}
