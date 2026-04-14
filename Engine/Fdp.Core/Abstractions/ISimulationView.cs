using System;
using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// Read-only view of simulation state.
    /// Abstraction over EntityRepository (GDB) and SimSnapshot (SoD).
    /// Modules use this interface without knowing the underlying strategy.
    /// </summary>
    public interface ISimulationView
    {
        /// <summary>
        /// Current simulation tick (frame number).
        /// </summary>
        uint Tick { get; }
        
        /// <summary>
        /// Current simulation time in seconds.
        /// </summary>
        float Time { get; }
        
        /// <summary>
        /// Gets read-only reference to unmanaged component (Tier 1).
        /// Throws if entity doesn't have component.
        /// </summary>
        ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged;
        
        /// <summary>
        /// Gets managed component (Tier 2).
        /// Returns immutable record/class.
        /// Throws if entity doesn't have component.
        /// </summary>
        T GetManagedComponentRO<T>(Entity e) where T : class;
        
        /// <summary>
        /// Checks if entity is alive (not destroyed).
        /// </summary>
        bool IsAlive(Entity e);

        /// <summary>
        /// Checks if entity has component (Unified/Unmanaged).
        /// </summary>
        bool HasComponent<T>(Entity e) where T : unmanaged;

        /// <summary>
        /// Checks if entity has managed component.
        /// </summary>
        bool HasManagedComponent<T>(Entity e) where T : class;
        
        /// <summary>
        /// Consumes all accumulated events of type T.
        /// Returns zero-copy span of events.
        /// Events include history since module's last run.
        /// </summary>
        ReadOnlySpan<T> ConsumeEvents<T>() where T : unmanaged;
        
        /// <summary>
        /// Creates a query builder for iterating entities.
        /// </summary>
        QueryBuilder Query();

        /// <summary>
        /// Consumes all captured managed events of type T.
        /// Returns a read-only list (snapshot) of events.
        /// Accepts both reference types (classes) and managed structs (structs containing references).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<T> ConsumeManagedEvents<T>();

        /// <summary>
        /// Acquires a command buffer for queueing mutations.
        /// Modules use this to queue changes (create/destroy entities, add/remove components).
        /// Commands are played back on main thread after module execution.
        /// 
        /// Thread-safe: Each module gets its own command buffer.
        /// </summary>
        IEntityCommandBuffer GetCommandBuffer();
    }
}
