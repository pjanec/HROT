using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-4661</c> (thermal drivers TD-S5, TD-7; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4h) — THE one place <see cref="Health.StateSince"/> is
    /// stamped: on the node that owns the Health (<see cref="HealthOwnership.AppliesHere"/>), the frame the DERIVED injury state
    /// (<see cref="Injury.StateOf(ISimulationView, Entity, in Health)"/>) is SEEN to change, its time is the sim time now. Every Health writer (damage, bleeding,
    /// fire, …) is covered by construction — it watches the result, not the writers; one that runs after it in a frame is stamped the next frame (one frame
    /// late at most). First sight stamps nothing (<see cref="DeathCrossings.Changed"/>): a body a scenario, a reload or a late join brings already dead keeps
    /// the time it carries (0 = since the start). Replicas take it from <c>EntityDamage</c>. ⛔ The state is never stored — only its time (CE-2155).
    /// ⭐ Parallel contract: a serial system, last in the <c>HealthApplication</c> capability; writes only the Health this node owns. 0 B per steady frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class HealthStateSinceSystem : IEcsModuleSystem
    {
        private readonly DeathCrossings _states = new();
        private EntityQuery? _health;

        /// <summary>Stamps this node made (rails, diagnostics).</summary>
        public long Stamped { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Health>()) return;
            if (_states.Begin(repo, out bool replaying) || _health == null) _health = repo.Query().With<Health>().Build();
            if (replaying || !repo.HasSingletonUnmanaged<GlobalTime>()) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            foreach (var e in _health)
            {
                ref readonly var hp = ref repo.GetComponentRO<Health>(e);
                if (!_states.Changed(e, (byte)Injury.StateOf(repo, e, in hp)) || !HealthOwnership.AppliesHere(repo, e)) continue;
                repo.GetComponentRW<Health>(e).StateSince = now;
                Stamped++;
            }
        }
    }
}
