using System;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Internal;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Core
{
    public sealed partial class EntityRepository : ISimulationView
    {
        // Thread-local command buffer for modules
        internal readonly ThreadLocal<EntityCommandBuffer> _perThreadCommandBuffer = new(() => new EntityCommandBuffer(), trackAllValues: true);

        // Optional override for tests and advanced scenarios.
        // When set, GetCommandBuffer() returns this instead of the per-thread buffer.
        private IEntityCommandBuffer? _commandBufferOverride;

        /// <summary>
        /// Overrides the command buffer returned by GetCommandBuffer().
        /// Pass null to restore the default per-thread buffer.
        /// Intended for test fixtures that need EAGER entity creation semantics.
        /// </summary>
        public void SetCommandBufferOverride(IEntityCommandBuffer? ecb) => _commandBufferOverride = ecb;

        // Properties
        uint ISimulationView.Tick => _simulationTick;
        
        float ISimulationView.Time => _simulationTime;
        
        // Methods
        
        IEntityCommandBuffer ISimulationView.GetCommandBuffer()
        {
            return _commandBufferOverride ?? _perThreadCommandBuffer.Value!;
        }

        /// <summary>
        /// Plays back all pending per-thread command buffer operations into the repository.
        /// Call at the sync phase, after simulation systems have finished recording deferred ops.
        /// In production the scheduler calls this; in tests call it from TickFrame.
        /// </summary>
        public void FlushCommandBuffers()
        {
            foreach (var buffer in _perThreadCommandBuffer.Values)
                buffer.Playback(this);
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
        
        ReadOnlySpan<T> ISimulationView.ReadEvents<T>()
        {
            return Bus.Read<T>();
        }

        System.Collections.Generic.IReadOnlyList<T> ISimulationView.ReadManagedEvents<T>()
        {
            return Bus.ReadManaged<T>();
        }
        
        QueryBuilder ISimulationView.Query()
        {
            return Query();
        }
    }
}
