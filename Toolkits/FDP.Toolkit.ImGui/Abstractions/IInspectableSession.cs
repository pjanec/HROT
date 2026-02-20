using System;
using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.ImGui.Abstractions
{
    public interface IInspectableSession
    {
        bool IsReadOnly { get; }
        int EntityCount { get; }
        
        IEnumerable<Entity> GetEntities();
        
        bool HasComponent(Entity e, Type componentType);
        object? GetComponent(Entity e, Type componentType);
        void SetComponent(Entity e, Type componentType, object componentData);
        
        IEnumerable<Type> GetAllComponentTypes();
    }
}
