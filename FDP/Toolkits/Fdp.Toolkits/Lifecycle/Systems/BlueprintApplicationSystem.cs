using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Lifecycle.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
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
            // We need direct access to Repository to apply templates immediately
            if (view is not EntityRepository repo)
            {
                return;
            }

            // Consume ConstructionOrder events
            var orders = view.ReadEvents<ConstructionOrder>();
            foreach (ref readonly var order in orders)
            {
                if (_tkb.TryGetByType(order.BlueprintId, out var template))
                {
                    foreach (var t in _translators)
                        t.Inject(repo, order.Entity, template);
                }
            }
        }
    }
}
