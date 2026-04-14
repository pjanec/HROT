using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Lifecycle.Systems
{
    /// <summary>
    /// Removes transient components from entities as soon as they become Active.
    /// Ensures initialization data doesn't linger.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class LifecycleCleanupSystem : IEcsModuleSystem
    {
        private List<int> _transientTypes = default!;
        private readonly MethodInfo _removeUnmanagedMethod;
        private readonly MethodInfo _removeManagedMethod;

        public LifecycleCleanupSystem()
        {
            _removeUnmanagedMethod = GetType().GetMethod(nameof(RemoveTransientUnmanaged), BindingFlags.NonPublic | BindingFlags.Instance)!;
            _removeManagedMethod = GetType().GetMethod(nameof(RemoveTransientManaged), BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        private void InitializeTransientTypes()
        {
            if (_transientTypes != null) return;
            _transientTypes = new List<int>();

            // Iterate actual registered IDs (not a dense 0‥count range, since IDs are explicit and sparse)
            foreach (var id in ComponentTypeRegistry.GetAllTypeIds())
            {
                // Transient = !Snapshot & !Record & !Save
                if (!ComponentTypeRegistry.IsSnapshotable(id) &&
                    !ComponentTypeRegistry.IsRecordable(id) &&
                    !ComponentTypeRegistry.IsSaveable(id))
                {
                    _transientTypes.Add(id);
                }
            }
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            InitializeTransientTypes();

            var cmd = view.GetCommandBuffer();
            
            foreach (var typeId in _transientTypes)
            {
                var type = ComponentTypeRegistry.GetType(typeId);
                if (type == null) continue;

                if (type.IsValueType)
                {
                    // Unmanaged path
                    // Note: This relies on reflection which has some overhead, 
                    // but we only do it once per type per frame, not per entity.
                    var generic = _removeUnmanagedMethod.MakeGenericMethod(type);
                    generic.Invoke(this, new object[] { view, cmd });
                }
                else
                {
                    // Managed path
                    var generic = _removeManagedMethod.MakeGenericMethod(type);
                    generic.Invoke(this, new object[] { view, cmd });
                }
            }
        }

        private void RemoveTransientUnmanaged<T>(ISimulationView view, IEntityCommandBuffer cmd) where T : unmanaged
        {
             foreach (var entity in view.Query()
                 .With<T>()
                 .WithLifecycle(EntityLifecycle.Active)
                 .Build()) cmd.RemoveComponent<T>(entity);
        }

        private void RemoveTransientManaged<T>(ISimulationView view, IEntityCommandBuffer cmd) where T : class
        {
             foreach (var entity in view.Query()
                 .WithManaged<T>()
                 .WithLifecycle(EntityLifecycle.Active)
                 .Build()) cmd.RemoveManagedComponent<T>(entity);
        }
    }
}
