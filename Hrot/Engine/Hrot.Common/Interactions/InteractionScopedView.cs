using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Hrot.Common.Events;

namespace Hrot.Common.Interactions
{
    // Wraps ISimulationView to route a strict whitelist of interaction event reads
    // through a module-private FdpEventBus rather than the global world bus.
    // All structural ECS queries and component mutations delegate transparently to
    // the underlying view.
    //
    // Whitelist for ReadEvents<T>:
    //   GizmoDragUpdateEvent, GizmoMouseEvent, GizmoKeyEvent,
    //   GizmoInteractionStartedEvent, GizmoInteractionCommitEvent,
    //   GizmoInteractionCancelEvent, GlobalActionRequestedEvent
    //
    // Whitelist for ReadManagedEvents<T>:
    //   ContextActionTriggered
    //
    // PublishEvent<GlobalActionRequestedEvent> in the command buffer is routed to
    // the interaction bus; all other PublishEvent calls go to the real ECB.
    public sealed class InteractionScopedView : ISimulationView
    {
        private readonly ISimulationView _inner;
        private readonly FdpEventBus _interactionBus;
        private readonly InteractionScopedCommandBuffer _cmdBuf;

        public InteractionScopedView(ISimulationView inner, FdpEventBus interactionBus)
        {
            _inner          = inner;
            _interactionBus = interactionBus;
            _cmdBuf         = new InteractionScopedCommandBuffer(inner.GetCommandBuffer(), interactionBus);
        }

        public uint  Tick  => _inner.Tick;
        public float Time  => _inner.Time;

        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => ref _inner.GetComponentRO<T>(e);

        public T GetManagedComponentRO<T>(Entity e) where T : class
            => _inner.GetManagedComponentRO<T>(e);

        public bool IsAlive(Entity e)                              => _inner.IsAlive(e);
        public bool HasComponent<T>(Entity e) where T : unmanaged => _inner.HasComponent<T>(e);
        public bool HasManagedComponent<T>(Entity e) where T : class => _inner.HasManagedComponent<T>(e);
        public QueryBuilder Query()                                => _inner.Query();
        public IEntityCommandBuffer GetCommandBuffer()             => _cmdBuf;

        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged
        {
            if (typeof(T) == typeof(GizmoDragUpdateEvent)           ||
                typeof(T) == typeof(GizmoMouseEvent)                 ||
                typeof(T) == typeof(GizmoKeyEvent)                   ||
                typeof(T) == typeof(GizmoInteractionStartedEvent)    ||
                typeof(T) == typeof(GizmoInteractionCommitEvent)     ||
                typeof(T) == typeof(GizmoInteractionCancelEvent)     ||
                typeof(T) == typeof(GlobalActionRequestedEvent))
            {
                return _interactionBus.Read<T>();
            }
            return _inner.ReadEvents<T>();
        }

        public IReadOnlyList<T> ReadManagedEvents<T>()
        {
            if (typeof(T) == typeof(ContextActionTriggered))
                return _interactionBus.ReadManaged<T>();
            return _inner.ReadManagedEvents<T>();
        }
    }

    // Routes PublishEvent<GlobalActionRequestedEvent> to the interaction bus;
    // all other event publishes and component mutations delegate to the real ECB.
    internal sealed class InteractionScopedCommandBuffer : IEntityCommandBuffer
    {
        private readonly IEntityCommandBuffer _realEcb;
        private readonly FdpEventBus _interactionBus;

        public InteractionScopedCommandBuffer(IEntityCommandBuffer realEcb, FdpEventBus interactionBus)
        {
            _realEcb        = realEcb;
            _interactionBus = interactionBus;
        }

        public void PublishEvent<T>(in T evt) where T : unmanaged
        {
            if (typeof(T) == typeof(GlobalActionRequestedEvent))
            {
                _interactionBus.Publish(evt);
                return;
            }
            _realEcb.PublishEvent(evt);
        }

        public Entity CreateEntity()                                                               => _realEcb.CreateEntity();
        public void   DestroyEntity(Entity entity)                                                 => _realEcb.DestroyEntity(entity);
        public void   AddComponent<T>(Entity entity, in T component) where T : unmanaged           => _realEcb.AddComponent(entity, component);
        public void   SetComponent<T>(Entity entity, in T component) where T : unmanaged           => _realEcb.SetComponent(entity, component);
        public void   RemoveComponent<T>(Entity entity) where T : unmanaged                        => _realEcb.RemoveComponent<T>(entity);
        public void   AddManagedComponent<T>(Entity entity, T? component) where T : class          => _realEcb.AddManagedComponent(entity, component);
        public void   SetManagedComponent<T>(Entity entity, T? component) where T : class          => _realEcb.SetManagedComponent(entity, component);
        public void   RemoveManagedComponent<T>(Entity entity) where T : class                     => _realEcb.RemoveManagedComponent<T>(entity);
        public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size)         => _realEcb.SetComponentRaw(entity, typeId, ptr, size);
        public void   SetManagedComponentRaw(Entity entity, int typeId, object obj)                => _realEcb.SetManagedComponentRaw(entity, typeId, obj);
        public void   SetLifecycleState(Entity entity, EntityLifecycle state)                      => _realEcb.SetLifecycleState(entity, state);
    }
}
