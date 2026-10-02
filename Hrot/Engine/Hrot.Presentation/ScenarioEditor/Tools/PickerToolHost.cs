using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 4b — the ONE implementation of the picker-modal protocol.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.12.
    ///
    /// <para>🔴 <b>What it replaces.</b> 📐 Measured <c>2026-09-09</c>: the same twelve-line body —
    /// <i>make a <see cref="TaskCompletionSource{T}"/> · mint an id · build a picker gizmo whose
    /// <c>onRemove</c> unregisters that id · register a cancellation hook · <c>Register</c> straight on
    /// <see cref="GlobalGizmoManager"/></i> — was written <b>six times</b> across
    /// <c>CanvasMapPickAdapter</c> and <c>EditorMapPickAdapter</c> (three picks each), and two more sites
    /// (<c>IgApplication</c>, <c>ReplayBrowserSubsystem</c>) hand-rolled the same thing with a private
    /// one-slot arbiter instead of a task — ✅ all four are converted as of <c>2026-09-09</c>. 🔒 Ruling 9 — one concept, one implementation.</para>
    ///
    /// <para>⭐⭐ <b>And every one of those copies was §4.8's BYPASS:</b> the gizmo took exclusive focus
    /// while <see cref="ToolController"/> still believed some other tool held it.</para>
    ///
    /// <para>⭐⭐⭐ <b>Why <c>PushModal</c> and not <c>Activate</c> — the whole reason 4b waited.</b> A pick
    /// INTERRUPTS: the operator is half-way through drawing a route, picks a point, and must get the
    /// route back. <c>Activate</c> cancels and disposes the tool underneath; <c>PushModal</c> suspends it
    /// and the pop resumes it (<c>Q27-F</c>).</para>
    ///
    /// <para>⚠ <b>One picker at a time per tool id.</b> The pending arm is keyed by tool id, so two
    /// concurrent <c>PickEntityAsync</c> calls would have the second replace the first's arm. ⭐ That is
    /// not a regression — before this class, two concurrent picks registered two live picker gizmos and
    /// both received input, which is worse. ⛔ Genuine concurrent picking is not a use case anything in
    /// this repo has; if it becomes one, the fix is a per-invocation tool id, not a queue.</para>
    /// </summary>
    public sealed class PickerToolHost
    {
        private readonly Func<ToolController?>     _tools;
        private readonly Func<GlobalGizmoManager?> _global;
        private readonly Action<string>?           _report;

        /// <summary>
        /// ⚠⚠ The controller this host has already registered its picker tools on.
        /// 🔴 <b>Registration MUST be lazy, and that is measured, not defensive:</b>
        /// <c>IgApplication.cs:513</c> builds its pick adapter <b>before</b> <c>:816</c> assigns the tool
        /// controller from the pack. ⇒ a constructor-time registration would silently register on
        /// <see langword="null"/> and every pick on IG would bypass the arbiter — exactly the
        /// silent-default shape this programme keeps finding. ⭐ Instead the host registers on first use,
        /// once the resolver actually yields a controller.
        /// </summary>
        private ToolController? _registeredOn;

        /// <summary>
        /// The arm body for each picker tool, replaced per invocation. ⚠⚠ It exists because
        /// <c>ToolActivation</c> takes only an <c>Entity</c> — the gizmo a pick needs is built from the
        /// caller's callbacks and cannot be reconstructed inside a registration-time delegate. ⭐ Same
        /// shape the 4a adapters use for their per-invocation parameters.
        /// </summary>
        private readonly Dictionary<string, Func<ToolActivationOutcome>> _pendingArm =
            new(StringComparer.Ordinal);

        /// <param name="tools">
        /// 🔒 The host's arbiter. ⛔ Optional so an unconverted host still picks — but then the pick
        /// arms WITHOUT suspending anything and says so, rather than silently reintroducing the bypass.
        /// </param>
        /// <param name="global">
        /// ⚠⚠ A RESOLVER, not an instance: hosts build the arbiter after <c>Kernel.Initialize()</c> and
        /// null it on teardown — the same reason every member of <c>InteractionDeps</c> is a resolver.
        /// </param>
        public PickerToolHost(
            Func<ToolController?>     tools,
            Func<GlobalGizmoManager?> global,
            Action<string>?           report = null)
        {
            _tools  = tools  ?? throw new ArgumentNullException(nameof(tools));
            _global = global ?? throw new ArgumentNullException(nameof(global));
            _report = report;

            // ⛔ Before any pick is requested a picker tool has nothing to arm — it REPORTS rather than
            //   pretending (ruling 49: registered-and-explained beats absent).
            foreach (var id in new[] { ScenarioToolIds.PickLocation,
                                       ScenarioToolIds.PickEntity,
                                       ScenarioToolIds.PickArea,
                                       ScenarioToolIds.PickBounds })
            {
                var captured = id;
                _pendingArm[captured] = () =>
                {
                    ToolReport.Unserviceable(_report, captured, "no pick is in progress");
                    return ToolActivationOutcome.Unserviceable;
                };
            }
        }

        /// <summary>
        /// Register the picker tools the first time a controller is actually available.
        /// ⭐ Idempotent, and re-registers if the host swapped controllers (teardown → rebuild).
        /// ⛔ Registration happens ONCE per controller — the duplicate-id guard stays strict (G4).
        /// </summary>
        private ToolController? EnsureRegistered()
        {
            var tools = _tools();
            if (tools == null || ReferenceEquals(tools, _registeredOn)) return tools;

            foreach (var pair in new[]
                     {
                         (ScenarioToolIds.PickLocation, "Pick Location"),
                         (ScenarioToolIds.PickEntity,   "Pick Entity"),
                         (ScenarioToolIds.PickArea,     "Pick Area"),
                         (ScenarioToolIds.PickBounds,   "Pick Bounds"),
                     })
            {
                var id = pair.Item1;
                if (tools.IsRegistered(id)) continue;
                tools.Register(
                    new ToolDescriptor(id, pair.Item2, ToolModality.Modal, ToolArbiter.Global),
                    _ => _pendingArm[id]());
            }

            _registeredOn = tools;
            return tools;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Run one pick as an INTERRUPTION.</b> Pushes <paramref name="toolId"/> over whatever is
        /// armed, registers the gizmo through the arbiter, and pops — resuming the suspended tool — as
        /// soon as the pick completes, cancels or faults.
        /// </summary>
        /// <param name="createGizmo">
        /// Builds the picker. ⭐ It is handed the <see cref="TaskCompletionSource{T}"/> to complete and a
        /// <c>remove</c> callback to pass as the gizmo's <c>onRemove</c> — ⛔ callers must not capture a
        /// gizmo id themselves; minting and unregistering it is this class's job.
        /// </param>
        public Task<T> RunPickAsync<T>(
            string                                                    toolId,
            Func<TaskCompletionSource<T>, Action, IEntityStatefulGizmo> createGizmo,
            CancellationToken                                          ct = default)
        {
            if (createGizmo == null) throw new ArgumentNullException(nameof(createGizmo));

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            var manager = _global();
            if (manager == null)
            {
                // ⛔ Never a silent hang: a caller awaiting a pick on a host with no arbiter would wait
                //   forever. ⭐ Cancel it and say why.
                ToolReport.Unserviceable(_report, toolId, "this host composes no global gizmo manager");
                tcs.TrySetCanceled();
                return tcs.Task;
            }

            long id = GlobalGizmoManager.NewId();

            // ⚠⚠ Assigned below; captured here so Remove can pop. 🔴 The POP MUST HAPPEN WHEN THE GIZMO
            //    GOES AWAY, not on a task continuation — measured 2026-09-09, see the rail note below.
            IDisposable? scopeRef = null;

            void Remove()
            {
                _global()?.Unregister(id);
                // ⭐ Idempotent (ModalScope.Dispose guards, and PopModalAt re-checks depth), so it is safe
                //   that Unregister→Dispose→onRemove can re-enter this.
                scopeRef?.Dispose();
            }

            var gizmo = createGizmo(tcs, Remove);

            _pendingArm[toolId] = () =>
            {
                var g = _global();
                if (g == null) return ToolActivationOutcome.Unserviceable;
                g.Register(id, gizmo);
                return ToolActivationOutcome.Armed;
            };

            IDisposable scope;
            var tools = EnsureRegistered();
            if (tools != null)
            {
                scope = tools.PushModal(toolId);
            }
            else
            {
                // ⚠ Unconverted host: arm directly, exactly as before this class existed, and SAY SO.
                //   🔒 R-137 — a unification may not cost a capability; ⛔ but silence would hide the
                //   bypass that step 4b exists to remove.
                ToolReport.Say(_report,
                    $"pick '{toolId}' armed WITHOUT an arbiter — this host wired no ToolController, so it "
                  + "cannot suspend the tool underneath (UXI-07 step 4b).");
                _pendingArm[toolId]();
                scope = NoScope.Instance;
            }

            scopeRef = scope;

            ct.Register(() =>
            {
                Remove();
                tcs.TrySetCanceled(ct);
            });

            // ⭐⭐ A BACKSTOP, not the mechanism. 🔴 MEASURED 2026-09-09: relying on this continuation
            //    ALONE was a RACE — a caller awaiting the pick could resume BEFORE the tool underneath
            //    was restored, because its await-continuation and this one are queued independently.
            //    The rail CompletingAPickResumesTheToolUnderneath caught it.
            // ⇒ ⭐ the real pop is in Remove(), tied to the gizmo actually going away. This covers the
            //    remaining case: a TCS completed WITHOUT the gizmo being removed. Both are idempotent.
            tcs.Task.ContinueWith(
                _ => scope.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The CALLBACK-STYLE half of the same protocol</b> — for a pick whose result does not come
        /// back as a <see cref="Task{T}"/>.
        ///
        /// <para>📐 <b>Measured:</b> a repo-wide grep shows only the two pick ADAPTERS combine a
        /// <see cref="TaskCompletionSource{T}"/> with a picker gizmo. <c>IgApplication</c>'s two arms
        /// (`CMD_PICK_LOCATION` / `CMD_PICK_ENTITY` from ExCon) and
        /// <c>ReplayBrowserSubsystem.ReplaySpatialPickerContext</c> instead publish their result — over the
        /// wire, or into a field the panel later reads. ⛔ They cannot use <see cref="RunPickAsync"/>,
        /// because there is no task to hang the pop on.</para>
        ///
        /// <para>⭐⭐ <b>It is the SAME mechanism, not a second protocol:</b> push, arm through the arbiter,
        /// and pop when the gizmo goes away. Only the completion signal differs — the gizmo's own
        /// <c>onRemove</c> instead of a task. ⇒ this is what replaces their PRIVATE ONE-SLOT ARBITERS
        /// (<c>_activeLocationPickerId</c>, <c>_activeEntityPickerId</c>, <c>_activeGizmoId</c>): the
        /// controller already guarantees one modal at a time, so a host-local "unregister my previous one"
        /// slot is exactly the duplicated mechanism step 4b exists to remove.</para>
        ///
        /// <para>⚠ Re-pushing the same picker while it is already up is a RE-TARGET, handled by the
        /// controller — ⛔ callers must NOT keep unregistering a previous id themselves.</para>
        /// </summary>
        /// <param name="createGizmo">
        /// Builds the picker, given a <c>remove</c> callback to use as its <c>onRemove</c>. ⭐ Invoking
        /// <c>remove</c> is what ends the interaction and resumes the tool underneath.
        /// </param>
        /// <returns><see langword="true"/> when the picker armed.</returns>
        public bool PushPicker(
            string                                toolId,
            Func<Action, IEntityStatefulGizmo>    createGizmo)
        {
            if (createGizmo == null) throw new ArgumentNullException(nameof(createGizmo));

            var manager = _global();
            if (manager == null)
            {
                ToolReport.Unserviceable(_report, toolId, "this host composes no global gizmo manager");
                return false;
            }

            long id = GlobalGizmoManager.NewId();
            IDisposable? scopeRef = null;

            void Remove()
            {
                _global()?.Unregister(id);
                scopeRef?.Dispose();   // ⭐ idempotent — see RunPickAsync
            }

            var gizmo = createGizmo(Remove);

            _pendingArm[toolId] = () =>
            {
                var g = _global();
                if (g == null) return ToolActivationOutcome.Unserviceable;
                g.Register(id, gizmo);
                return ToolActivationOutcome.Armed;
            };

            var tools = EnsureRegistered();
            if (tools == null)
            {
                ToolReport.Say(_report,
                    $"pick '{toolId}' armed WITHOUT an arbiter — this host wired no ToolController, so it "
                  + "cannot suspend the tool underneath (UXI-07 step 4b).");
                return _pendingArm[toolId]() == ToolActivationOutcome.Armed;
            }

            scopeRef = tools.PushModal(toolId);
            return tools.ActiveModal?.Id == toolId;
        }

        /// <summary>Returned when no arbiter was wired — disposing it must be safe and do nothing.</summary>
        private sealed class NoScope : IDisposable
        {
            internal static readonly NoScope Instance = new();
            public void Dispose() { }
        }
    }
}
