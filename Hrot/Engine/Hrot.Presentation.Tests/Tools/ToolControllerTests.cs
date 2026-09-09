using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using GizmoMap.Network;
using Hrot.ScenarioEditor.Tools;
using Xunit;

namespace Hrot.Presentation.Tests.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> <c>A1</c> — the rails for <see cref="ToolController"/>.</b>
    /// 📄 Design + both diagrams: <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.
    ///
    /// <para>⛔⛔ <b>These rails exist because the defect was REPRODUCED, not suspected.</b> 📐 Measured
    /// <c>2026-09-09</c> with a throwaway probe wired exactly as <c>MapInteractionPack.Build</c> does: two
    /// "exclusive" tools held focus at once and ONE <c>GizmoMouseEvent</c> reached BOTH. An inverse-edit
    /// red-proof (flip one tool's <c>RequiresExclusiveFocus</c>) removed the duplicate delivery, so the
    /// probe measured focus routing rather than an artefact.</para>
    ///
    /// <para>🔒 <b>And it is not academic:</b> the Windows lane measured that wiring
    /// <c>CanvasMapPickAdapter</c> to a live <c>GlobalGizmoManager</c> on SimHost breaks
    /// <c>hill-attack-close</c> (<c>CE-254</c>) — this is the ground under that.</para>
    ///
    /// <para>⚠ <b>Why a new class rather than folding into an existing suite</b> (<c>R-142</c> ④): the
    /// controller is a NEW feature with no suite of its own, and the arbiter suites live in
    /// <c>Fdp.Toolkits.Tests</c>, which cannot reference <c>Hrot.Presentation</c>. ⛔ The rails for the
    /// ARBITERS themselves stay in <c>GizmoHeadlessTests</c>; these are the controller's.</para>
    /// </summary>
    public sealed class ToolControllerTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public void Dispose() => _world.Dispose();

        /// <summary>A modal tool that records focus and counts the input it receives.</summary>
        private sealed class ProbeGizmo : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus { get; init; } = true;
            public bool IsFocused  { get; private set; }
            public bool Disposed   { get; private set; }
            public bool CancelSeen { get; private set; }
            public int  MouseCount { get; private set; }

            public void SetFocus(bool f) { IsFocused = f; }
            public void UpdateAndDraw(ISimulationView view, float dt, IDebugDrawBuilder b) { }
            public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
            public void OnDragUpdate(Vector3 pos) { }
            public void OnCommit(Vector3 w) { }
            public void OnMenuAction(int id) { }
            public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w) { MouseCount++; }
            public void OnKeyEvent(MapKeyboardKey k, bool p) { }
            public void OnCancel() { CancelSeen = true; }
            public void Dispose() { Disposed = true; }
        }

        /// <summary>The production wiring: ONE buffer, ONE bus, BOTH arbiters — MapInteractionPack.cs:92-100.</summary>
        private sealed class Fixture
        {
            public readonly DebugPrimitiveBuffer  Buffer = new();
            public readonly FdpEventBus           Bus    = new();
            public readonly GlobalGizmoManager    Global;
            public readonly DataDrivenGizmoSystem DataDriven;
            public readonly ToolController        Controller;

            public Fixture()
            {
                Global     = new GlobalGizmoManager(Buffer, Bus);
                DataDriven = new DataDrivenGizmoSystem(new GizmoRegistry(), Buffer, interactionBus: Bus);
                Controller = new ToolController(() => Global, () => DataDriven);
            }
        }

        /// <summary>
        /// ⛔⛔⛔ <b>READ THIS BEFORE CHANGING THE THREE RAILS BELOW — the first version of them was
        /// VACUOUS, and the inverse-edit red-proof is what caught it.</b>
        ///
        /// <para>📐 <b>Measured <c>2026-09-09</c>:</b> the first draft armed BOTH tools through the
        /// controller, then deleted <c>CancelOtherArbiter</c> to red-proof it — and all seven rails still
        /// PASSED. ⭐ The reason is worth keeping: <c>CancelActiveModalWithoutNotify()</c> already tears the
        /// previous modal down through ITS OWN arbiter when the stack pops, so with both tools registered
        /// the cross-cancel is redundant and the rails proved nothing.</para>
        ///
        /// <para>⭐⭐⭐ <b><c>CancelOtherArbiter</c> earns its keep in exactly ONE situation: the other
        /// arbiter holds a modal the controller DID NOT ARM.</b> That is not hypothetical — it is
        /// <c>EditorMapPickAdapter</c>, <c>EditorZoneAdapter</c> and <c>ScenarioSpawnAdapter</c>, which call
        /// <c>GlobalGizmoManager.Register</c> directly and are NOT converted in this slice
        /// (§Migration step 4). ⇒ ⭐ the rails below simulate that bypass, because that is the real case.</para>
        ///
        /// <para>🔒 <b>And this sharpens <c>Q27-A</c>'s condition.</b> The design said A1 closes the defect
        /// only <i>iff</i> every modal activation routes through the controller. Measured: the controller
        /// defends against a bypassing activation TOO, so converting the adapters makes the guard
        /// unnecessary rather than being a precondition for correctness. ⚠ Converting them is still the
        /// right end state — one arbiter beats one arbiter plus a sweeper.</para>
        /// </summary>
        private static void BypassAndArmDirectlyOnGlobal(Fixture fx, ProbeGizmo bypassing)
            => fx.Global.Register(GlobalGizmoManager.NewId(), bypassing);

        /// <summary>
        /// ⭐⭐⭐ <b>THE ONE THAT MATTERS — a BYPASSING modal cannot keep focus when a tool arms.</b>
        ///
        /// <para>⚠ Inverse-edit red-proof, re-run after the vacuity fix: commenting out the
        /// <c>CancelOtherArbiter</c> call makes this FAIL with both gizmos reporting <c>IsFocused</c> —
        /// which is exactly what the throwaway probe measured on today's code.</para>
        /// </summary>
        [Fact]
        public void ABypassingModalLosesFocusWhenAToolArms()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            // The picker/spawn/zone adapters' shape: registered straight on the arbiter, controller unaware.
            var bypassing = new ProbeGizmo();
            BypassAndArmDirectlyOnGlobal(fx, bypassing);
            Assert.True(bypassing.IsFocused);

            var entityTool = new ProbeGizmo();
            fx.Controller.Register(
                new ToolDescriptor("rotate", "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, entityTool); return ToolActivationOutcome.Armed; });

            Assert.True(fx.Controller.Activate("rotate", entity));

            Assert.True(entityTool.IsFocused);          // the tool owns the interaction
            Assert.False(bypassing.IsFocused);          // 🔴 and the bypassing picker LET GO — the fix
            Assert.True(bypassing.CancelSeen);          // it was told, not silently dropped
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE CONSEQUENCE, asserted directly: one input reaches ONE tool.</b> The probe measured
        /// <c>MouseCount == 1</c> on BOTH before this slice.
        ///
        /// <para>⚠ <c>FdpEventBus</c> is double-buffered — <c>Publish</c> lands in the write buffer and only
        /// becomes readable after <c>SwapBuffers</c> (<c>FdpEventBus.cs:30</c>). ⛔ Omitting the swap makes
        /// both counters read 0, which looks exactly like "something arbitrates". The first version of the
        /// probe hit that; it is recorded here so the next rail does not.</para>
        /// </summary>
        [Fact]
        public void OneInputReachesOnlyTheActiveTool()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            var bypassing = new ProbeGizmo();
            BypassAndArmDirectlyOnGlobal(fx, bypassing);

            var entityTool = new ProbeGizmo();
            fx.Controller.Register(
                new ToolDescriptor("rotate", "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, entityTool); return ToolActivationOutcome.Armed; });
            fx.Controller.Activate("rotate", entity);

            fx.Bus.Publish(new GizmoMouseEvent
            {
                Token = default, Button = MapMouseButton.Left, IsPressed = true, WorldPos = Vector3.Zero,
            });
            fx.Bus.SwapBuffers();

            fx.Global.Execute(_world, 0.016f);
            fx.DataDriven.Execute(_world, 0.016f);

            Assert.Equal(1, entityTool.MouseCount);
            Assert.Equal(0, bypassing.MouseCount);   // 🔴 was 1 before this slice — the duplicate delivery
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE TERMINAL HALF — exactly ONE <c>InputCaptureBinding</c> per frame.</b>
        /// 📐 <c>DebugGizmoLayer.cs:121-135</c> takes the FIRST binding and <c>break</c>s. With two, that
        /// choice looked arbitrary but was in fact DETERMINISTIC (the pack's group order always favours
        /// <c>GlobalGizmoManager</c>, so the entity-scoped tool never got the raw stream). With one, the
        /// <c>break</c> becomes correct by construction.
        /// </summary>
        [Fact]
        public void ExactlyOneInputCaptureBindingPerFrame()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            BypassAndArmDirectlyOnGlobal(fx, new ProbeGizmo());

            fx.Controller.Register(
                new ToolDescriptor("rotate", "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, new ProbeGizmo()); return ToolActivationOutcome.Armed; });
            fx.Controller.Activate("rotate", entity);

            fx.Global.Execute(_world, 0.016f);
            fx.DataDriven.Execute(_world, 0.016f);

            int bindings = fx.Buffer.GetFrame().ToArray()
                .Count(p => p.Shape == DebugPrimitiveShape.InputCaptureBinding);

            Assert.Equal(1, bindings);   // 🔴 was 2 before this slice
        }

        /// <summary>
        /// 🔒 <b><c>R-137</c> — unification may not cost a capability.</b> A modeless tool
        /// (<c>LayerControlGizmo</c>'s shape) must SURVIVE a modal switch. ⭐ This is why
        /// <c>CancelOtherArbiter</c> delegates to <c>CancelInteractiveTools()</c>, which spares permanent
        /// gizmos, rather than clearing the arbiter wholesale.
        /// </summary>
        [Fact]
        public void AModelessToolSurvivesAModalSwitch()
        {
            var fx = new Fixture();

            var modeless = new ProbeGizmo { RequiresExclusiveFocus = false };
            fx.Global.Register(GlobalGizmoManager.NewId(), modeless);

            var entity = _world.CreateEntity();
            fx.Controller.Register(
                new ToolDescriptor("rotate", "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, new ProbeGizmo()); return ToolActivationOutcome.Armed; });
            fx.Controller.Register(
                new ToolDescriptor("measure", "Measure", ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(GlobalGizmoManager.NewId(), new ProbeGizmo()); return ToolActivationOutcome.Armed; });

            fx.Controller.Activate("rotate", entity);
            fx.Controller.Activate("measure");

            Assert.False(modeless.Disposed);    // the layer-control shape is untouched
            Assert.False(modeless.CancelSeen);
        }

        /// <summary>
        /// ⭐ Ruling 49 — a host that cannot service a tool must SAY SO, not fail silently. An unregistered
        /// id reports and returns false; it does not throw and does not pretend.
        /// </summary>
        [Fact]
        public void AnUnknownToolReportsRatherThanFailingSilently()
        {
            string? reported = null;
            var controller = new ToolController(() => null, () => null, msg => reported = msg);

            Assert.False(controller.Activate("no-such-tool"));
            Assert.NotNull(reported);
            Assert.Contains("no-such-tool", reported);
            Assert.Null(controller.ActiveModal);
        }

        /// <summary>
        /// 🔒 <c>Q27</c>: re-activating the current modal is a no-op UNLESS <c>ToggleOnReactivate</c>.
        /// ⭐ <c>Edit</c>/<c>Route</c> carry the flag because they toggled before this slice and must keep
        /// toggling; <c>Rotate</c> does not, because both hosts deliberately re-arm it.
        /// </summary>
        [Fact]
        public void ReactivationTogglesOnlyWhenTheDescriptorSaysSo()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();
            int arms   = 0;

            fx.Controller.Register(
                new ToolDescriptor("edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped,
                                   ToggleOnReactivate: true),
                target => { arms++; fx.DataDriven.ActivateGizmo(target, new ProbeGizmo()); return ToolActivationOutcome.Armed; });

            fx.Controller.Activate("edit", entity);
            Assert.NotNull(fx.Controller.ActiveModal);

            fx.Controller.Activate("edit", entity);          // second press toggles OFF
            Assert.Null(fx.Controller.ActiveModal);
            Assert.Equal(1, arms);                            // it did not re-arm
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>Dismissed</c> is the third outcome, and it is why the delegate is not a
        /// <c>bool</c>.</b> 📐 <c>ToolActivationDrainSystem.ToggleEntityGizmo:176</c> turns <c>Edit</c>/
        /// <c>Route</c> OFF when a gizmo is already injected on that entity. ⛔ Reporting that as
        /// unserviceable would mislead the operator; treating it as armed would leave a dismissed tool on
        /// the stack. ⇒ the controller must succeed, arm NOTHING, and say nothing.
        /// </summary>
        [Fact]
        public void ADismissedActivationSucceedsButArmsNothing()
        {
            string? reported = null;
            var controller = new ToolController(() => null, () => null, msg => reported = msg);

            controller.Register(
                new ToolDescriptor("edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped),
                _ => ToolActivationOutcome.Dismissed);

            Assert.True(controller.Activate("edit"));   // it worked …
            Assert.Null(controller.ActiveModal);         // … and armed nothing
            Assert.Null(reported);                       // ⛔ and did NOT cry unserviceable
        }

        /// <summary>
        /// ⭐ The complement: <c>Unserviceable</c> arms nothing AND leaves the previous modal cleared, so a
        /// failed activation never leaves a half state. ⚠ The activation reports its own reason (the drain's
        /// <c>Unserviceable</c>), so the controller does not double-report here.
        /// </summary>
        [Fact]
        public void AnUnserviceableActivationLeavesNoModalArmed()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            fx.Controller.Register(
                new ToolDescriptor("rotate", "Rotate", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, new ProbeGizmo()); return ToolActivationOutcome.Armed; });
            fx.Controller.Register(
                new ToolDescriptor("measure", "Measure", ToolModality.Modal, ToolArbiter.Global),
                _ => ToolActivationOutcome.Unserviceable);

            fx.Controller.Activate("rotate", entity);
            Assert.NotNull(fx.Controller.ActiveModal);

            Assert.False(fx.Controller.Activate("measure"));
            Assert.Null(fx.Controller.ActiveModal);      // the old one is gone, the new one never armed
        }

        /// <summary>
        /// ⭐ <c>PushModal</c> is declared and NOT built in this slice. It must refuse loudly rather than
        /// quietly behaving like <c>Activate</c> — absent-and-explained beats present-and-broken.
        /// </summary>
        [Fact]
        public void PushModalRefusesLoudlyUntilItIsBuilt()
        {
            var controller = new ToolController(() => null, () => null);
            var ex = Assert.Throws<NotSupportedException>(() => controller.PushModal("anything"));
            Assert.Contains("UXI-07", ex.Message);
        }
    }
}
