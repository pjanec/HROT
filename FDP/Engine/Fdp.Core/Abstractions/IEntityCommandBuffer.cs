using System;
using Fdp.Core;

namespace Fdp.Interfaces
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
        /// <summary>
        /// Adds a zero-initialized unmanaged component to the entity.
        /// Bypasses the 1024-byte ECB payload limit for large components like blackboards.
        /// </summary>
        void AddEmptyComponent<T>(Entity entity) where T : unmanaged;
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
        /// Ruling 14 — writes <paramref name="size"/> bytes at <paramref name="byteOffset"/> inside an
        /// existing unmanaged component and <b>touches nothing else</b>.
        ///
        /// <para>
        /// ⭐ <b>The surgical counterpart to <see cref="SetComponentRaw"/>.</b> A whole-component write
        /// carries every field the caller did not mean to change back to whatever they were when the
        /// payload was read — which, on a shared blackboard, reverts unrelated subsystems by a tick.
        /// </para>
        ///
        /// <para>
        /// ⚠⚠ <b>This member has a DEFAULT implementation deliberately, and the reason is a count:</b>
        /// this interface has <b>12</b> implementers — one real buffer, two production wrappers that
        /// delegate to it, and <b>nine test mocks</b>. A required member would force nine test files to
        /// grow a body for a method they never call. ⛔ The default <b>throws</b> rather than no-ops:
        /// a silent no-op here is a lost edit, which is precisely the class of defect this method
        /// exists to remove. ⭐ The real buffer and both production wrappers override it.
        /// </para>
        /// </summary>
        unsafe void SetComponentFieldRaw(Entity entity, int typeId, int byteOffset, void* ptr, int size)
            => throw new NotSupportedException(
                $"{GetType().Name} does not implement SetComponentFieldRaw. A surgical field write "
                + "cannot be emulated by a whole-component write without reverting the component's "
                + "other fields, so this fails loudly rather than silently doing the wrong thing.");

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
