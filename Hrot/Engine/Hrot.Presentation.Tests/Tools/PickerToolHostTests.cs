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
    }
}
