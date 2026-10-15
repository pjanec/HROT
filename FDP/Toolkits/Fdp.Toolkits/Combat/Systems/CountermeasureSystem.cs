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
    /// ⭐⭐ <c>CE-2188</c> (<c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3c, Air Ops §7d AD3) — flares and chaff on the
    /// SimHost. A <see cref="DispenseCountermeasureIntent"/> (the brain's, local or over the wire) throws one salvo of
    /// <see cref="CountermeasuresDto.Salvo"/> decoys at the aircraft — at its velocity, sinking — when the dispenser has decoys of that kind
    /// and the interval has passed. Every homing round whose seeker the kind fools (a flare an <see cref="GuidanceKind.InfraRed"/> round,
    /// chaff a <see cref="GuidanceKind.Radar"/> one) and that sees a new decoy inside its cone draws ONCE for the salvo
    /// (<see cref="SimRng"/> — deterministic); at or above its <see cref="GuidedRound.Rejection"/> it takes the nearest such decoy as its
    /// target. Nothing else changes: the round steers at its target and its proximity fuze bursts there (<see cref="GuidedRoundSystem"/>).
    /// Runs before <see cref="GuidedRoundSystem"/>.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class CountermeasureSystem : IEcsModuleSystem
    {
        /// <summary>How fast a decoy sinks below the aircraft's own velocity (m/s).</summary>
        public const float DecoySinkMps = 8f;

        /// <summary>Decoys of one salvo leave this far apart, across the aircraft's flight (m).</summary>
        public const float DecoySpacingM = 3f;

        /// <summary>The seduction draw's salt (one site, one salt — <see cref="SimRng.FromSim"/>).</summary>
        public const int SeductionSalt = 0x2188;

        private EntityQuery? _decoys, _rounds;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Decoy>()) return;
            double now = HitModel.Now(repo);

            // the decoys fly and burn out
            _decoys = repo.Query().With<Decoy>().With<SimTransform>().Build();
            foreach (var e in _decoys)
            {
                ref var d = ref repo.GetComponentRW<Decoy>(e);
                if (now >= d.Until) { repo.DestroyEntity(e); continue; }
                repo.GetComponentRW<SimTransform>(e).Position += d.Velocity * deltaTime;
            }

            if (!repo.Bus.IsRegistered<DispenseCountermeasureIntent>()) return;
            foreach (ref readonly var intent in view.ReadEvents<DispenseCountermeasureIntent>())
                Dispense(repo, intent.Unit, intent.Kind, now);
        }

        /// <summary>One salvo of <paramref name="kind"/> from <paramref name="unit"/>, then the seduction draw. True when it threw.</summary>
        public bool Dispense(EntityRepository repo, Entity unit, DecoyKind kind, double now)
        {
            if (!repo.IsAlive(unit) || !repo.HasComponent<SimTransform>(unit) || !repo.IsComponentTypeRegistered<CountermeasureState>()) return false;
            if (CombatTkb.CountermeasuresOf(repo, unit) is not { } dto) return false;
            if (!repo.HasComponent<CountermeasureState>(unit)) repo.AddComponent(unit, default(CountermeasureState));
            ref var state = ref repo.GetComponentRW<CountermeasureState>(unit);
            if (state.Filled == 0) { state.Flares = dto.Flares; state.Chaff = dto.Chaff; state.Filled = 1; }
            ref int left = ref kind == DecoyKind.Chaff ? ref state.Chaff : ref state.Flares;
            if (left <= 0 || now < state.NextAt) return false;

            int n = Math.Min(Math.Max(1, dto.Salvo), left);
            left -= n;
            state.NextAt = now + Math.Max(0f, dto.IntervalS);
            uint salvo = ++state.Salvoes;

            var at = repo.GetComponentRO<SimTransform>(unit).Position;
            Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, unit, out _, out var velocity);
            var across = Vector3.Cross(velocity.LengthSquared() > 1e-4f ? Vector3.Normalize(velocity) : Vector3.UnitX, Vector3.UnitZ);
            if (across.LengthSquared() < 1e-6f) across = Vector3.UnitY;
            across = Vector3.Normalize(across);
            Span<Entity> thrown = stackalloc Entity[16];
            int count = Math.Min(n, thrown.Length);
            for (int i = 0; i < count; i++)
            {
                var decoy = repo.CreateEntity();
                float side = (i - 0.5f * (count - 1)) * DecoySpacingM;
                repo.AddComponent(decoy, new SimTransform { Position = at + across * side, Rotation = Quaternion.Identity });
                repo.AddComponent(decoy, new Decoy
                {
                    Kind = kind, Owner = unit, Until = now + Math.Max(0.1f, dto.DecoySeconds),
                    Velocity = velocity + new Vector3(0f, 0f, -DecoySinkMps),
                });
                thrown[i] = decoy;
            }
            Seduce(repo, kind, thrown[..count], (int)salvo, now);
            return true;
        }

        /// <summary>
        /// Every homing round the <paramref name="kind"/> fools, not already on a decoy, with one of <paramref name="decoys"/> in its cone:
        /// one draw — at or above its <see cref="GuidedRound.Rejection"/> it re-targets the nearest such decoy.
        /// </summary>
        private void Seduce(EntityRepository repo, DecoyKind kind, ReadOnlySpan<Entity> decoys, int salvo, double now)
        {
            if (!repo.IsComponentTypeRegistered<GuidedRound>()) return;
            var fooled = kind == DecoyKind.Chaff ? GuidanceKind.Radar : GuidanceKind.InfraRed;
            _rounds = repo.Query().With<GuidedRound>().With<SimTransform>().Build();
            foreach (var r in _rounds)
            {
                ref var g = ref repo.GetComponentRW<GuidedRound>(r);
                if (g.Guidance != fooled || g.Phase == GuidedPhase.Lost) continue;
                if (repo.IsAlive(g.Target) && repo.HasComponent<Decoy>(g.Target)) continue;
                var pos = repo.GetComponentRO<SimTransform>(r).Position;
                var vel = repo.HasComponent<SimVelocity>(r) ? repo.GetComponentRO<SimVelocity>(r).Linear : Vector3.Zero;
                Entity best = Entity.Null; float bestD = float.MaxValue;
                foreach (var d in decoys)
                {
                    var p = repo.GetComponentRO<SimTransform>(d).Position;
                    if (!GuidedRoundSystem.InCone(in g, pos, vel, p)) continue;
                    float dist = Vector3.DistanceSquared(p, pos);
                    if (dist < bestD) { bestD = dist; best = d; }
                }
                if (best.IsNull) continue;
                var rng = SimRng.FromSim(r.Index, SeductionSalt + salvo, (float)now);
                if (rng.NextSingle() < g.Rejection) continue;   // the seeker rejected the salvo
                g.Target = best;
                g.LastSeen = repo.GetComponentRO<SimTransform>(best).Position;
            }
        }
    }
}
