using System;
using System.Collections.Generic;
using Fdp.Core;
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
            => throw new NotSupportedException(
                "UXI-07: PushModal (suspend/resume) is designed but not built in this slice. " +
                "See docs/UX/UX_Feature_Tool_Model.md section 4.6. Use Activate for a deliberate switch.");

        /// <inheritdoc/>
        public void Cancel()
        {
            if (_modalStack.Count == 0) return;
            CancelActiveModalWithoutNotify();
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
        private void CancelActiveModalWithoutNotify()
        {
            if (_modalStack.Count == 0) return;

            var top = _modalStack[^1].Tool;
            _modalStack.RemoveAt(_modalStack.Count - 1);

            switch (top.Arbiter)
            {
                case ToolArbiter.EntityScoped: _dataDriven()?.CancelInteractiveTools(); break;
                case ToolArbiter.Global:       _global()?.CancelInteractiveTools();     break;
                case ToolArbiter.None:         break;   // the null modal tool owns no gizmo
            }
        }

        private void NotifyActiveModalChanged() => ActiveModalChanged?.Invoke(ActiveModal);

    }
}
