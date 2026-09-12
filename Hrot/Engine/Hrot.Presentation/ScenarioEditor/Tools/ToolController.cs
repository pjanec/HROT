using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> <c>A1</c> — the single tool arbiter.</b> 📄 The design, the two diagrams and
    /// the reproduction: <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.
    ///
    /// <para>⭐⭐ <b>THE FIX IS ONE LINE OF POLICY:</b> before arming a modal tool, clear any modal holding
    /// focus in the OTHER arbiter. That is <see cref="CancelOtherArbiter"/>, and it is the whole of the
    /// correctness change.</para>
    ///
    /// <para>🔒 <b>It DELEGATES rather than reimplements.</b> Both arbiters already expose
    /// <c>CancelInteractiveTools()</c> (<c>GlobalGizmoManager.cs:96</c> · <c>DataDrivenGizmoSystem.cs:124</c>),
    /// already driven from one place by <c>GizmoExecutionController.cs:48-49</c>, and they already encode
    /// <i>"cancel the interactive, spare the permanent"</i> — ⭐ which IS <c>Q27</c>'s modal/modeless split,
    /// written in code before the ruling existed. ⛔ Inventing a third teardown path would be the exact
    /// disease <c>UXI-07</c> exists to cure (seam law).</para>
    ///
    /// <para>⚠⚠ <b>The arbiters are resolved through delegates, not captured.</b> Same reason
    /// <c>ScenarioEditorModule.InteractionDeps</c> uses resolvers: hosts build these AFTER
    /// <c>Kernel.Initialize()</c> and null them on teardown, and CGF does not build them at all when
    /// headless. A captured instance would be permanently null on those paths.</para>
    /// </summary>
    public sealed class ToolController : IToolController
    {
        private readonly Dictionary<string, (ToolDescriptor Descriptor, ToolActivation Activate)> _tools =
            new(StringComparer.Ordinal);

        private readonly Func<GlobalGizmoManager?>    _global;
        private readonly Func<DataDrivenGizmoSystem?> _dataDriven;
        private readonly Action<string>?              _reportUnserviceable;

        private readonly List<ArmedTool>         _modalStack    = new();
        private readonly HashSet<ToolDescriptor> _activeModeless = new();

        /// <param name="global">Resolver for the non-entity arbiter. May return null on a host without one.</param>
        /// <param name="dataDriven">Resolver for the entity-scoped arbiter. May return null.</param>
        /// <param name="reportUnserviceable">
        /// 🔒 Same contract as <c>ToolActivationDrainSystem.reportUnserviceable</c>: it carries the NAME and
        /// the REASON, because <i>"nothing happened"</i> is indistinguishable from <i>"not implemented"</i>
        /// to the operator holding the mouse. Defaults to the FDP log.
        /// </param>
        public ToolController(
            Func<GlobalGizmoManager?>    global,
            Func<DataDrivenGizmoSystem?> dataDriven,
            Action<string>?              reportUnserviceable = null)
        {
            _global              = global     ?? throw new ArgumentNullException(nameof(global));
            _dataDriven          = dataDriven ?? throw new ArgumentNullException(nameof(dataDriven));
            _reportUnserviceable = reportUnserviceable;
        }

        /// <inheritdoc/>
        public ToolDescriptor? ActiveModal => _modalStack.Count > 0 ? _modalStack[^1].Tool : null;

        /// <inheritdoc/>
        public Entity ActiveModalTarget => _modalStack.Count > 0 ? _modalStack[^1].Target : Entity.Null;

        /// <inheritdoc/>
        public IReadOnlyList<ArmedTool> ModalStack => _modalStack;

        /// <inheritdoc/>
        public IReadOnlyCollection<ToolDescriptor> ActiveModeless => _activeModeless;

        /// <inheritdoc/>
        public event Action<ToolDescriptor?>? ActiveModalChanged;

        /// <summary>
        /// Register a tool and how to arm it. ⛔ Duplicate ids throw rather than silently replacing — the
        /// <c>G4</c> lesson: a registry that accepts two producers for one slot is a race, not a feature.
        /// </summary>
        public void Register(ToolDescriptor descriptor, ToolActivation activate)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (activate   == null) throw new ArgumentNullException(nameof(activate));
            if (_tools.ContainsKey(descriptor.Id))
                throw new InvalidOperationException($"Tool '{descriptor.Id}' is already registered.");

            _tools[descriptor.Id] = (descriptor, activate);
        }

        /// <summary>True when <paramref name="toolId"/> has been registered on this host.</summary>
        public bool IsRegistered(string toolId) => _tools.ContainsKey(toolId);

        /// <inheritdoc/>
        public bool Activate(string toolId, Entity target = default)
        {
            if (!_tools.TryGetValue(toolId, out var entry))
            {
                // ⭐ Same sentence shape as every other refusal (ToolReport) — an operator should not
                //   have to recognise two phrasings for "the tool you pressed did nothing".
                ToolReport.Unserviceable(_reportUnserviceable, toolId, "it is not registered on this host");
                return false;
            }

            var (descriptor, activate) = entry;

            if (descriptor.Modality == ToolModality.Modeless)
            {
                // Modeless tools coexist and toggle independently — they never touch the modal stack.
                switch (activate(target))
                {
                    case ToolActivationOutcome.Armed:     _activeModeless.Add(descriptor);    return true;
                    case ToolActivationOutcome.Dismissed: _activeModeless.Remove(descriptor); return true;
                    default:                                                                  return false;
                }
            }

            // 🔒 Q27: "Re-activating the current modal tool does NOT cancel it — no-op, or RE-TARGET when a
            //    different target is supplied. Toggle only if ToggleOnReactivate."
            // ⛔⛔ Keyed on (tool, TARGET). Edit on A then Edit on B is a RE-TARGET, not a toggle — 📐 the
            //    drain's toggle reads HasInjectedGizmo(e), which is per-entity (ToggleEntityGizmo:176).
            //    Falling through to the arm path below is exactly what re-targeting means.
            if (ReferenceEquals(ActiveModal, descriptor)
                && ActiveModalTarget == target
                && descriptor.ToggleOnReactivate)
            {
                Cancel();
                return true;
            }

            // ⭐⭐⭐ THE FIX. Clear the modal holding focus in BOTH arbiters before arming a new one.
            //    Cancelling our own arbiter is what "Activate REPLACES the top" already means; cancelling
            //    the OTHER one is what closes the two-arbiter defect.
            CancelActiveModalWithoutNotify();
            CancelOtherArbiter(descriptor.Arbiter);

            switch (activate(target))
            {
                case ToolActivationOutcome.Armed:
                    _modalStack.Add(new ArmedTool(descriptor, target));
                    NotifyActiveModalChanged();
                    return true;

                // ⭐ The tool turned ITSELF off (Edit/Route pressed twice on the same entity). ⛔ NOT an
                //   error, so no report — but nothing is armed, so nothing goes on the stack.
                case ToolActivationOutcome.Dismissed:
                    NotifyActiveModalChanged();
                    return true;

                default:
                    // The host cannot service it. The activation already said WHY through its own report
                    // channel (the drain's Unserviceable); leave NO modal armed rather than a half state.
                    NotifyActiveModalChanged();
                    return false;
            }
        }

        /// <inheritdoc/>
        public IDisposable PushModal(string toolId, Entity target = default)
        {
            if (!_tools.TryGetValue(toolId, out var entry))
            {
                ToolReport.Unserviceable(_reportUnserviceable, toolId, "it is not registered on this host");
                return NullScope.Instance;
            }

            var (descriptor, activate) = entry;

            // ⛔ A modeless tool has nothing to suspend and nothing to come back to — pushing one is a
            //   caller error, not a silent no-op that leaves an un-poppable handle around.
            if (descriptor.Modality == ToolModality.Modeless)
            {
                ToolReport.Unserviceable(_reportUnserviceable, toolId,
                    "it is modeless — use Activate; only modal tools can be pushed");
                return NullScope.Instance;
            }

            // ⭐⭐⭐ RE-PUSHING THE SAME TOOL REPLACES IT — it does NOT stack.
            //
            // 🔴 MEASURED 2026-09-09 by RePushingTheSamePickerRetargetsRatherThanStacking, and it caught a
            //   REAL defect in an already-committed deletion. Every converted host used to keep a private
            //   one-slot field (_activeLocationPickerId, _activeEntityPickerId, _activeGizmoId) purely to
            //   unregister its previous picker before arming a new one. Those were deleted on the premise
            //   that the controller already guarantees one modal at a time — TRUE for Activate, but
            //   PushModal had no such rule, so a second pick would have grown the stack and left the first
            //   picker ALIVE and suspended beneath it, recoverable only by popping twice.
            // ⭐ Stacking two identical interruptions is never meaningful: the operator asked for this
            //   tool, not for two of it. Replacing is the only reading that matches the gesture.
            if (ReferenceEquals(ActiveModal, descriptor))
                PopModalAt(_modalStack.Count);

            // ⭐⭐⭐ THE WHOLE DIFFERENCE FROM Activate, in one line: SUSPEND the current top instead of
            //   cancelling it. 🔒 Q27-F — "suspend = SetFocus(false) WITHOUT the Dispose()".
            //   ⛔ We deliberately do NOT call CancelOtherArbiter: an interruption must leave everything
            //      it interrupted alive, on BOTH arbiters.
            var suspended = SuspendCurrentTop();

            switch (activate(target))
            {
                case ToolActivationOutcome.Armed:
                    _modalStack.Add(new ArmedTool(descriptor, target, suspended));
                    WarnIfStackIsDeep();
                    NotifyActiveModalChanged();
                    return new ModalScope(this, _modalStack.Count);

                default:
                    // ⚠⚠ The push FAILED, so the suspension must be UNDONE — otherwise the tool underneath
                    //   is left alive but unfocused, i.e. visibly armed and silently dead. 🔴 That is the
                    //   dead-toggle shape this programme has already been bitten by twice.
                    ResumeInto(_modalStack.Count > 0 ? _modalStack[^1].Tool.Arbiter : ToolArbiter.None, suspended);
                    NotifyActiveModalChanged();
                    return NullScope.Instance;
            }
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            if (_modalStack.Count == 0) return;
            CancelActiveModalWithoutNotify();
            NotifyActiveModalChanged();
        }

        /// <inheritdoc/>
        public void NotifyToolEnded(string toolId, Entity target = default)
        {
            if (!_tools.TryGetValue(toolId, out var entry)) return;   // never registered ⇒ nothing to pop
            var descriptor = entry.Descriptor;

            // ⭐ TOPMOST match: the same (tool, target) can only be on the stack once (Activate replaces,
            //   PushModal re-targets), but searching downward is what makes the not-top case below reachable.
            int idx = -1;
            for (int i = _modalStack.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_modalStack[i].Tool, descriptor) && _modalStack[i].Target == target)
                {
                    idx = i;
                    break;
                }
            }

            // ⚠ IDEMPOTENT, deliberately — same property as PopModalAt. The arbiter sweeps
            //   (CancelFocused / CancelInteractiveTools) dispose a gizmo without going through its
            //   onRemove, but nothing guarantees that stays true, and a self-removing gizmo may also be
            //   torn down by entity death. ⇒ a second notification for an entry already gone is normal.
            if (idx < 0) return;

            var ended = _modalStack[idx];
            _modalStack.RemoveAt(idx);

            if (idx == _modalStack.Count)
            {
                // It WAS the top. ⛔⛔ NOT CancelFocused, and that is the whole difference from PopModalAt:
                //   the gizmo ENDED ITSELF, so there is nothing of ours left to tear down — and cancelling
                //   "the focus holder" here would reach whatever holds focus NOW, which after a self-removal
                //   is either nothing or somebody else's gizmo.
                // ⭐ The resume still happens: this entry interrupted something and that something is owed
                //   its focus back, exactly as a popped scope would give it.
                ResumeInto(_modalStack.Count > 0 ? _modalStack[^1].Tool.Arbiter : ToolArbiter.None,
                           ended.Suspended);
            }
            // ⛔ else: the entry was SUSPENDED underneath an interruption and its gizmo went away anyway
            //   (entity death — a suspended gizmo receives no input, so it cannot self-remove). Removing the
            //   entry is all that is owed: the interruption above still holds focus, and when IT pops,
            //   ResumeInto hands back this now-dead gizmo — which DataDrivenGizmoSystem.ResumeFocus
            //   documents as a no-op "when that gizmo is no longer injected" (:132). ⇒ no chain repair.

            NotifyActiveModalChanged();
        }

        // ── internals ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>The correctness change, in one method.</b> A modal tool arming in one arbiter must not
        /// leave a modal holding focus in the other.
        ///
        /// <para>⚠ <c>CancelInteractiveTools()</c> is deliberately the right granularity: it cancels the
        /// focus holder and every on-demand gizmo, and SPARES permanent/modeless ones
        /// (<c>GlobalGizmoManager.cs:94</c> says so in its own words). ⇒ a <c>LayerControlGizmo</c> or a
        /// drag handle survives a tool switch, which is what <c>Q27</c> ruling C requires.</para>
        ///
        /// <para>⛔ <c>ToolArbiter.None</c> (the <c>Select</c> null modal tool) clears BOTH — selecting is
        /// how you say "no tool", and that is exactly ruling C's <i>"whatever takes focus displaces it"</i>.</para>
        /// </summary>
        private void CancelOtherArbiter(ToolArbiter owner)
        {
            if (owner != ToolArbiter.EntityScoped) _dataDriven()?.CancelInteractiveTools();
            if (owner != ToolArbiter.Global)       _global()?.CancelInteractiveTools();
        }

        /// <summary>
        /// Tear the current modal down through ITS OWN arbiter. ⚠ Separate from
        /// <see cref="CancelOtherArbiter"/> so that <see cref="Activate"/> reads as the two distinct
        /// obligations it has: replace my own top, and clear the other side.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>It unwinds the WHOLE stack, not just the top</b> — and that is a deliberate reading of
        /// ruling C once <see cref="PushModal"/> exists. 🔒 <c>Activate</c> is <i>"a deliberate switch"</i>:
        /// the operator chose a different tool, so nothing that was interrupted is still wanted.
        /// ⛔ Popping only the top would leave suspended tools underneath that <b>nothing can ever
        /// resume</b> — their scope handle pops by depth and that depth is now occupied by the new tool.
        /// ⚠ Each level is cancelled through ITS OWN arbiter; ⛔ none is resumed.
        /// </remarks>
        private void CancelActiveModalWithoutNotify()
        {
            while (_modalStack.Count > 0)
            {
                var top = _modalStack[^1].Tool;
                _modalStack.RemoveAt(_modalStack.Count - 1);

                switch (top.Arbiter)
                {
                    case ToolArbiter.EntityScoped: _dataDriven()?.CancelInteractiveTools(); break;
                    case ToolArbiter.Global:       _global()?.CancelInteractiveTools();     break;
                    case ToolArbiter.None:         break;   // the null modal tool owns no gizmo
                }
            }
        }

        private void NotifyActiveModalChanged() => ActiveModalChanged?.Invoke(ActiveModal);

        // ── PushModal internals ───────────────────────────────────────────────────

        /// <summary>
        /// Suspend whatever currently holds focus, on the arbiter that owns the current top.
        /// ⭐ Returns the suspended gizmo, or <see langword="null"/> when nothing was armed.
        /// </summary>
        private Fdp.Toolkit.Diagnostics.Gizmos.IEntityStatefulGizmo? SuspendCurrentTop()
        {
            if (_modalStack.Count == 0) return null;

            return _modalStack[^1].Tool.Arbiter switch
            {
                ToolArbiter.EntityScoped => _dataDriven()?.SuspendFocus(),
                ToolArbiter.Global       => _global()?.SuspendFocus(),
                _                        => null,   // the null modal tool owns no gizmo
            };
        }

        /// <summary>Give focus back to <paramref name="gizmo"/> on <paramref name="owner"/>'s arbiter.</summary>
        private void ResumeInto(ToolArbiter owner, Fdp.Toolkit.Diagnostics.Gizmos.IEntityStatefulGizmo? gizmo)
        {
            if (gizmo == null) return;

            switch (owner)
            {
                case ToolArbiter.EntityScoped: _dataDriven()?.ResumeFocus(gizmo); break;
                case ToolArbiter.Global:       _global()?.ResumeFocus(gizmo);     break;
                case ToolArbiter.None:         break;
            }
        }

        /// <summary>
        /// Pop the entry at <paramref name="depth"/> — tear IT down, then resume what it suspended.
        /// ⚠ Idempotent and order-tolerant: a scope disposed after its entry already went away
        /// (<see cref="Cancel"/>, or an <see cref="Activate"/> that unwound the stack) does nothing.
        /// </summary>
        private void PopModalAt(int depth)
        {
            if (_modalStack.Count != depth) return;   // already popped, or something deeper is still up

            var popped = _modalStack[^1];
            _modalStack.RemoveAt(_modalStack.Count - 1);

            // ⛔⛔ CancelFocused, NOT CancelInteractiveTools — measured 2026-09-09: the sweep clears EVERY
            //    exclusive-focus gizmo on the arbiter, so it destroyed the tool this push had just
            //    SUSPENDED and the resume below had nothing left to resume. A pop ends ONE interruption.
            switch (popped.Tool.Arbiter)
            {
                case ToolArbiter.EntityScoped: _dataDriven()?.CancelFocused(); break;
                case ToolArbiter.Global:       _global()?.CancelFocused();     break;
                case ToolArbiter.None:         break;
            }

            // ⭐⭐ The RESUME — the half that makes this a stack rather than a cancel. The entry BENEATH
            //    names the arbiter, because that is where its gizmo lives.
            ResumeInto(_modalStack.Count > 0 ? _modalStack[^1].Tool.Arbiter : ToolArbiter.None,
                       popped.Suspended);

            NotifyActiveModalChanged();
        }

        /// <summary>
        /// 🔒 <c>Q27-F</c>: <i>"Nothing needs more than 2 today (tool → picker). Lean: no hard limit, but
        /// LOG beyond 3 — an unbounded stack is a leak, not a feature."</i> ⛔ Deliberately not an
        /// exception: refusing the push would break a legitimate deep interaction, which is worse.
        /// </summary>
        private void WarnIfStackIsDeep()
        {
            if (_modalStack.Count <= 3) return;

            FdpLog<ToolController>.Warn(
                "[Tools] modal stack is {0} deep ({1}) — nothing in this design needs more than 2 " +
                "(tool -> picker). A stack that keeps growing is a leak, not a feature (Q27-F).",
                _modalStack.Count,
                string.Join(" > ", _modalStack.Select(a => a.Tool.Id)));
        }

        /// <summary>The handle returned by <see cref="PushModal"/>. Disposing it pops exactly its level.</summary>
        private sealed class ModalScope : IDisposable
        {
            private readonly ToolController _owner;
            private readonly int            _depth;
            private bool                    _disposed;

            internal ModalScope(ToolController owner, int depth)
            {
                _owner = owner;
                _depth = depth;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.PopModalAt(_depth);
            }
        }

        /// <summary>
        /// ⭐ Returned when the push did not happen. ⛔ <see langword="null"/> is not an option — the
        /// caller writes <c>using var _ = tools.PushModal(...)</c>, so a null would throw at the
        /// <c>using</c> and turn a reported refusal into a crash.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            internal static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
