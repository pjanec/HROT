using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Lifecycle.Systems
{
    /// <summary>
    /// Processes lifecycle events (ACKs) and manages entity state transitions.
    /// Runs in BeforeSync phase to ensure changes are visible to all modules.
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class LifecycleSystem : IEcsModuleSystem
    {
        private readonly EntityLifecycleModule _manager;
        
        public LifecycleSystem(EntityLifecycleModule manager)
        {
            _manager = manager;
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            uint currentFrame = view.Tick;
            
            // Process construction ACKs
            var constructionAcks = view.ReadEvents<ConstructionAck>();
            foreach (var ack in constructionAcks)
            {
                _manager.ProcessConstructionAck(ack, currentFrame, cmd);
            }
            
            // Process destruction ACKs
            var destructionAcks = view.ReadEvents<DestructionAck>();
            foreach (var ack in destructionAcks)
            {
                _manager.ProcessDestructionAck(ack, currentFrame, cmd);
            }
            
            // Drain zero-participant pending constructions/destructions that are ready immediately
            _manager.DrainInstantComplete(cmd);

            // Check for timeouts
            _manager.CheckTimeouts(currentFrame, cmd);
        }
    }
}
