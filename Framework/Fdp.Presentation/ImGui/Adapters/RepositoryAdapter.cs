using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Utils;

namespace FDP.Toolkit.ImGui.Adapters
{
    public class RepositoryAdapter : IInspectableSession
    {
        private readonly EntityRepository _repo;

        public RepositoryAdapter(EntityRepository repo)
        {
            _repo = repo;
        }

        public bool IsReadOnly => false;

        public int EntityCount => _repo.EntityCount;

        public IEnumerable<Entity> GetEntities()
        {
            // Iterate all active entities and materialize to avoid iterator state machine issues with ref structs
            var list = new List<Entity>();
            foreach(var e in _repo.Query().Build())
            {
                list.Add(e);
            }
            return list;
        }

        public bool HasComponent(Entity e, Type componentType)
        {
            return RepoReflector.HasComponent(_repo, e, componentType);
        }

        public object? GetComponent(Entity e, Type componentType)
        {
            return RepoReflector.GetComponent(_repo, e, componentType);
        }

        public void SetComponent(Entity e, Type componentType, object componentData)
        {
            RepoReflector.SetComponent(_repo, e, componentType, componentData);
        }

        public IEnumerable<Type> GetAllComponentTypes()
        {
            return ComponentTypeRegistry.GetAllTypes();
        }
    }
}
