using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.ScenarioEditor.Tools;
using Xunit;

namespace Hrot.Presentation.Tests.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 4b — rails for <see cref="PickerToolHost"/>, the ONE picker-modal
    /// protocol.</b> 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.12.
    ///
    /// <para>⚠ <b>Why the rails live here and not in each host's suite</b> (<c>R-142</c> ④): the thing
    /// under test is the SHARED protocol, and it has no suite of its own until this file. ⛔ The four
    /// converted sites — <c>CanvasMapPickAdapter</c>, <c>EditorMapPickAdapter</c>, <c>IgApplication</c>'s
    /// two remote arms, <c>ReplayBrowserSubsystem</c> — differ only in WHICH gizmo they hand it and what
    /// they do with the result. ⭐ Testing each host's wiring separately would be four copies of one
    /// claim; the host-level claim is covered by the composition-root rails instead.</para>
    ///
    /// <para>🔴 <b>What these pin, in one sentence:</b> a pick is an INTERRUPTION — the tool underneath is
    /// suspended and comes back — and the host no longer needs a private one-slot arbiter to get there.</para>
    /// </summary>
    public sealed class PickerToolHostTests
    {
        /// <summary>Stands in for "the tool the operator was already using".</summary>
        private sealed class ToolProbe : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus => true;
            public bool IsFocused { get; private set; }
            public bool Disposed  { get; private set; }

            public void SetFocus(bool f) { IsFocused = f; }
            public void UpdateAndDraw(ISimulationView v, float dt, IDebugDrawBuilder b) { }
            public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
            public void OnDragUpdate(Vector3 p) { }
            public void OnCommit(Vector3 w) { }
            public void OnMenuAction(int id) { }
            public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w) { }
            public void OnKeyEvent(MapKeyboardKey k, bool p) { }
            public void OnCancel() { }
            public void Dispose() { Disposed = true; }
        }

        private sealed class Fixture
        {
            public readonly GlobalGizmoManager Global = new(new DebugPrimitiveBuffer());
            public readonly ToolController     Controller;
            public readonly PickerToolHost     Pickers;

            public Fixture(bool withArbiter = true)
            {
                Controller = new ToolController(() => Global, () => null);
                Pickers    = new PickerToolHost(
                    () => withArbiter ? Controller : null, () => Global);
            }

            /// <summary>Arms a Global-arbiter modal, as a route/measure tool would be.</summary>
            public ToolProbe ArmATool(string id = "route")
            {
                var probe = new ToolProbe();
                Controller.Register(
                    new ToolDescriptor(id, id, ToolModality.Modal, ToolArbiter.Global),
                    _ => { Global.Register(GlobalGizmoManager.NewId(), probe); return ToolActivationOutcome.Armed; });
                Assert.True(Controller.Activate(id));
                Assert.True(probe.IsFocused);
                return probe;
            }
        }

        // ── PushPicker — the callback-style half ──────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>THE claim of step 4b: a pick SUSPENDS the tool underneath; it does not destroy it.</b>
        /// ⛔ <c>Assert.False(route.Disposed)</c> is what an <c>Activate</c>-based conversion fails.
        /// </summary>
        [Fact]
        public void PushPickerSuspendsTheToolUnderneathRatherThanDestroyingIt()
        {
            var fx    = new Fixture();
            var route = fx.ArmATool();

            var armed = fx.Pickers.PushPicker(
                ScenarioToolIds.PickLocation, _ => new ToolProbe());

            Assert.True(armed);
            Assert.Equal(ScenarioToolIds.PickLocation, fx.Controller.ActiveModal?.Id);
            Assert.False(route.IsFocused);   // yielded the input …
            Assert.False(route.Disposed);    // ⭐⭐ … but ALIVE
        }

        /// <summary>
        /// ⭐⭐ <b>Invoking <c>remove</c> ends the interaction and RESUMES the tool underneath.</b>
        /// 🔒 For a callback-style pick there is no task, so the gizmo's own <c>onRemove</c> is the only
        /// completion signal — this rail is what makes that binding load-bearing rather than incidental.
        /// </summary>
        [Fact]
        public void InvokingRemoveResumesTheToolUnderneath()
        {
            var fx    = new Fixture();
            var route = fx.ArmATool();

            Action? remove = null;
            fx.Pickers.PushPicker(ScenarioToolIds.PickLocation, r => { remove = r; return new ToolProbe(); });
            Assert.NotNull(remove);

            remove!();

            Assert.False(route.Disposed);
            Assert.True(route.IsFocused);    // ⭐⭐ the tool has the input back
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE PRIVATE ONE-SLOT ARBITER IS UNNECESSARY — this is what its deletion rests on.</b>
        /// 🔴 Every converted host kept a field (<c>_activeLocationPickerId</c>, <c>_activeEntityPickerId</c>,
        /// <c>_activeGizmoId</c>) purely to unregister its previous picker before arming a new one.
        /// ⭐ The controller already guarantees one modal at a time, so re-pushing RE-TARGETS: the stack
        /// does not grow and the second picker replaces the first.
        /// </summary>
        [Fact]
        public void RePushingTheSamePickerRetargetsRatherThanStacking()
        {
            var fx = new Fixture();
            fx.ArmATool();

            var first  = new ToolProbe();
            var second = new ToolProbe();

            fx.Pickers.PushPicker(ScenarioToolIds.PickLocation, _ => first);
            int depthAfterFirst = fx.Controller.ModalStack.Count;

            fx.Pickers.PushPicker(ScenarioToolIds.PickLocation, _ => second);

            Assert.Equal(depthAfterFirst, fx.Controller.ModalStack.Count);   // ⛔ did NOT stack
            Assert.Equal(ScenarioToolIds.PickLocation, fx.Controller.ActiveModal?.Id);
            Assert.True(first.Disposed);      // the previous picker is gone …
            Assert.False(second.Disposed);    // … and the new one is live
        }

        /// <summary>
        /// ⭐ With no arbiter the pick still WORKS (a capability may not be lost — <c>R-137</c>) but it
        /// cannot suspend anything, and it SAYS SO rather than silently reintroducing the bypass.
        /// </summary>
        [Fact]
        public void WithNoArbiterThePickStillArmsAndReportsThatItCannotSuspend()
        {
            var reports = new System.Collections.Generic.List<string>();
            var global  = new GlobalGizmoManager(new DebugPrimitiveBuffer());
            var pickers = new PickerToolHost(() => null, () => global, reports.Add);

            bool armed = pickers.PushPicker(ScenarioToolIds.PickLocation, _ => new ToolProbe());

            Assert.True(armed);
            Assert.Equal(1, global.ActiveCount);
            Assert.Contains(reports, r => r.Contains("WITHOUT an arbiter"));
        }

        // ── RunPickAsync — the task-style half ────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ The <c>await</c>-style half suspends identically. ⚠ Pinned separately because the two
        /// halves complete by different signals — a task vs the gizmo's <c>onRemove</c> — and §4.12
        /// records that binding the pop to the TASK was a race.
        /// </summary>
        [Fact]
        public void RunPickAsyncAlsoSuspendsTheToolUnderneath()
        {
            var fx    = new Fixture();
            var route = fx.ArmATool();

            _ = fx.Pickers.RunPickAsync<int>(
                ScenarioToolIds.PickEntity, (tcs, remove) => new ToolProbe());

            Assert.Equal(ScenarioToolIds.PickEntity, fx.Controller.ActiveModal?.Id);
            Assert.False(route.Disposed);
        }

        /// <summary>
        /// ⛔ A pick on a host with NO gizmo manager must not hang the caller for ever — it cancels and
        /// reports. 🔒 A silently-never-completing task is the worst failure available here.
        /// </summary>
        [Fact]
        public async Task RunPickAsyncWithNoGizmoManagerCancelsRatherThanHanging()
        {
            var reports = new System.Collections.Generic.List<string>();
            var pickers = new PickerToolHost(() => null, () => null, reports.Add);

            var task = pickers.RunPickAsync<int>(
                ScenarioToolIds.PickEntity, (tcs, remove) => new ToolProbe());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Contains(reports, r => r.Contains("no global gizmo manager"));
        }
    
        // ── CE-1346 — a pick from ANOTHER source (an ORBAT row) ───────────────────────────────────────────

        private sealed class OnlyEven : Fdp.Toolkit.Vis2D.Abstractions.IEntityFilter
        {
            public bool IsMatch(Fdp.Core.Entity entity) => entity.Index % 2 == 0;
        }

        private static Task<Fdp.Core.Entity> ArmEntityPick(Fixture fx)
            => fx.Pickers.RunPickAsync<Fdp.Core.Entity>(ScenarioToolIds.PickEntity, (tcs, remove) =>
                new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(_ => Fdp.Core.Entity.Null, new OnlyEven(),
                    e => tcs.TrySetResult(e), () => tcs.TrySetCanceled(), remove));

        /// <summary>⭐⭐ <c>CE-1346</c> (Tool Model §4.16) — an entity offered from elsewhere completes the pick exactly as a map click
        /// would: the picker's filter decides, the await gets the entity, and the tool underneath resumes. ✅ Red-proof: the filter
        /// check dropped from <c>TryPick</c> ⇒ the refused entity is taken.</summary>
        [Fact]
        public async Task AnOfferedEntity_CompletesThePick_ThroughThePickersOwnFilter()
        {
            var fx    = new Fixture();
            var route = fx.ArmATool();
            var task  = ArmEntityPick(fx);

            Assert.False(fx.Pickers.TryOfferEntity(new Fdp.Core.Entity(3, 1)));   // the filter refuses it: the pick waits
            Assert.False(task.IsCompleted);
            Assert.Equal(ScenarioToolIds.PickEntity, fx.Controller.ActiveModal?.Id);

            Assert.True(fx.Pickers.TryOfferEntity(new Fdp.Core.Entity(4, 1)));
            Assert.Equal(4, (await task).Index);
            Assert.True(route.IsFocused);                                           // the tool underneath has the input back
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
            Assert.False(fx.Pickers.TryOfferEntity(new Fdp.Core.Entity(6, 1)));   // and the pick is over
        }

        /// <summary>⭐ <c>CE-1346</c> — with no entity pick up, or one suspended under another tool, an offer is refused (the ORBAT row
        /// then selects, as before). ✅ Red-proof: the suspended check dropped ⇒ a buried pick is answered.</summary>
        [Fact]
        public void NoPickOrASuspendedPick_RefusesAnOffer()
        {
            var fx = new Fixture();
            Assert.False(fx.Pickers.TryOfferEntity(new Fdp.Core.Entity(2, 1)));   // nothing armed

            var task = ArmEntityPick(fx);
            // ⭐ B-17 P4: a second PICK now replaces the first, so the buried case is a non-pick modal pushed over it (the overlay of a drawing tool)
            fx.Controller.Register(new ToolDescriptor("overlay", "overlay", ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(GlobalGizmoManager.NewId(), new ToolProbe()); return ToolActivationOutcome.Armed; });
            fx.Controller.PushModal("overlay");   // something over it
            Assert.False(fx.Pickers.TryOfferEntity(new Fdp.Core.Entity(2, 1)));
            Assert.False(task.IsCompleted);
        }

        // ── ⭐ B-17 P4 — the pick is visible (docs/UX/DESIGN_Map_Interaction.md §2.3, §2.5 D12) ─────────────────────────

        private static readonly Fdp.Toolkit.Vis2D.Abstractions.IEntityFilter RefuseAll = new Refuse();
        private sealed class Refuse : Fdp.Toolkit.Vis2D.Abstractions.IEntityFilter { public bool IsMatch(Entity e) => false; }

        /// <summary>A real manager + controller + host, a world whose bus carries the terminal's events, a settable clock and mouse.</summary>
        private sealed class P4Fx : IDisposable
        {
            public readonly DebugPrimitiveBuffer Buffer = new();
            public readonly GlobalGizmoManager   Global;
            public readonly ToolController       Controller;
            public readonly PickerToolHost       Pickers;
            public readonly EntityRepository     Repo = new();
            public double Now;
            public Vector2? Mouse;

            public P4Fx()
            {
                Global     = new GlobalGizmoManager(Buffer);
                Controller = new ToolController(() => Global, () => null);
                Pickers    = new PickerToolHost(() => Controller, () => Global, mouseScreen: () => Mouse, clock: () => Now);
                Repo.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoDragUpdateEvent>();
                Repo.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMouseEvent>();
                Repo.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoKeyEvent>();
            }

            public void Dispose() => Repo.Dispose();

            /// <summary>One frame: the events published so far reach the focused gizmo; returns what it drew (text, screen-space?).</summary>
            public (string Text, bool Screen)[] Frame()
            {
                Repo.Bus.SwapBuffers();
                Buffer.Clear();
                Global.Execute(Repo, 0.016f);
                return Buffer.GetFrame().ToArray().Where(q => q.Shape == DebugPrimitiveShape.Text)
                    .Select(q => (q.StringHash != 0 ? Buffer.InternMap.TryResolve(q.StringHash)! : q.TextContent.ToString(), q.Space == CoordinateSpace.Screen)).ToArray();
            }

            public void Move(float x, float y) => Repo.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoDragUpdateEvent { WorldPos = new Vector3(x, y, 0f) });
            public void Click(float x, float y)
            {
                Repo.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMouseEvent { Button = MapMouseButton.Left, IsPressed = true, WorldPos = new Vector3(x, y, 0f) });
                Repo.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMouseEvent { Button = MapMouseButton.Left, IsPressed = false, WorldPos = new Vector3(x, y, 0f) });
            }
            public void Esc() => Repo.Bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoKeyEvent { Key = MapKeyboardKey.Escape, IsPressed = true });

            public Task<Vector2> PickPoint(string prompt, CancellationToken ct = default)
                => Pickers.RunPickAsync<Vector2>(ScenarioToolIds.PickLocation,
                    (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.FdpLocationPickerGizmo(
                        onPicked: w => tcs.TrySetResult(w), onRemove: remove, onCancelled: () => tcs.TrySetCanceled()),
                    ct, prompt);

            public Task<int> PickUnit(string prompt, Func<Vector2, Entity> hit)
                => Pickers.RunPickAsync<int>(ScenarioToolIds.PickEntity,
                    (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(
                        hitTest: hit, filter: RefuseAll, onPicked: _ => tcs.TrySetResult(1), onCancelled: () => tcs.TrySetCanceled(), onRemove: remove,
                        onRejected: _ => Pickers.Reject("pick a unit, not a graphic")),
                    default, prompt);
        }

        /// <summary>
        /// ⭐ B-17 P4 — the prompt reaches the banner: <c>[Cancel]</c> and the prompt are drawn as SCREEN-SPACE gizmo text (a remote terminal draws them too), while the pick is pending, and gone when it ends.
        /// Red-proof: the host not wrapping the picker in the banner gizmo ⇒ no text is drawn.
        /// </summary>
        [Fact]
        public void P4_ThePrompt_ReachesTheBannerPrimitives_WhilePending()
        {
            using var fx = new P4Fx();
            var pick = fx.PickPoint("Pick a point for Move to");
            Assert.Equal("Pick a point for Move to", fx.Pickers.Prompt);
            var texts = fx.Frame();
            Assert.Contains(("Cancel", true), texts);
            Assert.Contains(("Pick a point for Move to", true), texts);
            fx.Click(5, 6);
            fx.Frame();
            Assert.True(pick.IsCompletedSuccessfully);
            Assert.Null(fx.Pickers.Prompt);
            Assert.DoesNotContain(fx.Frame(), t => t.Text == "Cancel");
        }

        /// <summary>
        /// ⭐ B-17 P7 ④ — the banner's look: <c>Cancel</c> is a <see cref="GizmoTextStyle.Button"/>, the prompt a <see cref="GizmoTextStyle.Backdrop"/>, the refusal at the cursor a Backdrop, and while a pick is pending a CROSSHAIR of two
        /// screen-pixel bars (18 × 2 px, white with a dark outline) stands at the cursor's world point; ⛔ none of it once the pick ends. The steady frame allocates 0 B (a hot-path rule). 🔴 Red-proofs (compile): <c>DrawCrosshair</c> not called ⇒
        /// no bars; the Cancel left as Plain ⇒ the style assert goes red.
        /// </summary>
        [Fact]
        public void P7_TheBanner_IsBackdropAndButton_AndACrosshairStandsAtTheCursor_At0BSteady()
        {
            using var fx = new P4Fx();
            var pick = fx.PickUnit("Pick a unit for Target", _ => new Entity(9, 1));   // a hit the filter refuses ⇒ the rejection flash
            fx.Move(40, 50);
            fx.Frame();         // the cursor reaches the picker
            fx.Click(41, 51);   // refused: a graphic, not a unit — flashes the reason at the cursor
            fx.Frame();         // the click is handled
            fx.Frame();         // and this frame draws the flash
            var frame = fx.Buffer.GetFrame().ToArray();
            string TextOfPrim(DebugPrimitive q) => q.StringHash != 0 ? fx.Buffer.InternMap.TryResolve(q.StringHash)! : q.TextContent.ToString();
            DebugPrimitive Text(string s) => frame.SingleOrDefault(q => q.Shape == DebugPrimitiveShape.Text && TextOfPrim(q) == s) is { Shape: DebugPrimitiveShape.Text } hit ? hit
                : throw new Xunit.Sdk.XunitException($"no text '{s}' in the frame; texts: " + string.Join(" | ", frame.Where(q => q.Shape == DebugPrimitiveShape.Text).Select(TextOfPrim)));
            Assert.Equal(GizmoTextStyle.Button, Text("Cancel").TextStyle);
            Assert.Equal(GizmoTextStyle.Backdrop, Text("Pick a unit for Target").TextStyle);
            Assert.Equal(GizmoTextStyle.Backdrop, Text("pick a unit, not a graphic").TextStyle);

            var bars = frame.Where(q => q.Shape == DebugPrimitiveShape.Box2D).ToArray();
            Assert.Equal(2, bars.Length);
            foreach (var b in bars)
            {
                Assert.Equal(SizeMode.ScreenPixels, b.SizeMode);                       // constant pixel size at every zoom, in 2-D and in 3-D
                Assert.Equal(41f, b.BoxCenterX); Assert.Equal(51f, b.BoxCenterY);      // at the cursor's world point
            }
            Assert.Contains(bars, b => b.BoxExtentX == 9f && b.BoxExtentY == 1f);     // 18 × 2 px
            Assert.Contains(bars, b => b.BoxExtentX == 1f && b.BoxExtentY == 9f);     // 2 × 18 px

            fx.Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            fx.Buffer.Clear();
            fx.Global.Execute(fx.Repo, 0.016f);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(delta == 0, $"the pending-pick banner + crosshair allocates {delta} B in a steady frame");

            fx.Esc(); fx.Frame(); fx.Frame();   // Esc is handled, then a frame with no pick
            Assert.DoesNotContain(fx.Buffer.GetFrame().ToArray(), q => q.Shape == DebugPrimitiveShape.Box2D);   // no crosshair once the pick has ended
        }

        /// <summary>⭐ B-17 P7 ④ — the Cancel hit area is the Button box the drawer paints: the word plus its padding (ONE definition, <see cref="PickerToolHost.CancelBoxLeftPx"/> …).</summary>
        [Fact]
        public void P7_TheCancelHitArea_IsTheButtonBox()
        {
            Assert.Equal(GizmoMap.Presentation.GizmoTextDraw.PadX, PickerToolHost.CancelLeftPx - PickerToolHost.CancelBoxLeftPx);
            Assert.Equal(GizmoMap.Presentation.GizmoTextDraw.PadY, PickerToolHost.BannerTopPx - PickerToolHost.CancelBoxTopPx);
            Assert.True(PickerToolHost.CancelBoxRightPx < PickerToolHost.PromptLeftPx - GizmoMap.Presentation.GizmoTextDraw.PadX, "the button box ends before the prompt's own box begins");
            using var fx = new P4Fx();
            _ = fx.PickPoint("p");
            fx.Mouse = new Vector2(PickerToolHost.CancelBoxRightPx - 1f, PickerToolHost.CancelBoxBottomPx - 1f);   // inside the box's far corner
            Assert.True(fx.Pickers.OverCancel());
            fx.Mouse = new Vector2(PickerToolHost.CancelBoxRightPx + 1f, PickerToolHost.BannerTopPx);
            Assert.False(fx.Pickers.OverCancel());
            fx.Mouse = new Vector2(PickerToolHost.CancelLeftPx, PickerToolHost.CancelBoxBottomPx + 1f);
            Assert.False(fx.Pickers.OverCancel());
        }

        /// <summary>⭐ B-17 P4 — a pick with no prompt of its own gets its tool's default words; the field's path becomes <i>"Pick a point for Move to"</i>.</summary>
        [Fact]
        public void P4_ThePromptWords_AreTheFieldsOrTheToolsDefault()
        {
            Assert.Equal("Pick a point for Move to", PickPrompts.For("a point", "Orders[0].MoveTo"));
            Assert.Equal("Pick a unit for Target unit", PickPrompts.For("a unit", "Target_unit"));
            Assert.Equal("Pick a unit", PickPrompts.For("a unit", null));
            using var fx = new P4Fx();
            _ = fx.Pickers.RunPickAsync<int>(ScenarioToolIds.PickEntity,
                (tcs, remove) => new ToolProbe(), default);
            Assert.Equal(PickPrompts.DefaultFor(ScenarioToolIds.PickEntity), fx.Pickers.Prompt);
        }

        /// <summary>⭐ B-17 P4 — a left click on the banner's [Cancel] cancels the pick (the asking field's await completes CANCELLED, which is what clears its Picking mark) and does NOT place a point; the tool underneath resumes.</summary>
        [Fact]
        public async Task P4_CancelOnTheBanner_CancelsThePick_AndResumesTheToolUnderneath()
        {
            using var fx = new P4Fx();
            var probe = new ToolProbe();
            fx.Controller.Register(new ToolDescriptor("route", "route", ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(GlobalGizmoManager.NewId(), probe); return ToolActivationOutcome.Armed; });
            Assert.True(fx.Controller.Activate("route"));

            var pick = fx.PickPoint("Pick a point for Move to");
            Assert.False(probe.IsFocused);
            fx.Mouse = new Vector2(30, 18);                // on [Cancel]
            fx.Click(100, 100);
            fx.Frame();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pick);
            Assert.Null(fx.Pickers.Prompt);
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
            Assert.True(probe.IsFocused);                   // the suspended tool resumes
        }

        /// <summary>⭐ B-17 P4 — the same click anywhere else is the pick (and Esc still cancels).</summary>
        [Fact]
        public async Task P4_AClickOffTheBanner_PicksThePoint_AndEscStillCancels()
        {
            using var fx = new P4Fx();
            var pick = fx.PickPoint("Pick a point for Move to");
            fx.Mouse = new Vector2(400, 300);
            fx.Click(7, 8);
            fx.Frame();
            Assert.Equal(new Vector2(7, 8), await pick);

            var second = fx.PickPoint("Pick again");
            fx.Esc();
            fx.Frame();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        }

        /// <summary>
        /// ⭐ B-17 P4 — a click the entity picker refuses flashes its reason at the cursor for 1.5 s, and the pick stays pending (then the flash is gone).
        /// Red-proof: the picker not reporting the refusal ⇒ Rejected stays null.
        /// </summary>
        [Fact]
        public void P4_ARejectedClick_FlashesTheReason_AndThePickStaysPending()
        {
            using var fx = new P4Fx();
            var graphic = new Entity(7, 1);
            var pick = fx.PickUnit("Pick a unit for Target", _ => graphic);
            fx.Mouse = new Vector2(400, 300);
            fx.Move(50, 60);
            fx.Frame();
            Assert.Null(fx.Pickers.Rejected);
            fx.Now = 10.0;
            fx.Click(50, 60);
            fx.Frame();                       // the click is handled
            var texts = fx.Frame();           // and the next frame draws the flash
            Assert.Equal("pick a unit, not a graphic", fx.Pickers.Rejected?.Reason);
            Assert.Contains(("pick a unit, not a graphic", false), texts);   // drawn at the cursor (world space)
            Assert.False(pick.IsCompleted);                                    // the pick stays
            Assert.Equal("Pick a unit for Target", fx.Pickers.Prompt);
            fx.Now = 10.0 + PickerToolHost.FlashSeconds + 0.1;
            Assert.Null(fx.Pickers.Rejected);
            Assert.DoesNotContain(fx.Frame(), t => t.Text == "pick a unit, not a graphic");
        }

        /// <summary>⭐ B-17 P4 — an entity OFFERED to the pick (an ORBAT row) that the filter refuses flashes too, and the pick stays.</summary>
        [Fact]
        public void P4_ARefusedOffer_Flashes()
        {
            using var fx = new P4Fx();
            var pick = fx.Pickers.RunPickAsync<int>(ScenarioToolIds.PickEntity,
                (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(_ => Entity.Null, RefuseAll, _ => tcs.TrySetResult(1), () => tcs.TrySetCanceled(), remove),
                default, "Pick a unit for Target");
            Assert.False(fx.Pickers.TryOfferEntity(new Entity(3, 1)));
            Assert.NotNull(fx.Pickers.Rejected);
            Assert.False(pick.IsCompleted);
        }

        /// <summary>
        /// ⭐ B-17 P4 — a second pick request REPLACES the first: the first's await completes cancelled, a notice names it (<i>"replaced: Pick a point for …"</i>, drawn under the banner,
        /// gone after a few seconds), the second is the live one, and the tool underneath is still suspended once (not twice).
        /// Red-proof: not cancelling the first ⇒ its task hangs and the notice is missing.
        /// </summary>
        [Fact]
        public async Task P4_ASecondRequest_ReplacesTheFirst_WithANotice()
        {
            using var fx = new P4Fx();
            var probe = new ToolProbe();
            fx.Controller.Register(new ToolDescriptor("route", "route", ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(GlobalGizmoManager.NewId(), probe); return ToolActivationOutcome.Armed; });
            Assert.True(fx.Controller.Activate("route"));

            var first = fx.PickPoint("Pick a point for Move to");
            fx.Now = 1.0;
            var second = fx.Pickers.RunPickAsync<int>(ScenarioToolIds.PickEntity,
                (tcs, remove) => new Fdp.Toolkit.Vis2D.Gizmos.EntityPickerGizmo(_ => Entity.Null, RefuseAll, _ => tcs.TrySetResult(1), () => tcs.TrySetCanceled(), remove),
                default, "Pick a unit for Target");

            Assert.True(await Task.WhenAny(first, Task.Delay(2000)) == first, "the first pick's await must complete (cancelled), not hang");
            Assert.True(first.IsCanceled);
            Assert.False(second.IsCompleted);
            Assert.Equal("Pick a unit for Target", fx.Pickers.Prompt);
            Assert.Equal("replaced: Pick a point for Move to", fx.Pickers.Notice);
            Assert.Contains(("replaced: Pick a point for Move to", true), fx.Frame());
            Assert.Equal(ScenarioToolIds.PickEntity, fx.Controller.ActiveModal?.Id);
            fx.Now = 1.0 + PickerToolHost.NoticeSeconds + 0.1;
            Assert.Null(fx.Pickers.Notice);

            fx.Esc();                                       // the one Esc ends the live pick and the route comes back (it was suspended once, not stacked)
            fx.Frame();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
            Assert.True(probe.IsFocused);
        }

        /// <summary>⭐ B-17 P4 — the form side: Cancel on the banner completes the broker's pending pick (its Picking mark clears — <c>IsPickPendingFor</c> goes false) and the next field's pick is then free.</summary>
        [Fact]
        public void P4_CancelOnTheBanner_ClearsTheFormsPickingMark()
        {
            using var fx = new P4Fx();
            var service = new HostBackedPickService(fx);
            var broker = new Hrot.Presentation.Facades.MapPickBroker(() => service);
            broker.RequestLocationPick("Orders[0].MoveTo");
            Assert.True(broker.IsPickPendingFor("Orders[0].MoveTo"));
            Assert.Equal("Pick a point for Move to", fx.Pickers.Prompt);   // the asking field's words
            fx.Mouse = new Vector2(30, 18);
            fx.Click(1, 1);
            fx.Frame();
            Assert.False(broker.IsPickPendingFor("Orders[0].MoveTo"));
        }

        private sealed class HostBackedPickService : Hrot.UI.Common.Facades.IMapPickService
        {
            private readonly P4Fx _fx;
            public HostBackedPickService(P4Fx fx) => _fx = fx;
            public Task<Hrot.Core.Mission.GeoPoint> PickLocationAsync(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<Fdp.Core.WorldPoint> PickLocalAsync(string? prompt, CancellationToken ct = default)
                => _fx.PickPoint(prompt ?? "?", ct).ContinueWith(t => new Fdp.Core.WorldPoint(t.Result.X, t.Result.Y, 0f), TaskContinuationOptions.OnlyOnRanToCompletion);
            public Task<int> PickEntityAsync(string[]? filterPresets = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<System.Collections.Generic.IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
