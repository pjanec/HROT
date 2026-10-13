using System;
using System.Collections.Generic;
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
    public sealed class BleedOutSystem : IEcsModuleSystem
    {
        /// <summary>How much bleeding time is gathered before it is taken off the health (s).</summary>
        public const float StepSeconds = 1f;

        private readonly Dictionary<Entity, float> _pending = new();
        private readonly List<Entity> _gone = new();

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo
                || !repo.IsComponentTypeRegistered<InjuryThresholds>() || !repo.IsComponentTypeRegistered<Health>()) return;

            foreach (var e in repo.Query().With<InjuryThresholds>().With<Health>().Build())
            {
                if (repo.HasComponent<NetworkAuthority>(e) && !repo.HasAuthority<Health>(e)) { _pending.Remove(e); continue; }
                ref readonly var health = ref repo.GetComponentRO<Health>(e);
                float rate = repo.GetComponentRO<InjuryThresholds>(e).BleedPerSecond;
                if (rate <= 0f || Injury.StateOf(repo, e, in health) != InjuryState.Incapacitated) { _pending.Remove(e); continue; }

                _pending.TryGetValue(e, out float bled);
                bled += deltaTime;
                if (bled < StepSeconds) { _pending[e] = bled; continue; }
                _pending.Remove(e);

                ref var hp = ref repo.GetComponentRW<Health>(e);
                hp.Current = MathF.Max(0f, hp.Current - rate * hp.Max * bled);
                if (hp.Current <= 0f) CombatLife.ApplyHitCapabilities(repo, e, in hp);
            }

            // Entities destroyed while bleeding.
            foreach (var e in _pending.Keys) if (!repo.IsAlive(e)) _gone.Add(e);
            foreach (var e in _gone) _pending.Remove(e);
            _gone.Clear();
        }
    }
}
