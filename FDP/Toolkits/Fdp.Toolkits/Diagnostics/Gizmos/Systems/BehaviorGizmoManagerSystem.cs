using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ECS system that manages the lifecycle of behavior-bound gizmos.
    /// A behavior-bound gizmo is activated by an
    /// <see cref="AssignBehaviorEvent"/> and torn down by a
    /// <see cref="ClearBehaviorEvent"/> or <see cref="DestructionOrder"/>.
    /// At most one behavior gizmo can be active per entity at any time.
    ///
    /// <para>
    /// <b>SelectionState design deviation:</b> Same as <see cref="DataDrivenGizmoSystem"/>.
    /// An optional <c>isSelectedPredicate</c> delegate is accepted instead of a hard
    /// dependency on <c>Hrot.IG.Components.SelectionState</c>.
    /// </para>
    ///
    /// <para>
    /// <b>GlobalDebugSettings integration deferred to GZ015 (Phase 6).</b>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class BehaviorGizmoManagerSystem : IEcsModuleSystem
    {
        private readonly BehaviorGizmoRegistry _behaviorRegistry;
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Func<ISimulationView, Entity, bool>? _isSelectedPredicate;
        private readonly Dictionary<Entity, IEntityStatefulGizmo> _activeBehaviorGizmos;

        // ---- Construction ----------------------------------------------------------

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="behaviorRegistry">Registry of behavior-gizmo factories.</param>
        /// <param name="drawBuilder">Target draw builder for all active behavior gizmos.</param>
        /// <param name="isSelectedPredicate">
        /// Per-entity selection gate (same semantics as <see cref="DataDrivenGizmoSystem"/>).
        /// When <c>null</c>, all active behavior gizmos are drawn unconditionally.
        /// </param>
        public BehaviorGizmoManagerSystem(
            BehaviorGizmoRegistry behaviorRegistry,
            IDebugDrawBuilder drawBuilder,
            Func<ISimulationView, Entity, bool>? isSelectedPredicate = null)
        {
            _behaviorRegistry     = behaviorRegistry ?? throw new ArgumentNullException(nameof(behaviorRegistry));
            _drawBuilder          = drawBuilder      ?? throw new ArgumentNullException(nameof(drawBuilder));
            _isSelectedPredicate  = isSelectedPredicate;
            _activeBehaviorGizmos = new Dictionary<Entity, IEntityStatefulGizmo>();
        }

        // ---- IEcsModuleSystem -----------------------------------------------------

        public void Execute(ISimulationView view, float deltaTime)
        {
            // 1. Tear down gizmos for destroyed entities.
            var destructions = view.ReadEvents<DestructionOrder>();
            foreach (ref readonly var evt in destructions)
                TeardownEntity(evt.Entity);

            // 2. Tear down gizmos explicitly cleared by behavior systems.
            var clears = view.ReadEvents<ClearBehaviorEvent>();
            foreach (ref readonly var evt in clears)
                TeardownEntity(evt.Entity);

            // 3. Activate new behavior gizmos. AssignBehaviorEvent is a managed class event.
            var assigns = view.ReadManagedEvents<AssignBehaviorEvent>();
            foreach (var evt in assigns)
            {
                if (!_behaviorRegistry.TryGetFactory(evt.BehaviorName, out var factory))
                    continue; // unknown behavior name -- silently ignore

                // Replace any existing gizmo for this entity.
                TeardownEntity(evt.Entity);

                // View and entity are passed at construction -- no OnInitialize call needed.
                var instance = factory.Create(view, evt.Entity);
                _activeBehaviorGizmos[evt.Entity] = instance;
            }

            // 4. Drive active behavior gizmos.
            // UpdateAndDraw is called regardless of focus state.
            bool alwaysDraw = _isSelectedPredicate == null;
            foreach (var kvp in _activeBehaviorGizmos)
            {
                Entity entity = kvp.Key;
                if (!view.IsAlive(entity))
                    continue;

                bool selected = alwaysDraw || _isSelectedPredicate!(view, entity);
                if (!selected)
                    continue;

                kvp.Value.UpdateAndDraw(deltaTime, _drawBuilder);
            }
        }

        // ---- Helpers ---------------------------------------------------------------

        private void TeardownEntity(Entity entity)
        {
            if (!_activeBehaviorGizmos.TryGetValue(entity, out var instance))
                return;

            instance.Dispose();
            _activeBehaviorGizmos.Remove(entity);
        }
    }
}
