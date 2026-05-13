using System;
using System.Collections.Generic;
using System.Linq;
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
    ///         visibility policies, calls <see cref="IEntityStatefulGizmo.UpdateAndDraw"/>.
    ///         UpdateAndDraw is called for ALL active gizmos regardless of focus state.</item>
    ///   <item>Routes typed interaction events to the gizmo that holds exclusive focus.</item>
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

        // Exclusive-focus tracking: the single gizmo that captures all typed input events.
        // null when no gizmo holds focus.
        private IEntityStatefulGizmo? _focusedGizmo;

        // Optional isolated interaction bus. When non-null, interaction events are read from
        // this bus instead of the world bus so that UI noise is quarantined.
        private readonly FdpEventBus? _interactionBus;

        // On-demand gizmos injected externally (e.g. EntityRotatorGizmo activated via context
        // menu). Keyed by entity; always drawn while the entity is alive; not governed by
        // GizmoRegistry rules or the selection predicate.
        private readonly Dictionary<Entity, IEntityStatefulGizmo> _injectedGizmos = new();

        // ---- On-demand gizmo management ------------------------------------------------

        /// <summary>
        /// Registers an on-demand gizmo for <paramref name="entity"/> and grants it exclusive
        /// focus if it requests it. Replaces any previously injected gizmo for the same entity.
        /// Call this when the operator activates an interaction tool (e.g. "Rotate") for a
        /// specific entity from a context menu or inspector panel.
        /// </summary>
        public void ActivateGizmo(Entity entity, IEntityStatefulGizmo gizmo)
        {
            // Tear down any existing injected gizmo for this entity first.
            DeactivateGizmo(entity);

            _injectedGizmos[entity] = gizmo;

            if ((gizmo.RequiresExclusiveFocus || gizmo.WantsRawInput) && _focusedGizmo == null)
            {
                _focusedGizmo = gizmo;
                _focusedGizmo.SetFocus(true);
            }
        }

        /// <summary>
        /// Removes and disposes the on-demand gizmo previously injected for
        /// <paramref name="entity"/>, if any, and releases exclusive focus.
        /// </summary>
        public void DeactivateGizmo(Entity entity)
        {
            if (!_injectedGizmos.TryGetValue(entity, out var gizmo)) return;

            if (_focusedGizmo == gizmo)
            {
                _focusedGizmo.SetFocus(false);
                _focusedGizmo = null;
            }

            gizmo.Dispose();
            _injectedGizmos.Remove(entity);
        }

        /// <summary>
        /// Returns <c>true</c> when an on-demand gizmo is currently injected for
        /// <paramref name="entity"/> via <see cref="ActivateGizmo"/>.
        /// </summary>
        public bool HasInjectedGizmo(Entity entity) => _injectedGizmos.ContainsKey(entity);

        // Synchronously cancels and disposes all injected (on-demand) gizmos.
        // Called by GizmoExecutionController when the last terminal disconnects.
        public void CancelInteractiveTools()
        {
            foreach (var kvp in _injectedGizmos)
            {
                if (kvp.Value == _focusedGizmo)
                {
                    _focusedGizmo.SetFocus(false);
                    _focusedGizmo = null;
                }
                kvp.Value.OnCancel();
                kvp.Value.Dispose();
            }
            _injectedGizmos.Clear();
        }

        // ---- Private per-instance gizmo record ------------------------------------

        private struct CompiledGizmoInstance
        {
            public IEntityStatefulGizmo Instance;
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
        /// policy allows it are drawn unconditionally. When non-null, <see cref="IEntityStatefulGizmo.UpdateAndDraw"/>
        /// is only called for entities for which the predicate returns <c>true</c>.
        /// </param>
        public DataDrivenGizmoSystem(
            GizmoRegistry registry,
            IDebugDrawBuilder drawBuilder,
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null,
            GizmoUndoStack? undoStack = null,
            FdpEventBus? interactionBus = null)
        {
            _registry             = registry    ?? throw new ArgumentNullException(nameof(registry));
            _drawBuilder          = drawBuilder ?? throw new ArgumentNullException(nameof(drawBuilder));
            _isSelectedPredicate  = isSelectedPredicate;
            _activeGizmos         = new Dictionary<Entity, List<CompiledGizmoInstance>>();
            _globalVisibilityCache = new bool[registry.Rules.Count];
            _undoStack            = undoStack;
            _interactionBus       = interactionBus;
        }

        // ---- IEcsModuleSystem -----------------------------------------------------

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DataDrivenGizmoSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only view ({view.GetType().Name}).");

            // Note: _drawBuilder.EndFrame(deltaTime) is now the responsibility of the
            // application shell (EditorSubsystem.Update, IgApplication.Update,
            // SimHostApp.OnUpdate). It must be called before kernel.Update() so that
            // the buffer is cleared before backend ECS systems populate it, and before
            // the canvas renders it. Calling it here would wipe primitives emitted
            // by other systems running in the same PostSimulation pass.

            // 1. Teardown destroyed entities first (so same-frame replace works correctly).
            var destructions = view.ReadEvents<DestructionOrder>();
            foreach (ref readonly var evt in destructions)
                TeardownEntity(evt.Entity);

            // 1b. Tear down gizmos whose required-component mask is no longer satisfied.
            // This handles the case where a marker component (e.g. ActiveRotationToolRequest)
            // is removed by the gizmo's own onRemove callback.
            var entitiesToTeardown = new List<(Entity entity, int ruleIndex)>();
            foreach (var kvp in _activeGizmos)
            {
                Entity entity = kvp.Key;
                if (!view.IsAlive(entity)) continue;
                ref var header = ref repo.GetHeader(entity.Index);
                var instances = kvp.Value;
                for (int i = 0; i < instances.Count; i++)
                {
                    var gi = instances[i];
                    // Injected (on-demand) gizmos have RuleIndex == -1; skip them.
                    if (gi.RuleIndex < 0) continue;
                    var rule = _registry.Rules[gi.RuleIndex];
                    if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                        entitiesToTeardown.Add((entity, gi.RuleIndex));
                }
            }
            foreach (var (entity, ruleIndex) in entitiesToTeardown)
                TeardownGizmoByRule(entity, ruleIndex);

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

                    // View and entity are passed at construction — no OnInitialize call needed.
                    var instance = rule.Definition.CreateInstance(view, evt.Entity);

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

            // 2b. Late-activate gizmos for entities that gained components after construction.
            var activations = view.ReadEvents<GizmoComponentActivatedEvent>();
            foreach (ref readonly var evt in activations)
            {
                if (!view.IsAlive(evt.Entity)) continue;
                ref var header = ref repo.GetHeader(evt.Entity.Index);
                var rules = _registry.Rules;
                for (int r = 0; r < rules.Count; r++)
                {
                    var rule = rules[r];
                    if (!BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                        continue;

                    // Skip if a gizmo instance from this rule already exists for this entity.
                    if (_activeGizmos.TryGetValue(evt.Entity, out var existing) &&
                        existing.Any(gi => gi.RuleIndex == rule.RuleIndex))
                        continue;

                    var instance = rule.Definition.CreateInstance(view, evt.Entity);

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

                    // Grant exclusive focus if the gizmo requests it.
                    if ((instance.RequiresExclusiveFocus || instance.WantsRawInput) && _focusedGizmo == null)
                    {
                        _focusedGizmo = instance;
                        _focusedGizmo.SetFocus(true);
                    }
                }
            }


            // 3. Pre-evaluate global visibility for all rules — once per frame, not per entity.
            var allRules = _registry.Rules;
            int cacheSize = _globalVisibilityCache.Length;
            for (int i = 0; i < allRules.Count && i < cacheSize; i++)
                _globalVisibilityCache[i] = allRules[i].Definition.VisibilityPolicy.IsGloballyEnabled(view);

            // 4. Drive active gizmos (with optional wall-clock budget).
            // UpdateAndDraw is called for ALL gizmos regardless of focus state.
            bool alwaysDraw = _isSelectedPredicate == null;
            float budget = MaxGizmoFrameMs;

            if (budget <= 0f || _entityList.Count == 0)
            {
                // Unlimited path: iterate all active gizmos normally.
                var buf = (DebugPrimitiveBuffer)_drawBuilder;
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
                        int mark = buf.Count;
                        gi.Instance.UpdateAndDraw(deltaTime, _drawBuilder);
                        buf.StampGizmoTypeId(mark, gi.Definition.GizmoTypeId);
                        // Emit InputCaptureBinding for the exclusive-focus holder.
                        if (gi.Instance == _focusedGizmo &&
                            (_focusedGizmo.RequiresExclusiveFocus || _focusedGizmo.WantsRawInput))
                        {
                            var binding = DebugPrimitive.MakeInputCaptureBinding(
                                networkId: (long)entity.Index,
                                subElementId: 0,
                                exclusive: _focusedGizmo.RequiresExclusiveFocus,
                                wantsRawInput: _focusedGizmo.WantsRawInput);
                            binding.AnchorGeneration = (ushort)entity.Generation;
                            _drawBuilder.EmitRaw(in binding);
                        }
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
                        int mark = ((DebugPrimitiveBuffer)_drawBuilder).Count;
                        gi.Instance.UpdateAndDraw(deltaTime, _drawBuilder);
                        ((DebugPrimitiveBuffer)_drawBuilder).StampGizmoTypeId(mark, gi.Definition.GizmoTypeId);
                        // Emit InputCaptureBinding for the exclusive-focus holder.
                        if (gi.Instance == _focusedGizmo &&
                            (_focusedGizmo.RequiresExclusiveFocus || _focusedGizmo.WantsRawInput))
                        {
                            var binding = DebugPrimitive.MakeInputCaptureBinding(
                                networkId: (long)entity.Index,
                                subElementId: 0,
                                exclusive: _focusedGizmo.RequiresExclusiveFocus,
                                wantsRawInput: _focusedGizmo.WantsRawInput);
                            binding.AnchorGeneration = (ushort)entity.Generation;
                            _drawBuilder.EmitRaw(in binding);
                        }
                    }

                    // Check budget after each entity.
                    if (sw.Elapsed.TotalMilliseconds >= budget)
                        break;
                }

                // Update offset for next frame: resume where we left off.
                _timeSliceOffset = (startOffset + processed) % count;
            }

            // 4b. Drive injected on-demand gizmos; always drawn while the entity is alive.
            foreach (var kvp in _injectedGizmos)
            {
                if (view.IsAlive(kvp.Key))
                {
                    uint injTypeId = Fnv1a32(kvp.Value.GetType().FullName ?? string.Empty);
                    int mark = ((DebugPrimitiveBuffer)_drawBuilder).Count;
                    kvp.Value.UpdateAndDraw(deltaTime, _drawBuilder);
                    ((DebugPrimitiveBuffer)_drawBuilder).StampGizmoTypeId(mark, injTypeId);
                    // Emit InputCaptureBinding for the exclusive-focus holder.
                    if (kvp.Value == _focusedGizmo &&
                        (_focusedGizmo.RequiresExclusiveFocus || _focusedGizmo.WantsRawInput))
                    {
                        var binding = DebugPrimitive.MakeInputCaptureBinding(
                            networkId: (long)kvp.Key.Index,
                            subElementId: 0,
                            exclusive: _focusedGizmo.RequiresExclusiveFocus,
                            wantsRawInput: _focusedGizmo.WantsRawInput);
                        binding.AnchorGeneration = (ushort)kvp.Key.Generation;
                        _drawBuilder.EmitRaw(in binding);
                    }
                }
            }

            // 5. Route typed interaction events to the appropriate gizmo.
            var uiBus = _interactionBus ?? repo.Bus;
            RouteInteractionEvents(uiBus);

            // 6. Process commit events and push undo records to the stack.
            if (_undoStack != null)
            {
                var commits = uiBus.Read<GizmoInteractionCommitEvent>();
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

        // ---- Interaction event routing -------------------------------------------

        private void RouteInteractionEvents(FdpEventBus bus)
        {
            // Started: find the gizmo on the picked entity, set focus if exclusive.
            var started = bus.Read<GizmoInteractionStartedEvent>();
            foreach (ref readonly var evt in started)
            {
                var gizmo = FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId);
                if (gizmo == null) continue;
                if ((gizmo.RequiresExclusiveFocus || gizmo.WantsRawInput) && _focusedGizmo != gizmo)
                {
                    _focusedGizmo?.SetFocus(false);
                    _focusedGizmo = gizmo;
                    _focusedGizmo.SetFocus(true);
                }
                gizmo.OnInteractionStarted(ToGizmoToken(evt.Token), evt.WorldPos);
            }

            // DragUpdate: route to the focused gizmo (token match).
            var drags = bus.Read<GizmoDragUpdateEvent>();
            foreach (ref readonly var evt in drags)
            {
                var gizmo = _focusedGizmo ?? FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId);
                gizmo?.OnDragUpdate(evt.WorldPos);
            }

            // Commit: route, then clear focus.
            var commits = bus.Read<GizmoInteractionCommitEvent>();
            foreach (ref readonly var evt in commits)
            {
                var gizmo = _focusedGizmo ?? FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId);
                gizmo?.OnCommit(evt.WorldPos);
            }

            // Cancel: route, then clear focus.
            var cancels = bus.Read<GizmoInteractionCancelEvent>();
            foreach (ref readonly var evt in cancels)
            {
                var gizmo = _focusedGizmo ?? FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId);
                gizmo?.OnCancel();
            }

            // MenuAction: route only to the matching gizmo via composite key.
            var menus = bus.Read<GizmoMenuActionEvent>();
            foreach (ref readonly var evt in menus)
            {
                var entity = new Entity((int)evt.AnchorId, 0);
                FindGizmo(entity, evt.GizmoTypeId)?.OnMenuAction(evt.ActionId);

                // Route to injected tools (VertexEditGizmo, RouteWaypointGizmo, ...).
                foreach (var kvp in _injectedGizmos)
                    kvp.Value.OnMenuAction(evt.ActionId);
            }

            // StructUpdate: route to the matching gizmo on the target entity.
            var structUpdates = bus.ReadManaged<GizmoStructUpdateEvent>();
            foreach (var evt in structUpdates)
            {
                var entity = new Entity((int)evt.AnchorId, 0);
                FindGizmo(entity, evt.GizmoTypeId)?.OnStructUpdate(evt.PayloadJson);
            }

            // MouseEvent: only the focused exclusive-focus gizmo receives raw mouse events.
            var mouseEvents = bus.Read<GizmoMouseEvent>();
            foreach (ref readonly var evt in mouseEvents)
            {
                var gizmo = _focusedGizmo ?? FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId);
                gizmo?.OnMouseEvent(evt.Button, evt.IsPressed, evt.WorldPos);
            }

            // KeyEvent: only the focused exclusive-focus gizmo receives key events.
            var keyEvents = bus.Read<GizmoKeyEvent>();
            foreach (ref readonly var evt in keyEvents)
            {
                (_focusedGizmo ?? FindGizmo(evt.Token.Target, evt.Token.GizmoTypeId))?.OnKeyEvent(evt.Key, evt.IsPressed);
            }
        }

        // Converts the ECS-based PickToken to the ECS-free GizmoPickToken used by
        // IGizmoInteractionHandler. Index maps to AnchorId; Generation maps to StreamId.
        private static GizmoPickToken ToGizmoToken(PickToken token) => new GizmoPickToken
        {
            AnchorId     = (long)token.Target.Index,
            SubElementId = token.SubElementId,
            StreamId     = (uint)token.Target.Generation,
            GizmoTypeId  = token.GizmoTypeId,
        };

        /// <summary>
        /// Returns the gizmo instance active on <paramref name="entity"/> that matches the
        /// given <paramref name="gizmoTypeId"/> composite key, or <c>null</c> if none is found.
        /// When <paramref name="gizmoTypeId"/> is 0 (legacy / pre-GZ064 peers), returns the first
        /// registered gizmo as a fallback so existing behaviour is not silently broken.
        /// Injected on-demand gizmos always take priority.
        /// </summary>
        private IEntityStatefulGizmo? FindGizmo(Entity entity, uint gizmoTypeId)
        {
            // Injected (on-demand) gizmos have strict priority over base rules.
            if (_injectedGizmos.TryGetValue(entity, out var injected))
                return injected;

            // Events that carry only an entity index (generation == 0, e.g. StructUpdate,
            // MenuAction) need an index-only lookup because the live entry has a non-zero
            // generation that would fail an exact Entity equality check.
            List<CompiledGizmoInstance>? list;
            if (entity.Generation == 0)
            {
                list = null;
                foreach (var kvp in _activeGizmos)
                {
                    if (kvp.Key.Index == entity.Index)
                    {
                        list = kvp.Value;
                        break;
                    }
                }
                if (list == null || list.Count == 0)
                    return null;
            }
            else
            {
                if (!_activeGizmos.TryGetValue(entity, out list) || list.Count == 0)
                    return null;
            }

            if (gizmoTypeId == 0)
                return list[0].Instance;

            foreach (var gi in list)
            {
                if (gi.Definition.GizmoTypeId == gizmoTypeId)
                    return gi.Instance;
            }
            return null;
        }

        // FNV-1a 32-bit hash of a string -- used to derive GizmoTypeId for injected gizmos
        // that have no IGizmoDefinition. Mirrors GizmoSettingsRegistry.ComputeHash.
        private static uint Fnv1a32(string name)
        {
            uint h = 2166136261u;
            foreach (char c in name)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        // ---- Helpers ---------------------------------------------------------------

        private void TeardownGizmoByRule(Entity entity, int ruleIndex)
        {
            if (!_activeGizmos.TryGetValue(entity, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].RuleIndex != ruleIndex) continue;
                var gizmo = list[i].Instance;
                if (_focusedGizmo == gizmo)
                {
                    _focusedGizmo.SetFocus(false);
                    _focusedGizmo = null;
                }
                gizmo.Dispose();
                list.RemoveAt(i);
            }
            if (list.Count == 0)
            {
                _activeGizmos.Remove(entity);
                _entityList.Remove(entity);
            }
        }

        private void TeardownEntity(Entity entity)
        {
            // Also tear down any injected on-demand gizmo for this entity.
            if (_injectedGizmos.TryGetValue(entity, out var injected))
            {
                if (_focusedGizmo == injected)
                {
                    _focusedGizmo.SetFocus(false);
                    _focusedGizmo = null;
                }
                injected.Dispose();
                _injectedGizmos.Remove(entity);
            }

            if (!_activeGizmos.TryGetValue(entity, out var list))
                return;

            foreach (var gi in list)
            {
                // Clear focus if this entity's gizmo held it.
                if (_focusedGizmo == gi.Instance)
                {
                    _focusedGizmo.SetFocus(false);
                    _focusedGizmo = null;
                }
                gi.Instance.Dispose();
            }

            _activeGizmos.Remove(entity);
            _entityList.Remove(entity);
            // Reset offset if it would be out of bounds.
            if (_timeSliceOffset >= _entityList.Count)
                _timeSliceOffset = 0;
        }
    }
}
