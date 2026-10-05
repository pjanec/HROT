using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// ⭐ <c>CE-2074</c> (R-200) — applies <see cref="SetRoeEvent"/>: the ONE writer of <see cref="Roe"/> after spawn.
    /// Gated like the behaviour ingress (<see cref="BehaviorOriginRank"/>): an order may change the ROE only if its rank is
    /// at least that of whoever set it; the TKB default (<see cref="BehaviorOrigin.Unmarked"/>) yields to anyone.
    /// 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.4.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class RoeSystem : IEcsModuleSystem
    {
        /// <summary>Refused changes since this system was built (a test / diagnostics probe).</summary>
        public int RefusedCount { get; private set; }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.IsComponentTypeRegistered<Roe>()) return;

            foreach (var evt in repo.Bus.Read<SetRoeEvent>())
                if (!Apply(repo, evt)) RefusedCount++;
        }

        /// <summary>⭐ <c>CE-3043</c> — what a <see cref="SetRoeEvent"/> does, applied NOW (the editor's paused edit and this
        /// system share it). <c>false</c> = refused by the gate, or a dead entity.</summary>
        public static bool Apply(EntityRepository repo, in SetRoeEvent evt)
        {
            if (!repo.IsAlive(evt.Entity) || !repo.IsComponentTypeRegistered<Roe>()) return false;
            if (!repo.HasComponent<Roe>(evt.Entity)) repo.AddComponent(evt.Entity, new Roe());

            ref var roe = ref repo.GetComponentRW<Roe>(evt.Entity);
            var origin = evt.Origin == BehaviorOrigin.Self ? roe.SetBy : evt.Origin;
            if (roe.SetBy != BehaviorOrigin.Unmarked
                && BehaviorOriginRank.Of(origin) < BehaviorOriginRank.Of(roe.SetBy))
                return false;

            if (evt.Fire != RoeFire.FireUnset) roe.Fire = evt.Fire;
            if (evt.Reactions != RoeReactions.ReactionsUnset) roe.Reactions = evt.Reactions;
            if (evt.ReturnFireWindowSeconds > 0f) roe.ReturnFireWindowSeconds = evt.ReturnFireWindowSeconds;   // CE-2095
            roe.SetBy = origin == BehaviorOrigin.Unmarked ? BehaviorOrigin.Operator : origin;
            return true;
        }
    }
}
