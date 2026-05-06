using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ECS system that manages the full lifecycle of entity-bound gizmos registered in a
    /// <see cref="GizmoRegistry"/>. Runs in the <see cref="SystemPhase.PostSimulation"/> phase.
    ///
    /// <para>
    /// For each frame the system:
    /// <list type="number">
    ///   <item>Tears down gizmos whose entities were destroyed (<see cref="DestructionOrder"/>).</item>
    ///   <item>Initialises gizmos for newly constructed entities whose component mask satisfies
    ///         one or more registered rules (<see cref="ConstructionOrder"/>).</item>
    ///   <item>Pre-evaluates the global visibility for every rule once (not once per entity).</item>
    ///   <item>Iterates active gizmos; for each entity that passes the selection predicate and
    ///         visibility policies, calls <see cref="IStatefulGizmo.UpdateAndDraw"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>SelectionState design deviation:</b> <c>Hrot.IG.Components.SelectionState</c> is not
    /// reachable from <c>Fdp.Toolkits</c> (no project reference). This system therefore accepts
    /// an optional <c>isSelectedPredicate</c> delegate instead of performing an ECS query with
    /// SelectionState. When the predicate is <c>null</c>, all active gizmos are always drawn
    /// (equivalent to a global-force mode). Callers in Hrot assemblies should supply a predicate
    /// that checks <c>view.HasComponent&lt;SelectionState&gt;(entity) &amp;&amp;
    /// view.GetComponentRO&lt;SelectionState&gt;(entity).IsSelected</c>.
    /// See BATCH-02-REPORT.md for details.
    /// </para>
    ///
    /// <para>
    /// <b>GlobalDebugSettings integration deferred to GZ015 (Phase 6).</b>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class DataDrivenGizmoSystem : IEcsModuleSystem
    {
        private readonly GizmoRegistry _registry;
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Func<ISimulationView, Entity, bool>? _isSelectedPredicate;
        private readonly Dictionary<Entity, List<CompiledGizmoInstance>> _activeGizmos;
        private readonly bool[] _globalVisibilityCache;

        // ---- Private per-instance gizmo record ------------------------------------

        private struct CompiledGizmoInstance
        {
            public IStatefulGizmo Instance;
            public IGizmoDefinition Definition;
            public int RuleIndex;
        }

        // ---- Construction ----------------------------------------------------------

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="registry">The rule registry. All rules must be registered before this
        /// constructor is called so that the global-visibility cache is sized correctly.</param>
        /// <param name="drawBuilder">Target draw builder for all active gizmos.</param>
        /// <param name="isSelectedPredicate">
        /// Per-entity selection gate. When <c>null</c>, all active gizmos whose visibility
        /// policy allows it are drawn unconditionally. When non-null, <see cref="UpdateAndDraw"/>
        /// is only called for entities for which the predicate returns <c>true</c>.
        /// </param>
        public DataDrivenGizmoSystem(
            GizmoRegistry registry,
            IDebugDrawBuilder drawBuilder,
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null)
        {
            _registry             = registry    ?? throw new ArgumentNullException(nameof(registry));
            _drawBuilder          = drawBuilder ?? throw new ArgumentNullException(nameof(drawBuilder));
            _isSelectedPredicate  = isSelectedPredicate;
            _activeGizmos         = new Dictionary<Entity, List<CompiledGizmoInstance>>();
            _globalVisibilityCache = new bool[registry.Rules.Count];
        }

        // ---- IEcsModuleSystem -----------------------------------------------------

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DataDrivenGizmoSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only view ({view.GetType().Name}).");

            // Advance the persistence clock and clear the previous frame's transient primitives.
            _drawBuilder.EndFrame(deltaTime);

            // 1. Teardown destroyed entities first (so same-frame replace works correctly).
            var destructions = view.ReadEvents<DestructionOrder>();
            foreach (ref readonly var evt in destructions)
                TeardownEntity(evt.Entity);

            // 2. Initialise gizmos for newly constructed entities.
            var constructions = view.ReadEvents<ConstructionOrder>();
            foreach (ref readonly var evt in constructions)
            {
                ref var header = ref repo.GetHeader(evt.Entity.Index);
                var rules = _registry.Rules;
                for (int r = 0; r < rules.Count; r++)
                {
                    var rule = rules[r];
                    if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                        continue;

                    var instance = rule.Definition.CreateInstance();
                    instance.OnInitialize(view, evt.Entity);

                    if (!_activeGizmos.TryGetValue(evt.Entity, out var list))
                    {
                        list = new List<CompiledGizmoInstance>();
                        _activeGizmos[evt.Entity] = list;
                    }

                    list.Add(new CompiledGizmoInstance
                    {
                        Instance   = instance,
                        Definition = rule.Definition,
                        RuleIndex  = rule.RuleIndex,
                    });
                }
            }

            // 3. Pre-evaluate global visibility for all rules — once per frame, not per entity.
            var allRules = _registry.Rules;
            int cacheSize = _globalVisibilityCache.Length;
            for (int i = 0; i < allRules.Count && i < cacheSize; i++)
                _globalVisibilityCache[i] = allRules[i].Definition.VisibilityPolicy.IsGloballyEnabled(view);

            // 4. Drive active gizmos.
            bool alwaysDraw = _isSelectedPredicate == null;
            foreach (var kvp in _activeGizmos)
            {
                Entity entity = kvp.Key;
                if (!view.IsAlive(entity))
                    continue;

                bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
                if (!selected)
                    continue;

                var instances = kvp.Value;
                for (int i = 0; i < instances.Count; i++)
                {
                    var gi = instances[i];
                    if (gi.RuleIndex < cacheSize && !_globalVisibilityCache[gi.RuleIndex])
                        continue;
                    if (!gi.Definition.VisibilityPolicy.IsEntityVisible(view, entity))
                        continue;

                    gi.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                }
            }
        }

        // ---- Helpers ---------------------------------------------------------------

        private void TeardownEntity(Entity entity)
        {
            if (!_activeGizmos.TryGetValue(entity, out var list))
                return;

            foreach (var gi in list)
                gi.Instance.OnTeardown();

            _activeGizmos.Remove(entity);
        }
    }
}
