using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;

namespace Fdp.Presentation.Adapters
{
    public class RepositoryAdapter : IInspectableSession
    {
        private readonly EntityRepository _repo;

        /// <summary>
        /// Reserved pseudo-entity handle representing all ECS singleton components.
        /// Always appears as the first entry in <see cref="GetEntities"/>.
        /// Component queries for this entity are routed to the singleton storage paths
        /// instead of the standard dynamic-entity tables.
        /// </summary>
        public static readonly Entity SingletonEntity = new Entity(int.MaxValue, ushort.MaxValue);

        public RepositoryAdapter(EntityRepository repo)
        {
            _repo = repo;
        }

        /// <summary>Exposes the underlying ECS repository for renderers that need singleton access.</summary>
        public EntityRepository Repo => _repo;

        public bool IsReadOnly => false;

        public int EntityCount => _repo.EntityCount;

        public bool IsAlive(Entity e)
        {
            if (e == SingletonEntity) return true;
            return _repo.IsAlive(e);
        }

        public IEnumerable<Entity> GetEntities()
        {
            // Singleton pseudo-entity is always the first entry.
            var list = new List<Entity>();
            list.Add(SingletonEntity);
            foreach(var e in _repo.Query().Build())
            {
                list.Add(e);
            }
            return list;
        }

        public bool HasComponent(Entity e, Type componentType)
        {
            if (e == SingletonEntity) return RepoReflector.HasSingleton(_repo, componentType);
            return RepoReflector.HasComponent(_repo, e, componentType);
        }

        public object? GetComponent(Entity e, Type componentType)
        {
            if (e == SingletonEntity) return RepoReflector.GetSingleton(_repo, componentType);
            return RepoReflector.GetComponent(_repo, e, componentType);
        }

        public void SetComponent(Entity e, Type componentType, object componentData)
        {
            if (e == SingletonEntity)
                RepoReflector.SetSingleton(_repo, componentType, componentData);
            else
                RepoReflector.SetComponent(_repo, e, componentType, componentData);
        }

        public IEnumerable<Type> GetAllComponentTypes()
        {
            return ComponentTypeRegistry.GetAllTypes();
        }

        public bool HasAuthority(Entity e, Type componentType)
        {
            // Global singletons are owned by this node (no per-entity authority mask).
            if (e == SingletonEntity) return true;
            int typeId = ComponentTypeRegistry.GetId(componentType);
            if (typeId < 0) return false;
            return _repo.HasAuthority(e, typeId);
        }
    }
}
