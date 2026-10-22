using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011 I3, docs/DESIGN_Injury_States.md §4) — an INCAPACITATED person bleeds out: the node that owns
    /// his <see cref="Health"/> drains it at the type's <see cref="InjuryThresholds.BleedPerSecond"/> until 0, which is ordinary death
    /// (<see cref="CombatLife.ApplyHitCapabilities"/>, the one rule). Every other node sees the drop through the replicated health.
    /// </summary>
    /// <remarks>
    /// ⭐ Applied in whole <see cref="StepSeconds"/> steps, not per frame: health is published on change
    /// (<c>EntityDamageEgressTranslator</c>), so a per-frame drain would put one sample per frame per casualty on the wire.
    /// ⭐ Runs in the cross-role <c>HealthApplication</c> capability beside <see cref="HealthApplicationSystem"/>, under the same authority
    /// gate — only the Health owner writes.
    /// </remarks>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class BleedOutSystem : IEcsModuleSystem
    {
        /// <summary>How much bleeding time is gathered before it is taken off the health (s).</summary>
        public const float StepSeconds = 1f;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<InjuryThresholds>()
                || !repo.IsComponentTypeRegistered<Health>() || !repo.IsComponentTypeRegistered<BleedOut>()) return;

            // ⭐ R-3039 S1 — the bleed gathered between steps lives on the man (BleedOut), not in this system: a checkpoint restored into a
            //   fresh system carries it on. It stays while he bleeds (no add / remove per step) and goes when he stops.
            foreach (var e in repo.Query().With<InjuryThresholds>().With<Health>().Build())
            {
                bool bleeding = !(repo.HasComponent<NetworkAuthority>(e) && !repo.HasAuthority<Health>(e));
                ref readonly var health = ref repo.GetComponentRO<Health>(e);
                float rate = repo.GetComponentRO<InjuryThresholds>(e).BleedPerSecond;
                bleeding &= rate > 0f && Injury.StateOf(repo, e, in health) == InjuryState.Incapacitated;
                if (!bleeding)
                {
                    if (repo.HasComponent<BleedOut>(e)) repo.RemoveComponent<BleedOut>(e);
                    continue;
                }

                if (!repo.HasComponent<BleedOut>(e)) repo.AddComponent(e, new BleedOut());
                ref var pending = ref repo.GetComponentRW<BleedOut>(e);
                pending.Pending += deltaTime;
                if (pending.Pending < StepSeconds) continue;
                float bled = pending.Pending;
                pending.Pending = 0f;

                ref var hp = ref repo.GetComponentRW<Health>(e);
                hp.Current = MathF.Max(0f, hp.Current - rate * hp.Max * bled);
                if (hp.Current <= 0f) CombatLife.ApplyHitCapabilities(repo, e, in hp);
            }
        }
    }
}
