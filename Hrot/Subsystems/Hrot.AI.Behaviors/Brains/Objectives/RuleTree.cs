using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ CE-2384 (T3; docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.5) — the rules' tree as an INDEX, built once per frame on first use: every rule
    /// entity (<c>ObjectiveLinks</c> + <c>ObjectiveStatus</c>), its children by resolved <c>Parent</c>, and the rules about each resolved <c>Subject</c>.
    /// 📐 Before, every aggregator (<c>ObjectiveChildren.Of</c> / <c>.PointsOf</c>, <c>BestBehavior.Find</c> / <c>.Sibling</c>, a bounty lookup) walked EVERY
    /// rule and resolved its parent, per call, per frame — O(rules²) on a round whose rule count grows with every respawn (CE-2381).
    /// <para>⭐ Order: every list is in ascending ENTITY INDEX — the order the query walk gave — so <c>Tally.FirstFailed</c> and "the first of a tie"
    /// do not move. ⭐ Freshness: rebuilt when the world's <c>GlobalVersion</c> moves (a frame) or <see cref="Invalidate"/> was called (a rule's
    /// links appeared or its parent changed — <c>ObjectiveHost.Enter</c>), so a link made mid-frame is seen by the next reader in the same frame.</para>
    /// </summary>
    public sealed class RuleTree
    {
        private static readonly ConditionalWeakTable<EntityRepository, RuleTree> s_trees = new();

        private readonly List<Entity> _all = new();
        private readonly Dictionary<Entity, List<Entity>> _children = new();
        private readonly Dictionary<Entity, List<Entity>> _bySubject = new();
        private readonly Stack<List<Entity>> _pool = new();
        private EntityQuery? _query;
        private EntityRepository? _queryWorld;
        private uint _version;
        private bool _dirty = true;

        /// <summary>The number of rebuilds — a rail reads it to prove the index is built once per frame, not per call.</summary>
        public int Builds { get; private set; }

        /// <summary>How many times <paramref name="world"/>'s index was built (0 before the first) — without refreshing it.</summary>
        public static int BuildsOf(EntityRepository world) => s_trees.TryGetValue(world, out var tree) ? tree.Builds : 0;

        /// <summary>The world's index, fresh for this frame.</summary>
        public static RuleTree Of(EntityRepository world)
        {
            var tree = s_trees.GetValue(world, static _ => new RuleTree());
            tree.Refresh(world);
            return tree;
        }

        /// <summary>A rule's links appeared, or its parent / subject changed: the next reader rebuilds.</summary>
        public static void Invalidate(EntityRepository world)
        {
            if (s_trees.TryGetValue(world, out var tree)) tree._dirty = true;
        }

        /// <summary>Every rule entity, ascending entity index.</summary>
        public IReadOnlyList<Entity> All => _all;

        /// <summary>The rules whose resolved <c>Parent</c> is <paramref name="parent"/>, ascending entity index (empty for none or a null parent).</summary>
        public IReadOnlyList<Entity> ChildrenOf(Entity parent)
            => !parent.IsNull && _children.TryGetValue(parent, out var list) ? list : System.Array.Empty<Entity>();

        /// <summary>The rules whose resolved <c>Subject</c> is <paramref name="subject"/>, ascending entity index.</summary>
        public IReadOnlyList<Entity> About(Entity subject)
            => !subject.IsNull && _bySubject.TryGetValue(subject, out var list) ? list : System.Array.Empty<Entity>();

        private void Refresh(EntityRepository world)
        {
            if (!_dirty && _version == world.GlobalVersion) return;
            _dirty = false;
            _version = world.GlobalVersion;
            Builds++;

            Recycle(_children);
            Recycle(_bySubject);
            _all.Clear();
            if (!world.IsComponentTypeRegistered<ObjectiveLinks>() || !world.IsComponentTypeRegistered<ObjectiveStatus>()) return;
            if (_query is null || !ReferenceEquals(_queryWorld, world))
            {
                _query = world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build();
                _queryWorld = world;
            }
            foreach (var e in _query)
            {
                _all.Add(e);
                ref readonly var l = ref world.GetComponentRO<ObjectiveLinks>(e);
                Add(_children, l.Parent.Resolve(world), e);
                Add(_bySubject, l.Subject.Resolve(world), e);
            }
        }

        private void Add(Dictionary<Entity, List<Entity>> map, Entity key, Entity e)
        {
            if (key.IsNull) return;
            if (!map.TryGetValue(key, out var list)) map[key] = list = _pool.Count > 0 ? _pool.Pop() : new List<Entity>();
            list.Add(e);
        }

        private void Recycle(Dictionary<Entity, List<Entity>> map)
        {
            foreach (var list in map.Values) { list.Clear(); _pool.Push(list); }
            map.Clear();
        }
    }
}
