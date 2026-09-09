using System.Collections.Generic;
using System.Linq;
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
        private readonly IActiveViewProvider? _breakpointManager;

        /// <summary>Number of currently registered gizmos. Used for testing.</summary>
        public int ActiveCount => _activeGizmos.Count;

        /// <param name="drawBuilder">Target draw builder shared with the gizmo layer.</param>
        /// <param name="interactionBus">
        /// Optional isolated interaction bus. When non-null, interaction events are read from
        /// this bus instead of the world bus so that UI noise is quarantined.
        /// </param>
        public GlobalGizmoManager(IDebugDrawBuilder drawBuilder, FdpEventBus? interactionBus = null,
            IActiveViewProvider? breakpointManager = null)
        {
            _drawBuilder       = drawBuilder;
            _interactionBus    = interactionBus;
            _breakpointManager = breakpointManager;
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

        /// <summary>
        /// ⭐⭐⭐ <b>SUSPEND the focus holder — <c>SetFocus(false)</c> WITHOUT the <c>Dispose()</c>.</b>
        /// Returns the suspended gizmo so the caller can resume exactly it, or <c>null</c> when nothing
        /// held focus.
        ///
        /// <para>🔒 <b>This is the "entire missing capability" named by <c>UXI-07</c>/<c>Q27-F</c>:</b>
        /// every teardown path here pairs <c>SetFocus(false)</c> with <c>Dispose()</c>
        /// (<see cref="Unregister"/>, <see cref="CancelInteractiveTools"/>), so a caller that wants to
        /// INTERRUPT a tool rather than switch away from it had no way to say so. ⛔ Without this, a picker
        /// that armed over a half-drawn route destroyed the route.</para>
        ///
        /// <para>⭐⭐ <b>The gizmo STAYS REGISTERED, so it keeps DRAWING</b> — 🔒 the design's lean: <i>"a
        /// half-drawn route that vanishes while you pick a point, then reappears, reads as a bug."</i>
        /// It simply stops holding focus, which frees the slot for the interrupting tool.</para>
        ///
        /// <para>⚠ The SUSPEND STACK is deliberately NOT kept here. The caller
        /// (<c>ToolController</c>) owns ordering and depth, so the two arbiters do not grow two
        /// half-copies of one stack.</para>
        /// </summary>
        public IEntityStatefulGizmo? SuspendFocus()
        {
            var suspended = _focusedGizmo;
            if (suspended == null) return null;

            suspended.SetFocus(false);
            _focusedGizmo = null;
            return suspended;
        }

        /// <summary>
        /// ⭐ Give focus back to a gizmo previously returned by <see cref="SuspendFocus"/>.
        /// ⛔ No-ops when that gizmo is no longer registered — it may have completed or been cancelled
        /// while suspended, and resuming a disposed gizmo would be worse than not resuming.
        /// </summary>
        public void ResumeFocus(IEntityStatefulGizmo? gizmo)
        {
            if (gizmo == null) return;
            if (!_activeGizmos.ContainsValue(gizmo)) return;

            // ⚠ Whatever holds focus now is being displaced by the resume — the caller has already torn
            //   the interrupting tool down, so this only fires if something else grabbed the slot.
            if (_focusedGizmo != null && _focusedGizmo != gizmo)
                _focusedGizmo.SetFocus(false);

            _focusedGizmo = gizmo;
            gizmo.SetFocus(true);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Cancel and dispose ONLY the current focus holder</b> — the precise inverse of
        /// <see cref="SuspendFocus"/>, and what a STACK POP needs.
        ///
        /// <para>🔴 <b>Why this exists, measured <c>2026-09-09</c>:</b> <c>ToolController.PushModal</c>'s
        /// pop first used <see cref="CancelInteractiveTools"/> — which sweeps <b>every</b> exclusive-focus
        /// gizmo on this arbiter — and so it <b>destroyed the very tool the push had just suspended</b>.
        /// ⇒ the resume then had nothing to resume. ⭐ A rail caught it
        /// (<c>PoppingResumesTheToolBeneathAndDisposesTheInterrupter</c>).</para>
        ///
        /// <para>⚠ <b>Sweep vs. pop are different operations</b>: <see cref="CancelInteractiveTools"/> is
        /// <i>"the terminal disconnected, drop everything interactive"</i>; this is <i>"one interruption
        /// ended."</i> ⛔ Using the first for the second is what the rail reddened on.</para>
        /// </summary>
        public void CancelFocused()
        {
            var gizmo = _focusedGizmo;
            if (gizmo == null) return;

            gizmo.OnCancel();
            gizmo.SetFocus(false);
            _focusedGizmo = null;

            long? key = null;
            foreach (var kvp in _activeGizmos)
            {
                if (kvp.Value == gizmo) { key = kvp.Key; break; }
            }
            if (key.HasValue) _activeGizmos.Remove(key.Value);

            gizmo.Dispose();
        }

        // Synchronously disposes all on-demand gizmos and releases the focused gizmo.
        // Called by GizmoExecutionController when the last terminal disconnects.
        // Permanent gizmos (RequiresExclusiveFocus == false AND WantsRawInput == false)
        // such as LayerControlGizmo are left intact.
        public void CancelInteractiveTools()
        {
            if (_focusedGizmo != null)
            {
                _focusedGizmo.OnCancel();
                _focusedGizmo.SetFocus(false);
                // Remove from _activeGizmos before Dispose so the second sweep below skips it.
                var focusedKey = _activeGizmos
                    .Where(kvp => kvp.Value == _focusedGizmo)
                    .Select(k => (long?)k.Key)
                    .FirstOrDefault();
                if (focusedKey.HasValue)
                    _activeGizmos.Remove(focusedKey.Value);
                _focusedGizmo.Dispose();
                _focusedGizmo = null;
            }
            var onDemandKeys = _activeGizmos
                .Where(kvp => kvp.Value.RequiresExclusiveFocus || kvp.Value.WantsRawInput)
                .Select(k => k.Key).ToList();
            foreach (var key in onDemandKeys)
            {
                _activeGizmos[key].Dispose();
                _activeGizmos.Remove(key);
            }
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Step 1: UpdateAndDraw each gizmo; emit InputCaptureBinding for focus holder.
            var buf = (DebugPrimitiveBuffer)_drawBuilder;
            ISimulationView activeView = _breakpointManager?.ActiveView ?? view;
            foreach (var kvp in _activeGizmos)
            {
                uint typeId = Fnv1a32(kvp.Value.GetType().FullName ?? string.Empty);
                int mark = buf.Count;
                kvp.Value.UpdateAndDraw(activeView, deltaTime, _drawBuilder);
                buf.StampGizmoTypeId(mark, typeId);

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

        // FNV-1a 32-bit hash used to derive GizmoTypeId for stamping purposes.
        // Mirrors GizmoSettingsRegistry.ComputeHash.
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
    }
}
