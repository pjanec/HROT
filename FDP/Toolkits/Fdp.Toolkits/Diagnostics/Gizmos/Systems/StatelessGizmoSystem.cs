using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ECS system that executes all registered <see cref="IStatelessGizmo"/> projectors
    /// for every matching, live entity each frame. Runs in
    /// <see cref="SystemPhase.PostSimulation"/>.
    ///
    /// <para>
    /// Unlike <see cref="DataDrivenGizmoSystem"/>, this system holds no per-entity state.
    /// One projector instance handles every entity that matches its component mask.
    /// </para>
    ///
    /// <para>
    /// Selection and visibility follow the same delegate pattern as
    /// <see cref="DataDrivenGizmoSystem"/>: when <c>isSelectedPredicate</c> is
    /// <c>null</c>, all matching entities are drawn; when non-null, only entities for
    /// which the predicate returns <c>true</c> are drawn.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class StatelessGizmoSystem : IEcsModuleSystem
    {
        private readonly StatelessGizmoRegistry _registry;
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Func<ISimulationView, Entity, bool>? _isSelectedPredicate;
        // ⚠ NOT readonly: the registry is mutable (Register/RegisterGlobal append), so these are grown
        // in Execute when it has. See EnsureCaches — CE-188.
        private bool[] _globalVisibilityCache;
        private bool[] _globalRulesVisibilityCache;

        // ⭐ CE-2394 — per rule, the entity indices that match it this frame (reused; grown, never shrunk)
        private int[][] _matches = System.Array.Empty<int[]>();
        private int[] _matchCount = System.Array.Empty<int>();

        private void EnsureMatchLists(int ruleCount)
        {
            if (_matches.Length >= ruleCount) return;
            int old = _matches.Length;
            System.Array.Resize(ref _matches, ruleCount);
            System.Array.Resize(ref _matchCount, ruleCount);
            for (int r = old; r < ruleCount; r++) _matches[r] = new int[64];
        }

        /// <summary>Max wall-clock budget in ms for entity iteration. 0 = unlimited.</summary>
        public float MaxGizmoFrameMs { get; set; } = 0f;

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <remarks>
        /// <para>⚠ This used to require that every projector be registered <i>before</i> construction,
        /// so the visibility caches were sized correctly. <b>That precondition was unsatisfiable and
        /// production violated it every run</b>: the registry is mutable, hosts register projectors after
        /// building the system, and an AI hot-reload registers more at any time. The caches are now grown
        /// on demand (<c>CE-188</c>), so registration order no longer matters.</para>
        /// </remarks>
        public StatelessGizmoSystem(
            StatelessGizmoRegistry registry,
            IDebugDrawBuilder drawBuilder,
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null)
        {
            _registry                   = registry    ?? throw new ArgumentNullException(nameof(registry));
            _drawBuilder                = drawBuilder ?? throw new ArgumentNullException(nameof(drawBuilder));
            _isSelectedPredicate        = isSelectedPredicate;
            _globalVisibilityCache      = new bool[registry.Rules.Count];
            _globalRulesVisibilityCache = new bool[registry.GlobalRules.Count];
        }

        // ---- IEcsModuleSystem -------------------------------------------------------

        /// <summary>
        /// Grows the visibility caches to match the registry, which can gain rules at any time.
        /// </summary>
        /// <remarks>
        /// <para><b>Why grow rather than bounds-check (<c>CE-188</c>).</b> The throw this fixes came from
        /// the global-rules dispatch loop indexing a cache sized at construction. Adding a bounds check
        /// there would have stopped the exception and left every late-registered global gizmo silently
        /// never drawing — trading a loud failure for a quiet one, which is the opposite of what this
        /// codebase needs. Growing the cache makes late registration simply work.</para>
        ///
        /// <para>Allocation happens only on the frames where the registry actually grew; the steady state
        /// is a length comparison.</para>
        /// </remarks>
        private void EnsureCaches()
        {
            int ruleCount = _registry.Rules.Count;
            if (_globalVisibilityCache.Length < ruleCount)
                Array.Resize(ref _globalVisibilityCache, ruleCount);

            int globalRuleCount = _registry.GlobalRules.Count;
            if (_globalRulesVisibilityCache.Length < globalRuleCount)
                Array.Resize(ref _globalRulesVisibilityCache, globalRuleCount);
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(StatelessGizmoSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only view ({view.GetType().Name}).");

            EnsureCaches();

            var rules = _registry.Rules;
            int ruleCount = rules.Count;

            // Evaluate and dispatch global (entity-less) rules first.
            var globalRules = _registry.GlobalRules;
            int globalRuleCount = globalRules.Count;
            for (int g = 0; g < globalRuleCount && g < _globalRulesVisibilityCache.Length; g++)
                _globalRulesVisibilityCache[g] = globalRules[g].VisibilityPolicy.IsGloballyEnabled(view);

            for (int g = 0; g < globalRuleCount; g++)
            {
                if (!_globalRulesVisibilityCache[g]) continue;
                globalRules[g].Projector.Draw(view, _drawBuilder);
            }

            // Pre-evaluate global visibility once per rule, not once per entity.
            for (int r = 0; r < ruleCount && r < _globalVisibilityCache.Length; r++)
                _globalVisibilityCache[r] = rules[r].VisibilityPolicy.IsGloballyEnabled(view);

            bool alwaysDraw = _isSelectedPredicate == null;
            var entityIndex = repo.GetEntityIndex();
            int maxIndex    = entityIndex.MaxIssuedIndex;
            float budget = MaxGizmoFrameMs;

            // ⭐ CE-2394 — ONE pass over the entities collects each visible rule's matches; then each rule draws its own, in the same
            //   order as before (rule by rule, ascending entity index). It walked every entity slot once PER RULE (~20), re-reading each
            //   one's metadata and component mask every time. ⛔ No allocation in the steady state: the match lists are reused and grow
            //   only when a rule matches more entities than ever before. The budget clock is a timestamp, not a Stopwatch object.
            EnsureMatchLists(ruleCount);
            for (int r = 0; r < ruleCount; r++) _matchCount[r] = 0;
            for (int i = 0; i <= maxIndex; i++)
            {
                ref readonly var metaSG = ref entityIndex.GetMetadata(i);
                if (!metaSG.IsActive) continue;
                ref var compSG = ref entityIndex.GetComponentMask(i);
                for (int r = 0; r < ruleCount; r++)
                {
                    if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r]) continue;
                    if (!BitMask512.HasAll(compSG, rules[r].RequiredMask)) continue;
                    var list = _matches[r];
                    int n = _matchCount[r];
                    if (n == list.Length) { System.Array.Resize(ref list, list.Length * 2); _matches[r] = list; }
                    list[n] = i;
                    _matchCount[r] = n + 1;
                }
            }

            long start = budget > 0f ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            long budgetTicks = budget > 0f ? (long)(budget * System.Diagnostics.Stopwatch.Frequency / 1000.0) : 0;
            bool budgetExceeded = false;

            for (int r = 0; r < ruleCount; r++)
            {
                // Skip rules whose global visibility policy rejects them.
                if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r])
                    continue;

                if (budgetExceeded) break;

                var rule = rules[r];
                var matched = _matches[r];
                int count = _matchCount[r];

                for (int m = 0; m < count; m++)
                {
                    int i = matched[m];
                    var entity = new Entity(i, entityIndex.GetMetadata(i).Generation);

                    if (!alwaysDraw && !_isSelectedPredicate!(view, entity))
                        continue;

                    // ⭐⭐ UXI-23 S4: the per-ENTITY half of IGizmoVisibilityPolicy, finally consumed.
                    // 📄 UX_Feature_Map_Parity.md §3.2f · UX_Feature_Entity_Symbology.md §3.4.
                    //
                    // ⚠ This interface has always declared IsEntityVisible, and DataDrivenGizmoSystem has
                    // always honoured it (:326, :369) — but THIS system called only IsGloballyEnabled, so a
                    // per-entity policy registered here was stored and silently ignored. That made §3.4's
                    // design ("move CullingState out of the projector key and into the policy")
                    // unimplementable: the policy would have done nothing at all.
                    //
                    // ⭐ Cost: placed AFTER the mask match, so it runs once per MATCHED entity rather than
                    // per rule x entity; and the reference compare short-circuits it for AlwaysVisiblePolicy,
                    // which is every projector's default and what reflection registers.
                    if (!ReferenceEquals(rule.VisibilityPolicy, AlwaysVisiblePolicy.Instance)
                        && !rule.VisibilityPolicy.IsEntityVisible(view, entity))
                        continue;

                    rule.Projector.Draw(view, entity, _drawBuilder);

                    // Check budget after each entity (only if budget is active).
                    if (budgetTicks > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - start >= budgetTicks)
                    {
                        budgetExceeded = true;
                        break;
                    }
                }
            }
        }
    }
}
