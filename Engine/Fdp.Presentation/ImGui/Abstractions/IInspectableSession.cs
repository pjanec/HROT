using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Presentation.Abstractions
{
    public interface IInspectableSession
    {
        bool IsReadOnly { get; }
        int EntityCount { get; }
        
        IEnumerable<Entity> GetEntities();

        bool IsAlive(Entity e);

        bool HasComponent(Entity e, Type componentType);
        object? GetComponent(Entity e, Type componentType);
        void SetComponent(Entity e, Type componentType, object componentData);
        
        IEnumerable<Type> GetAllComponentTypes();

        /// <summary>
        /// Returns <c>true</c> if the local node holds authority over the specified component type
        /// on the given entity. Used by the Entity Inspector UI to colour-code authority boundaries.
        /// </summary>
        bool HasAuthority(Entity e, Type componentType);
    }
}
