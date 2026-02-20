using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.ImGui.Adapters
{
    public class SimulationViewAdapter : IInspectableSession
    {
        private readonly ISimulationView _view;
        
        // Caches
        private static readonly MethodInfo _hasCompGeneric;
        private static readonly MethodInfo _hasManagedCompGeneric;
        private static readonly MethodInfo _getCompGeneric;
        private static readonly MethodInfo _getManagedCompGeneric;
        
        private static readonly Dictionary<Type, MethodInfo> _hasCache = new();
        private static readonly Dictionary<Type, MethodInfo> _getCache = new();

        static SimulationViewAdapter()
        {
             var methods = typeof(ISimulationView).GetMethods();
             
             // Assumes unambiguous method names or filters by param count if needed
             _hasCompGeneric = methods.First(m => m.Name == "HasComponent" && m.IsGenericMethod);
             _hasManagedCompGeneric = methods.First(m => m.Name == "HasManagedComponent" && m.IsGenericMethod);
             _getCompGeneric = methods.First(m => m.Name == "GetComponentRO" && m.IsGenericMethod);
             _getManagedCompGeneric = methods.First(m => m.Name == "GetManagedComponentRO" && m.IsGenericMethod);
        }

        public SimulationViewAdapter(ISimulationView view)
        {
            _view = view;
        }

        public bool IsReadOnly => true;
        // ISimulationView doesn't typically expose EntityCount or iteration, returning 0/empty.
        public int EntityCount => 0; 
        public IEnumerable<Entity> GetEntities() => Enumerable.Empty<Entity>();

        public bool HasComponent(Entity e, Type t)
        {
            if (!_hasCache.TryGetValue(t, out var m))
            {
                if (t.IsValueType) 
                     m = _hasCompGeneric.MakeGenericMethod(t);
                else 
                     m = _hasManagedCompGeneric.MakeGenericMethod(t);
                _hasCache[t] = m;
            }
            return (bool)m.Invoke(_view, new object[] { e })!;
        }
        
        public object? GetComponent(Entity e, Type t)
        {
             if (!_getCache.TryGetValue(t, out var m))
             {
                 if (t.IsValueType)
                      m = _getCompGeneric.MakeGenericMethod(t);
                 else 
                      m = _getManagedCompGeneric.MakeGenericMethod(t);
                 _getCache[t] = m;
             }
             return m.Invoke(_view, new object[] { e });
        }

        public void SetComponent(Entity e, Type t, object data) => throw new InvalidOperationException("Read Only Session");

        public IEnumerable<Type> GetAllComponentTypes() => ComponentTypeRegistry.GetAllTypes();
    }
}
