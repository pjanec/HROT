using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Kernel;
using Fdp.Toolkit.Lifecycle.Systems;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;

namespace Fdp.Examples.NetworkDemo.Configuration
{
    public static class DemoTopology
    {
        public static IEnumerable<object> GetSystems(ITkbDatabase tkb, EntityLifecycleModule elm, int localNodeId, IEventBus bus)
        {
            var systems = new List<object>();
            var entityMap = new NetworkEntityMap();

            // Lifecycle
            systems.Add(new LifecycleSystem(elm));
            systems.Add(new BlueprintApplicationSystem(tkb));
            
            // Replication
            systems.Add(new GhostCreationSystem(entityMap));
            systems.Add(new GhostPromotionSystem(tkb, elm));
            systems.Add(new SmartEgressSystem());
            
            // Demo Specific
            systems.Add(new RefactoredPlayerInputSystem());
            systems.Add(new PhysicsSystem());
            systems.Add(new CombatFeedbackSystem(localNodeId, bus));
            
            return systems;
        }
    }
}
