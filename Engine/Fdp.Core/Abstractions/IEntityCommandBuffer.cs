using System;
using Fdp.Kernel;

namespace ModuleHost.Core.Abstractions
{
    /// <summary>
    /// Interface for recording deferred mutations to the world.
    /// Used by modules to safely queue changes.
    /// </summary>
    public interface IEntityCommandBuffer
    {
        Entity CreateEntity();
        void DestroyEntity(Entity entity);
        
        void AddComponent<T>(Entity entity, in T component) where T : unmanaged;
        void SetComponent<T>(Entity entity, in T component) where T : unmanaged;
        void RemoveComponent<T>(Entity entity) where T : unmanaged;
        
        void AddManagedComponent<T>(Entity entity, T? component) where T : class;
        void SetManagedComponent<T>(Entity entity, T? component) where T : class;
        void RemoveManagedComponent<T>(Entity entity) where T : class;
        
        /// <summary>
        /// Publishes an event to be processed in the next frame.
        /// </summary>
        void PublishEvent<T>(in T evt) where T : unmanaged;
        
        /// <summary>
        /// Sets an unmanaged component using raw pointer and type ID.
        /// </summary>
        unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size);

        /// <summary>
        /// Sets a managed component using object reference and type ID.
        /// </summary>
        void SetManagedComponentRaw(Entity entity, int typeId, object obj);

        /// <summary>
        /// Sets the lifecycle state of the entity (Constructing, Active, TearDown).
        /// </summary>
        void SetLifecycleState(Entity entity, EntityLifecycle state);
    }
}
