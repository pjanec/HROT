using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Editor-side <see cref="AiTracerCoordinator"/> subclass that physically arms/disarms
    /// per-entity trace buffers by posting <see cref="PatchDebugStateCommand"/> to the world bus.
    /// </summary>
    public sealed class EditorAiTracerCoordinator : AiTracerCoordinator
    {
        private readonly EntityRepository _world;
        private readonly HashSet<Entity> _armedEntities = new();

        public EditorAiTracerCoordinator(EntityRepository world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void ArmEntity(Entity entity)
        {
            if (!_world.IsAlive(entity)) return;
            _armedEntities.Add(entity);
            PatchEntity(entity, enable: true);
        }

        public void DisarmEntity(Entity entity)
        {
            _armedEntities.Remove(entity);
            if (_world.IsAlive(entity))
                PatchEntity(entity, enable: false);
        }

        public void DisarmAll()
        {
            foreach (var e in _armedEntities)
                if (_world.IsAlive(e))
                    PatchEntity(e, enable: false);
            _armedEntities.Clear();
        }

        private void PatchEntity(Entity entity, bool enable)
        {
            _world.Bus.PublishManaged(new PatchDebugStateCommand
            {
                Target    = entity,
                PatchJson = enable
                    ? "{\"Behavior\":{\"EnableTraceBuffer\":true}}"
                    : "{\"Behavior\":{\"EnableTraceBuffer\":false}}",
            });
        }
    }
}
