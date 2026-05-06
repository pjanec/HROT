using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
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
        private readonly GizmoUndoStack? _undoStack;

        /// <summary>Max wall-clock budget in ms for step 4. 0 = unlimited.</summary>
        public float MaxGizmoFrameMs { get; set; } = 0f;

        // Time-slice state: ordered entity list and current offset for carry-over.
        private readonly List<Entity> _entityList = new();
        private int _timeSliceOffset = 0;

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
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
            GizmoUndoStack? undoStack = null)
        {
            _registry             = registry    ?? throw new ArgumentNullException(nameof(registry));
            _drawBuilder          = drawBuilder ?? throw new ArgumentNullException(nameof(drawBuilder));
            _isSelectedPredicate  = isSelectedPredicate;
            _activeGizmos         = new Dictionary<Entity, List<CompiledGizmoInstance>>();
            _globalVisibilityCache = new bool[registry.Rules.Count];
            _undoStack            = undoStack;
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
                        _entityList.Add(evt.Entity);
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

            // 4. Drive active gizmos (with optional wall-clock budget).
            bool alwaysDraw = _isSelectedPredicate == null;
            float budget = MaxGizmoFrameMs;

            if (budget <= 0f || _entityList.Count == 0)
            {
                // Unlimited path: iterate all active gizmos normally.
                foreach (var kvp in _activeGizmos)
                {
                    Entity entity = kvp.Key;
                    if (!view.IsAlive(entity)) continue;
                    bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
                    if (!selected) continue;
                    var instances = kvp.Value;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        var gi = instances[i];
                        if (gi.RuleIndex < cacheSize && !_globalVisibilityCache[gi.RuleIndex]) continue;
                        if (!gi.Definition.VisibilityPolicy.IsEntityVisible(view, entity)) continue;
                        gi.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                    }
                }
            }
            else
            {
                // Time-sliced path: resume from _timeSliceOffset, stop when budget exceeded.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int count = _entityList.Count;
                int processed = 0;
                int startOffset = _timeSliceOffset;

                while (processed < count)
                {
                    int idx = (startOffset + processed) % count;
                    processed++;
                    Entity entity = _entityList[idx];

                    if (!view.IsAlive(entity)) continue;
                    if (!_activeGizmos.TryGetValue(entity, out var instances)) continue;

                    bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
                    if (!selected) continue;

                    for (int i = 0; i < instances.Count; i++)
                    {
                        var gi = instances[i];
                        if (gi.RuleIndex < cacheSize && !_globalVisibilityCache[gi.RuleIndex]) continue;
                        if (!gi.Definition.VisibilityPolicy.IsEntityVisible(view, entity)) continue;
                        gi.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                    }

                    // Check budget after each entity.
                    if (sw.Elapsed.TotalMilliseconds >= budget)
                        break;
                }

                // Update offset for next frame: resume where we left off.
                _timeSliceOffset = (startOffset + processed) % count;
            }

            // 5. Process commit events and push undo records to the stack.
            if (_undoStack != null)
            {
                var commits = view.ReadEvents<GizmoInteractionCommitEvent>();
                foreach (ref readonly var commit in commits)
                {
                    var target = commit.Token.Target;
                    if (!_activeGizmos.TryGetValue(target, out var gizmoList)) continue;
                    for (int i = 0; i < gizmoList.Count; i++)
                    {
                        var record = gizmoList[i].Instance.CreateUndoRecord(commit);
                        if (record != null)
                            _undoStack.Push(record);
                    }
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
            _entityList.Remove(entity);
            // Reset offset if it would be out of bounds.
            if (_timeSliceOffset >= _entityList.Count)
                _timeSliceOffset = 0;
        }
    }
}
