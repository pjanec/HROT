using System;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-5000</c> part 2 (ruling 9) — THE per-thread "has anything of <typeparamref name="TComp"/> changed since this thread last built its view table?" cache that every
    /// damage view table repeats (R-220: a steady frame hands back the same table, 0 B). True while no <typeparamref name="TComp"/> was written since the stamp was taken
    /// (<see cref="Fdp.Toolkit.Replication.Utilities.FrameStamp"/>), none was added (the add counter), and every entity read is still alive and still carries it (only walked
    /// after a structural change). Per CLOSED generic type, per thread.
    /// </summary>
    internal static class ViewTableCache<TComp> where TComp : unmanaged
    {
        [ThreadStatic] private static Fdp.Toolkit.Replication.Utilities.FrameStamp t_stamp;
        [ThreadStatic] private static EntityRepository? t_repo;
        [ThreadStatic] private static long t_structural, t_added;
        [ThreadStatic] private static Entity[]? t_entities;
        [ThreadStatic] private static int t_count;
        [ThreadStatic] private static EntityRepository? t_queryRepo;
        [ThreadStatic] private static EntityQuery? t_query;

        /// <summary>True when this thread's last build over <paramref name="repo"/> still holds.</summary>
        public static bool Holds(EntityRepository repo)
        {
            if (!ReferenceEquals(repo, t_repo) || !t_stamp.HoldsSinceTaken(repo, typeof(TComp)) || repo.ComponentsAddedOf(ComponentType<TComp>.ID) != t_added) return false;
            if (repo.StructuralChanges == t_structural) return true;
            for (int k = 0; k < t_count; k++)
                if (!repo.IsAlive(t_entities![k]) || !repo.HasComponent<TComp>(t_entities[k])) return false;
            t_structural = repo.StructuralChanges;
            return true;
        }

        /// <summary>Starts a rebuild over <paramref name="repo"/>: the cached query of every <typeparamref name="TComp"/> entity (the entity list cleared).</summary>
        public static EntityQuery Begin(EntityRepository repo)
        {
            t_count = 0;
            if (!ReferenceEquals(repo, t_queryRepo) || t_query == null)
            {
                t_query = repo.Query().With<TComp>().WithLifecycle(EntityLifecycle.All).Build();
                t_queryRepo = repo;
            }
            return t_query;
        }

        /// <summary>Notes an entity the rebuild read (re-checked after a structural change).</summary>
        public static void Read(Entity e)
        {
            var a = t_entities ??= new Entity[8];
            if (t_count == a.Length) Array.Resize(ref t_entities, a.Length * 2);
            t_entities![t_count++] = e;
        }

        /// <summary>Ends a rebuild: the stamp taken now.</summary>
        public static void Taken(EntityRepository repo)
        {
            t_stamp.Take(repo); t_repo = repo; t_structural = repo.StructuralChanges; t_added = repo.ComponentsAddedOf(ComponentType<TComp>.ID);
        }
    }
}
