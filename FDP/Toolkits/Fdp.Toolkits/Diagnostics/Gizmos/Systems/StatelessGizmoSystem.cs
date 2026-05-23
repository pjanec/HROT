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
    public sealed class StatelessGizmoSystem : IEcsModuleSystem
    {
        private readonly StatelessGizmoRegistry _registry;
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Func<ISimulationView, Entity, bool>? _isSelectedPredicate;
        private readonly bool[] _globalVisibilityCache;
        private readonly bool[] _globalRulesVisibilityCache;

        /// <summary>Max wall-clock budget in ms for entity iteration. 0 = unlimited.</summary>
        public float MaxGizmoFrameMs { get; set; } = 0f;

        /// <summary>
        /// Creates the system. All projectors must be registered in
        /// <paramref name="registry"/> before this constructor is called so that the
        /// global-visibility cache is sized correctly.
        /// </summary>
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

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(StatelessGizmoSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only view ({view.GetType().Name}).");

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

            var sw = (budget > 0f) ? System.Diagnostics.Stopwatch.StartNew() : null;
            bool budgetExceeded = false;

            for (int r = 0; r < ruleCount; r++)
            {
                // Skip rules whose global visibility policy rejects them.
                if (r < _globalVisibilityCache.Length && !_globalVisibilityCache[r])
                    continue;

                if (budgetExceeded) break;

                var rule = rules[r];

                for (int i = 0; i <= maxIndex; i++)
                {
                    ref readonly var metaSG = ref entityIndex.GetMetadata(i);
                    if (!metaSG.IsActive) continue;
                    ref var compSG = ref entityIndex.GetComponentMask(i);
                    if (!BitMask512.HasAll(compSG, rule.RequiredMask)) continue;

                    var entity = new Entity(i, metaSG.Generation);

                    if (!alwaysDraw && !_isSelectedPredicate!(view, entity))
                        continue;

                    rule.Projector.Draw(view, entity, _drawBuilder);

                    // Check budget after each entity (only if budget is active).
                    if (sw != null && sw.Elapsed.TotalMilliseconds >= budget)
                    {
                        budgetExceeded = true;
                        break;
                    }
                }
            }
        }
    }
}
