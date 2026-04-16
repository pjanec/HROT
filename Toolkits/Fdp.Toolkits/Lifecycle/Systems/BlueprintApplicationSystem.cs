using System;
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

        public BlueprintApplicationSystem(ITkbDatabase tkb)
        {
            _tkb = tkb;
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
                    // Apply template with preservation
                    template.ApplyTo(repo, order.Entity, preserveExisting: true);
                }
            }
        }
    }
}
