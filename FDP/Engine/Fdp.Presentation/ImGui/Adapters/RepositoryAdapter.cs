using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Replication.Edits;

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

        // ⭐ CE-2403 (R-2084) — refilled per call, not re-allocated (the inspector asks every frame). ⚠ The returned list is valid until the
        //   next call — every caller enumerates it at once or copies it (ParamUsage: ToList).
        private readonly List<Entity> _entities = new();

        public IEnumerable<Entity> GetEntities()
        {
            // Singleton pseudo-entity is always the first entry.
            var list = _entities;
            list.Clear();
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

        /// <summary>⭐ CE-3386 (docs/DESIGN_Struct_Editor.md §10.4) — the inspector's write is ONE step of the editor's history where one is recorded (Edit mode: the recorder
        /// is installed and on; Live / Preview / replay: no recorder, or it is off ⇒ the bracket is a no-op). Managed and unmanaged alike: the step is the world before / after.
        /// ⛔ Only the user's own commit calls this — a plain <see cref="SetComponent"/> made inside another step (ParamUsage.Rename under a parameter rename) records nothing.</summary>
        public void SetComponentAsUserStep(Entity e, Type componentType, object componentData, string label)
        {
            EditSteps.Begin(_repo, label);
            try { SetComponent(e, componentType, componentData); }
            catch { EditSteps.Cancel(_repo); throw; }
            EditSteps.End(_repo);
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
