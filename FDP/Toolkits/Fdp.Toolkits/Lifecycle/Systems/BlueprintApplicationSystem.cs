using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Lifecycle.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    [TickMode(TickPolicy.EveryFrame)]
    public class BlueprintApplicationSystem : IEcsModuleSystem
    {
        private readonly ITkbDatabase _tkb;
        private readonly IReadOnlyList<ITkbEntityTranslator> _translators;

        public BlueprintApplicationSystem(
            ITkbDatabase tkb,
            IReadOnlyList<ITkbEntityTranslator>? translators = null)
        {
            _tkb = tkb;
            _translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            // We need direct access to Repository to apply templates immediately
            if (view is not EntityRepository repo)
            {
                return;
            }

            // Consume ConstructionOrder events
            var orders = view.ReadEvents<ConstructionOrder>();
            foreach (ref readonly var order in orders)
            {
                // ⭐ CE-2419 — an order can outlive its entity (torn down in the frame it was queued — 📐 seen when the master ejected the brain
                //   mid-load): nothing is left to construct. ⛔ Before, the injection threw "is not alive" and the unhandled exception killed the node.
                if (!repo.IsAlive(order.Entity)) continue;
                if (_tkb.TryGetByType(order.BlueprintId, out var template))
                {
                    foreach (var t in _translators)
                        t.Inject(repo, order.Entity, template);
                }
            }
        }
    }
}
