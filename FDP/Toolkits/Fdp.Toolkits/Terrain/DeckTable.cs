using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4692</c> BL-3 (📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §3) — the laid-bridge decks that are STILL STANDING in one view: the entity index of every
    /// <see cref="ObstacleKind.WalkableDeck"/> obstacle that is Active or Constructing. A deck the baked world still holds (<see cref="TerrainWalkable.Obstacle"/>) whose obstacle is not
    /// here has FALLEN: the floor, sight and fire skip it AT ONCE (<see cref="TerrainWorld.DeckGone"/>), as a felled trunk is skipped (CE-4603), until the rebake takes it out — so
    /// the floor never stands where the bridge is gone. ⭐ Per thread, per view, rebuilt only when the view's structure changed (a destroy or a lifecycle change is structural);
    /// a steady frame hands back the same table (0 B, R-220).
    /// </summary>
    [Fdp.Core.WorldDerived("live deck set recomputed from obstacle entities")]
    public sealed class DeckTable
    {
        private readonly int[] _sorted;
        private DeckTable(int[] sorted) => _sorted = sorted;

        /// <summary>No deck stands (the view registers obstacles, but none is a standing deck).</summary>
        public static readonly DeckTable None = new(Array.Empty<int>());

        /// <summary>How many decks stand.</summary>
        public int Count => _sorted.Length;

        /// <summary>True when the deck of obstacle <paramref name="obstacle"/> (an entity index) still stands in this view. ⛔ No allocation.</summary>
        public bool Standing(int obstacle) => Array.BinarySearch(_sorted, obstacle) >= 0;

        [ThreadStatic] private static DeckTable? t_last;
        [ThreadStatic] private static EntityRepository? t_repo;
        [ThreadStatic] private static long t_structural, t_version;
        [ThreadStatic] private static EntityQuery? t_query;
        [ThreadStatic] private static EntityRepository? t_queryRepo;
        [ThreadStatic] private static List<int>? t_scratch;

        /// <summary>The standing decks of <paramref name="view"/>; null when it does not register static obstacles (nothing to skip by).</summary>
        public static DeckTable? Of(ISimulationView view)
        {
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<StaticObstacle>()) return null;
                // a destroy and a lifecycle change are STRUCTURAL; the global version catches a snapshot re-synced under the same structure count
                if (ReferenceEquals(repo, t_repo) && t_last != null && repo.StructuralChanges == t_structural && repo.GlobalVersion == t_version) return t_last;
                if (!ReferenceEquals(repo, t_queryRepo) || t_query == null)
                {
                    t_query = repo.Query().With<StaticObstacle>().WithLifecycle(EntityLifecycle.All).Build();
                    t_queryRepo = repo;
                }
                var list = t_scratch ??= new List<int>(8);
                list.Clear();
                foreach (var e in t_query)
                {
                    if (repo.GetComponentRO<StaticObstacle>(e).Kind != ObstacleKind.WalkableDeck) continue;
                    var state = repo.GetLifecycleState(e);
                    if (state == EntityLifecycle.Active || state == EntityLifecycle.Constructing) list.Add(e.Index);
                }
                t_repo = repo; t_structural = repo.StructuralChanges; t_version = repo.GlobalVersion;
                return t_last = Same(t_last, list) ? t_last! : Make(list);
            }
            var q = view.Query().With<StaticObstacle>().WithLifecycle(EntityLifecycle.Active).Build();
            var all = new List<int>();
            foreach (var e in q) if (view.GetComponentRO<StaticObstacle>(e).Kind == ObstacleKind.WalkableDeck) all.Add(e.Index);
            return Make(all);
        }

        private static DeckTable Make(List<int> list)
        {
            if (list.Count == 0) return None;
            var a = list.ToArray();
            Array.Sort(a);
            return new DeckTable(a);
        }

        private static bool Same(DeckTable? last, List<int> list)
        {
            if (last == null || last._sorted.Length != list.Count) return false;
            list.Sort();
            for (int i = 0; i < list.Count; i++) if (last._sorted[i] != list[i]) return false;
            return true;
        }
    }
}
