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

        /// <summary>⭐ CE-3386 (docs/DESIGN_Struct_Editor.md §10.4) — the write of a USER action (the inspector's OK): one step of the editor's history where one is
        /// recorded. ⛔ Not every write: <see cref="SetComponent"/> stays a plain write, because a write made INSIDE another step's apply / undo (a parameter rename
        /// rewriting its bindings) must never record a step of its own. Default: the plain write.</summary>
        void SetComponentAsUserStep(Entity e, Type componentType, object componentData, string label) => SetComponent(e, componentType, componentData);
        
        IEnumerable<Type> GetAllComponentTypes();

        /// <summary>
        /// Returns <c>true</c> if the local node holds authority over the specified component type
        /// on the given entity. Used by the Entity Inspector UI to colour-code authority boundaries.
        /// </summary>
        bool HasAuthority(Entity e, Type componentType);
    }
}
