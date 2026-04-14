using System;
using System.Threading;
using Fdp.Kernel;
using Fdp.Kernel.Internal;
using ModuleHost.Core.Abstractions;

namespace Fdp.Kernel
{
    public sealed partial class EntityRepository : ISimulationView
    {
        // Thread-local command buffer for modules
        internal readonly ThreadLocal<EntityCommandBuffer> _perThreadCommandBuffer = new(() => new EntityCommandBuffer(), trackAllValues: true);

        // Properties
        uint ISimulationView.Tick => _globalVersion;
        
        float ISimulationView.Time => _simulationTime;
        
        // Methods
        
        IEntityCommandBuffer ISimulationView.GetCommandBuffer()
        {
            return _perThreadCommandBuffer.Value!;
        }

        ref readonly T ISimulationView.GetComponentRO<T>(Entity e)
        {
            // Delegate to existing internal methods via UnsafeShim or direct if accessible
            // Since we are in EntityRepository, we can call internal methods directly if we know them.
            // But GetComponentRO logic differs for managed/unmanaged.
            // ISimulationView splits them.
            
            // For unmanaged T:
            return ref GetUnmanagedComponentRO<T>(e);
        }
        
        T ISimulationView.GetManagedComponentRO<T>(Entity e)
        {
            // Call internal method directly
            var val = GetManagedComponentRO<T>(e);
            if (val == null) 
            {
                bool has = HasManagedComponent<T>(e);
                System.Console.WriteLine($"FATAL: Entity {e} GetManagedComponentRO<{typeof(T).Name}> returned null, but Has={has}. Idx={e.Index}");
                throw new InvalidOperationException($"Entity {e} missing component {typeof(T).Name}");
            }
            return val;
        }
        
        bool ISimulationView.IsAlive(Entity e)
        {
            return IsAlive(e);
        }

        bool ISimulationView.HasComponent<T>(Entity e)
        {
            return HasUnmanagedComponent<T>(e);
        }

        bool ISimulationView.HasManagedComponent<T>(Entity e)
        {
            return HasManagedComponent<T>(e);
        }
        
        ReadOnlySpan<T> ISimulationView.ConsumeEvents<T>()
        {
            return Bus.Consume<T>();
        }

        System.Collections.Generic.IReadOnlyList<T> ISimulationView.ConsumeManagedEvents<T>()
        {
            return Bus.ConsumeManaged<T>();
        }
        
        QueryBuilder ISimulationView.Query()
        {
            return Query();
        }
    }
}
