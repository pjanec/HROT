using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// Manages non-entity-bound <see cref="IEntityStatefulGizmo"/> instances (e.g.
    /// placement gizmos, picker gizmos). Runs in the PostSimulation phase alongside
    /// <see cref="DataDrivenGizmoSystem"/>.
    ///
    /// <para>Each frame the system calls <see cref="IEntityStatefulGizmo.UpdateAndDraw"/>
    /// for every registered gizmo, emits <see cref="DebugPrimitive.MakeInputCaptureBinding"/>
    /// for the exclusive-focus holder, and routes typed interaction events from the ECS bus
    /// to the focused gizmo.</para>
    ///
    /// <para>Use <see cref="NewId"/> to generate a stable key, then <see cref="Register"/> /
    /// <see cref="Unregister"/> to manage the gizmo lifecycle. <see cref="Unregister"/> is
    /// idempotent: calling it for an already-removed id is a safe no-op.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class GlobalGizmoManager : IEcsModuleSystem
    {
        private static long _nextId = 0;

        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly Dictionary<long, IEntityStatefulGizmo> _activeGizmos = new();
        private IEntityStatefulGizmo? _focusedGizmo;
        private readonly FdpEventBus? _interactionBus;

        /// <summary>Number of currently registered gizmos. Used for testing.</summary>
        public int ActiveCount => _activeGizmos.Count;

        /// <param name="drawBuilder">Target draw builder shared with the gizmo layer.</param>
        /// <param name="interactionBus">
        /// Optional isolated interaction bus. When non-null, interaction events are read from
        /// this bus instead of the world bus so that UI noise is quarantined.
        /// </param>
        public GlobalGizmoManager(IDebugDrawBuilder drawBuilder, FdpEventBus? interactionBus = null)
        {
            _drawBuilder    = drawBuilder;
            _interactionBus = interactionBus;
        }

        /// <summary>Generates a unique stable id for use with <see cref="Register"/>.</summary>
        public static long NewId() => Interlocked.Increment(ref _nextId);

        /// <summary>
        /// Registers a gizmo with the given stable id. If the gizmo requires exclusive
        /// focus and no other gizmo currently holds it, focus is granted immediately.
        /// Replaces any previously registered gizmo with the same id.
        /// </summary>
        public void Register(long id, IEntityStatefulGizmo gizmo)
        {
            // Unregister any previous gizmo under the same id first.
            Unregister(id);

            _activeGizmos[id] = gizmo;

            if ((gizmo.RequiresExclusiveFocus || gizmo.WantsRawInput) && _focusedGizmo == null)
            {
                _focusedGizmo = gizmo;
                gizmo.SetFocus(true);
            }
        }

        /// <summary>
        /// Removes and disposes the gizmo with the given id.
        /// Releases exclusive focus if this gizmo held it.
        /// Safe to call when the id is not registered (no-op).
        /// </summary>
        public void Unregister(long id)
        {
            if (!_activeGizmos.Remove(id, out var gizmo))
                return;

            if (_focusedGizmo == gizmo)
            {
                gizmo.SetFocus(false);
                _focusedGizmo = null;
            }

            gizmo.Dispose();
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Step 1: UpdateAndDraw each gizmo; emit InputCaptureBinding for focus holder.
            foreach (var kvp in _activeGizmos)
            {
                kvp.Value.UpdateAndDraw(deltaTime, _drawBuilder);

                if (kvp.Value == _focusedGizmo &&
                    (_focusedGizmo.RequiresExclusiveFocus || _focusedGizmo.WantsRawInput))
                {
                    var binding = DebugPrimitive.MakeInputCaptureBinding(
                        networkId:    kvp.Key,
                        subElementId: 0,
                        exclusive:    _focusedGizmo.RequiresExclusiveFocus,
                        wantsRawInput: _focusedGizmo.WantsRawInput);
                    _drawBuilder.EmitRaw(in binding);
                }
            }

            // Step 2: Route interaction events to the focused gizmo.
            if (_focusedGizmo == null)
                return;

            var focused = _focusedGizmo;

            var bus = _interactionBus ?? ((EntityRepository)view).Bus;

            var drags = bus.Read<GizmoDragUpdateEvent>();
            foreach (ref readonly var evt in drags)
                focused.OnDragUpdate(evt.WorldPos);

            var mouseEvents = bus.Read<GizmoMouseEvent>();
            foreach (ref readonly var evt in mouseEvents)
                focused.OnMouseEvent(evt.Button, evt.IsPressed, evt.WorldPos);

            var keyEvents = bus.Read<GizmoKeyEvent>();
            foreach (ref readonly var evt in keyEvents)
                focused.OnKeyEvent(evt.Key, evt.IsPressed);

            // Route StructUpdate events by AnchorId so the gizmo receives JSON mutations
            // committed via its StructInspector panel on the terminal.
            var structUpdates = bus.ReadManaged<GizmoStructUpdateEvent>();
            foreach (var evt in structUpdates)
            {
                if (_activeGizmos.TryGetValue(evt.AnchorId, out var target))
                    target.OnStructUpdate(evt.PayloadJson);
            }
        }
    }
}
