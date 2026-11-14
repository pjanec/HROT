using System;
using System.Collections.Generic;
using System.Numerics;
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
        private readonly Func<Vector2?>?           _mouseScreen;
        private readonly Func<double>              _clock;

        /// <summary>⭐ B-17 P4 — how long a refused click's reason stays at the cursor.</summary>
        public const double FlashSeconds = 1.5;
        /// <summary>⭐ B-17 P4 — how long <i>"replaced: …"</i> stays under the banner.</summary>
        public const double NoticeSeconds = 3.0;
        /// <summary>The banner's screen position (pixels, from the map's top-left): the Cancel button, then the prompt after it. ⭐ B-17 P7 ④ — <c>CancelLeftPx</c> /
        /// <c>BannerTopPx</c> are where the WORD is drawn; the button box is the word plus the drawer's padding (<c>GizmoTextDraw.PadX/PadY</c>) — ONE definition,
        /// <see cref="CancelBoxLeftPx"/> … <see cref="CancelBoxBottomPx"/>, which <see cref="OverCancel"/> tests. 📐 "Cancel" at the 13 px gizmo font is ~45 px wide (Roboto advances + 1 px spacing).</summary>
        public const float CancelLeftPx = 16f, PromptLeftPx = 100f, BannerTopPx = 12f, CancelTextWidthPx = 45f, GizmoTextHeightPx = 13f;
        public const float CancelBoxLeftPx = CancelLeftPx - GizmoMap.Presentation.GizmoTextDraw.PadX;
        public const float CancelBoxRightPx = CancelLeftPx + CancelTextWidthPx + GizmoMap.Presentation.GizmoTextDraw.PadX;
        public const float CancelBoxTopPx = BannerTopPx - GizmoMap.Presentation.GizmoTextDraw.PadY;
        public const float CancelBoxBottomPx = BannerTopPx + GizmoTextHeightPx + GizmoMap.Presentation.GizmoTextDraw.PadY;

        /// <summary>⭐ B-17 P4 — the pick in progress (null: none).</summary>
        private PendingPick? _pending;

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

        /// <summary>⭐ <c>CE-1346</c> — the entity picker this host has armed and not yet removed, and the tool id it armed under.</summary>
        private Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo? _livePicker;
        private string? _livePickerTool;

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
            Action<string>?           report = null,
            Func<Vector2?>?           mouseScreen = null,
            Func<double>?             clock = null)
        {
            _tools  = tools  ?? throw new ArgumentNullException(nameof(tools));
            _global = global ?? throw new ArgumentNullException(nameof(global));
            _report = report;
            _mouseScreen = mouseScreen;
            _clock = clock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);

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
        /// <summary>⭐ B-17 P2 — the host's tool controller (null on a host without one), for a picker tool that puts a line on the hint (<c>DrawingMenu.Attach</c>).</summary>
        public ToolController? Tools => _tools();

        /// <summary>⭐ B-17 P4 — the words of the pick in progress (<c>"Pick a point for Move to"</c>), or null when none (or a drawing, which is not a pick).</summary>
        public string? Prompt => _pending?.Prompt;

        /// <summary>⭐ B-17 P4 — the click the pending pick refused, for <see cref="FlashSeconds"/> after it; null otherwise.</summary>
        public PickRejection? Rejected
            => _pending is { RejectedReason: { } r } p && _clock() - p.RejectedAt < FlashSeconds ? new PickRejection(r, p.RejectedAt) : null;

        /// <summary>⭐ B-17 P4 — <i>"replaced: …"</i>, for <see cref="NoticeSeconds"/> after a request replaced the pick before it; null otherwise.</summary>
        public string? Notice => _pending is { } p ? NoticeOf(p) : null;

        internal string? NoticeOf(PendingPick p) => p.Notice is { } n && _clock() - p.NoticeAt < NoticeSeconds ? n : null;

        /// <summary>⭐ B-17 P4 — refuses a click on the pending pick: <paramref name="reason"/> flashes at the cursor and the pick stays pending. False with no pick.</summary>
        public bool Reject(string reason)
        {
            if (_pending is not { } p) return false;
            p.RejectedReason = reason; p.RejectedAt = _clock();
            return true;
        }

        /// <summary>⭐ B-17 P4 — the mouse is on the banner's [Cancel] (screen pixels; false when the host gave no mouse).</summary>
        internal bool OverCancel()
            => _mouseScreen?.Invoke() is { } m
               && m.X >= CancelBoxLeftPx && m.X <= CancelBoxRightPx
               && m.Y >= CancelBoxTopPx && m.Y <= CancelBoxBottomPx;

        /// <summary>⭐ B-17 P4 — cancels the pending pick as its Cancel / Esc does (the field's await completes cancelled). False with no pick.</summary>
        public bool CancelPending()
        {
            if (_pending is not { } p) return false;
            p.Cancel();
            return true;
        }

        /// <summary>A pick with a prompt replaces the one before it (cancelled, its caller's await completed cancelled); returns the replaced prompt.</summary>
        private string? ReplacePending(string? prompt)
        {
            if (prompt == null || _pending is not { Prompt: { } old } p) return null;
            p.Cancel();
            return old;
        }

        /// <summary>The pick record around a built gizmo, and the gizmo to register (wrapped with the banner when it has a prompt).</summary>
        private PendingPick Begin(string toolId, string? prompt, string? replaced, IEntityStatefulGizmo inner, Action remove, out IEntityStatefulGizmo toArm)
        {
            var pick = new PendingPick { ToolId = toolId, Prompt = prompt, Inner = inner };
            if (replaced != null) { pick.Notice = "replaced: " + replaced; pick.NoticeAt = _clock(); }
            pick.Remove = remove;
            _pending = pick;
            toArm = prompt == null ? inner : new PickBannerGizmo(inner, pick, this);
            return pick;
        }

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
                    new ToolDescriptor(id, pair.Item2, ToolModality.Modal, ToolArbiter.Global,
                        Hint: "click the map · Esc: stop picking", Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Crosshairs),   // ⭐ B-17 P1
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
            CancellationToken                                          ct = default,
            string?                                                    prompt = null)
        {
            if (createGizmo == null) throw new ArgumentNullException(nameof(createGizmo));
            prompt ??= PickPrompts.DefaultFor(toolId);

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

            string? replaced = ReplacePending(prompt);   // ⭐ B-17 P4 — a second request replaces the first, with a notice
            long id = GlobalGizmoManager.NewId();

            // ⚠⚠ Assigned below; captured here so Remove can pop. 🔴 The POP MUST HAPPEN WHEN THE GIZMO
            //    GOES AWAY, not on a task continuation — measured 2026-09-09, see the rail note below.
            IDisposable? scopeRef = null;

            IEntityStatefulGizmo? gizmo = null;

            void Remove()
            {
                if (ReferenceEquals(_livePicker, gizmo)) { _livePicker = null; _livePickerTool = null; }   // ⭐ CE-1346
                if (_pending != null && ReferenceEquals(_pending.Inner, gizmo)) _pending = null;   // ⭐ B-17 P4
                _global()?.Unregister(id);
                // ⭐ Idempotent (ModalScope.Dispose guards, and PopModalAt re-checks depth), so it is safe
                //   that Unregister→Dispose→onRemove can re-enter this.
                scopeRef?.Dispose();
            }

            gizmo = createGizmo(tcs, Remove);
            Track(toolId, gizmo);
            var pending = Begin(toolId, prompt, replaced, gizmo, Remove, out var toArm);
            pending.CancelCaller = () => tcs.TrySetCanceled();

            _pendingArm[toolId] = () =>
            {
                var g = _global();
                if (g == null) return ToolActivationOutcome.Unserviceable;
                g.Register(id, toArm);
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
            Func<Action, IEntityStatefulGizmo>    createGizmo,
            string?                               prompt = null)
        {
            if (createGizmo == null) throw new ArgumentNullException(nameof(createGizmo));
            prompt ??= PickPrompts.DefaultFor(toolId);

            var manager = _global();
            if (manager == null)
            {
                ToolReport.Unserviceable(_report, toolId, "this host composes no global gizmo manager");
                return false;
            }

            string? replaced = ReplacePending(prompt);   // ⭐ B-17 P4
            long id = GlobalGizmoManager.NewId();
            IDisposable? scopeRef = null;

            IEntityStatefulGizmo? gizmo = null;

            void Remove()
            {
                if (ReferenceEquals(_livePicker, gizmo)) { _livePicker = null; _livePickerTool = null; }   // ⭐ CE-1346
                if (_pending != null && ReferenceEquals(_pending.Inner, gizmo)) _pending = null;   // ⭐ B-17 P4
                _global()?.Unregister(id);
                scopeRef?.Dispose();   // ⭐ idempotent — see RunPickAsync
            }

            gizmo = createGizmo(Remove);
            Track(toolId, gizmo);
            Begin(toolId, prompt, replaced, gizmo, Remove, out var toArm);

            _pendingArm[toolId] = () =>
            {
                var g = _global();
                if (g == null) return ToolActivationOutcome.Unserviceable;
                g.Register(id, toArm);
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

        private void Track(string toolId, IEntityStatefulGizmo gizmo)
        {
            if (gizmo is Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo picker) { _livePicker = picker; _livePickerTool = toolId; }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1346</c> (docs/UX/UX_Feature_Tool_Model.md §4.16) — offers <paramref name="entity"/> to the entity pick in progress, as
        /// if it had been clicked on the map: the picker's own filter decides, and a taken offer ends the pick the click's way (the
        /// await completes, the tool underneath resumes). False when no entity pick is up, when it is suspended under another tool,
        /// or when its filter refuses the entity. 🔒 User, 2026-10-25: "when such an entity picking mode is used it would work also by
        /// clicking the entity in the Orbat tree".
        /// </summary>
        public bool TryOfferEntity(Fdp.Core.Entity entity)
        {
            if (_livePicker is not { } picker) return false;
            if (_tools() is { } tools && tools.ActiveModal is { } active && active.Id != _livePickerTool) return false;   // suspended
            if (picker.TryPick(entity)) return true;
            Reject("that is not a valid pick here");   // ⭐ B-17 P4 — the filter refused it: say so, the pick stays
            return false;
        }

        /// <summary>Returned when no arbiter was wired — disposing it must be safe and do nothing.</summary>
        private sealed class NoScope : IDisposable
        {
            internal static readonly NoScope Instance = new();
            public void Dispose() { }
        }
    }
}
