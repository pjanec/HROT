using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, AW-8 / AW-9; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-4) — the two halves of a pick-up, in the
    /// <c>Interactions</c> capability on EVERY node, each gated on what this node owns: <b>the round's owner</b> (the node that flies it)
    /// checks an ask (<see cref="RoundPickUp.TryGrant"/>), removes the round — so a later ask finds it gone: ONE winner — and publishes the
    /// grant; <b>the man's brain owner</b> puts the grant in his hands (<see cref="RoundPickUp.Receive"/>). The interaction transport carries
    /// each across nodes (kinds 9 and 10); in one world both halves run here.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class RoundPickUpSystem : IEcsModuleSystem
    {
        /// <summary>Asks granted / grants put in a man's hands on this node (rails and diagnostics read them).</summary>
        public long Granted { get; private set; }
        public long Received { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (repo.Bus.IsRegistered<PickUpRoundEvent>() && repo.Bus.IsRegistered<PickUpGrantEvent>())
            {
                var asks = repo.Bus.Read<PickUpRoundEvent>();
                for (int i = 0; i < asks.Length; i++)
                {
                    ref readonly var ask = ref asks[i];
                    if (!RoundPickUp.TryGrant(repo, ask.Man, ask.Round, out var grant)) continue;   // not ours, gone, not lying, out of reach
                    repo.DestroyEntity(ask.Round);   // ⭐ the first valid ask wins; a replicated round's master dispose removes the replicas
                    repo.Bus.Publish(grant);
                    Granted++;
                }
            }
            if (repo.Bus.IsRegistered<PickUpGrantEvent>())
            {
                var grants = repo.Bus.Read<PickUpGrantEvent>();
                for (int i = 0; i < grants.Length; i++)
                {
                    ref readonly var grant = ref grants[i];
                    if (!PossessionSystem.OwnsBrain(repo, grant.Man)) continue;   // his hands are on his brain's node
                    if (RoundPickUp.Receive(repo, in grant)) Received++;
                }
            }
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, §7.10 PU-6) — a LIVE round in a man's hands keeps burning (<see cref="HeldFuze"/>); when its fuze runs out
    /// before he throws it, it is thrown at his own feet with what is left — the ONE firing path, so it bursts where he stands. On the
    /// brain's node (where his hands are), in the <c>Interactions</c> capability.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class HandsFuzeSystem : IEcsModuleSystem
    {
        /// <summary>A cooked-off round flies with this fuze (s): it bursts at once, at his hands.</summary>
        public const float CookOffFuzeSeconds = 0.05f;

        /// <summary>How far in front of him the cooked-off round is "thrown" (m).</summary>
        public const float CookOffDropM = 0.5f;

        private readonly List<Entity> _cooked = new();

        /// <summary>Rounds that cooked off in a hand on this node.</summary>
        public long CookedOff { get; private set; }

        /// <summary>⭐ <c>CE-3236</c> — Irritant hits a venting canister in a hand gave its holder / canisters that burnt out in a hand.</summary>
        public long Irritated { get; private set; }
        public long BurntOut { get; private set; }

        /// <summary>How long one in-hand Irritant hit lasts: the canister's warhead effect, at least a step and a half (as the cloud's).</summary>
        private static float IrritantSeconds(EntityRepository repo, long ammo)
        {
            float effect = CombatTkb.WarheadOf(repo, ammo)?.EffectSeconds ?? 0f;
            return MathF.Max(effect, (float)AreaEffectCloudSystem.StepSeconds + 0.5f);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<HeldFuze>()) return;
            _cooked.Clear();
            foreach (var man in repo.Query().With<HeldFuze>().Build())
            {
                if (!PossessionSystem.OwnsBrain(repo, man)) continue;
                ref var held = ref repo.GetComponentRW<HeldFuze>(man);
                if (PersonHands.Rounds(repo, man, held.Ammo) <= 0) { _cooked.Add(man); continue; }   // thrown or taken some other way
                if (held.BurnSeconds > 0f)
                {
                    // ⭐ CE-3236 (B-14, §7.15 VC-6) — a VENTING canister burns on in his hand: an Irritant hit on him each whole second;
                    //   burnt out, it is empty — out of his hands, no burst
                    float before = held.BurnSeconds;
                    held.BurnSeconds -= deltaTime;
                    if (held.BurnSeconds > 0f && MathF.Floor(held.BurnSeconds) != MathF.Floor(before)     // each whole second it still burns
                        && AreaEffectCloudSystem.Irritate(repo, man, IrritantSeconds(repo, held.Ammo))) Irritated++;
                    if (held.BurnSeconds > 0f) continue;
                    PersonHands.Take(repo, man, held.Ammo);
                    BurntOut++;
                    _cooked.Add(man);
                    continue;
                }
                held.Remaining -= deltaTime;
                if (held.Remaining > 0f) continue;
                CookOff(repo, man, held.Ammo);
                _cooked.Add(man);
            }
            foreach (var man in _cooked) repo.RemoveComponent<HeldFuze>(man);
        }

        private void CookOff(EntityRepository repo, Entity man, long ammo)
        {
            int index = PersonHands.MountIndex(repo, man);
            if (index < 0 || !repo.HasComponent<SimTransform>(man) || !repo.Bus.IsRegistered<WeaponFireIntent>()) return;
            ref readonly var at = ref repo.GetComponentRO<SimTransform>(man);
            var ahead = Vector3.Transform(Vector3.UnitX, at.Rotation) * CookOffDropM;
            repo.Bus.Publish(new WeaponFireIntent
            {
                Shooter = man, Target = Entity.Null, WeaponIndex = index, AtPoint = true, TargetPoint = at.Position + new Vector3(ahead.X, ahead.Y, 0f),
                AmmoGuid = ammo, FuzeSeconds = CookOffFuzeSeconds, RaisedAt = HitModel.Now(repo),
            });
            PersonHands.Take(repo, man, ammo);
            CookedOff++;
        }
    }
}
