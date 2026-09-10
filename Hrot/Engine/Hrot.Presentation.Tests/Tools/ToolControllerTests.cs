using System;
using System.Collections.Generic;
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
        /// ⭐⭐⭐ <b><c>CE-259q</c> — A TOOL THAT ENDED ITSELF CAN BE ARMED AGAIN IMMEDIATELY.</b>
        /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7h.
        ///
        /// <para>🔴 <b>The operator's report, <c>2026-09-10</c>, verbatim:</b> <i>"i right click again, select
        /// Edit shape again, nothing happens. i need to repeat, second try work. third try does not. fourth
        /// try does."</i> ⇒ ⭐ a DETERMINISTIC 2-cycle, which is why this rail asserts the SECOND arm rather
        /// than just the first.</para>
        ///
        /// <para>📐 <b>The mechanism it pins:</b> <c>VertexEditGizmo</c> self-removes on right-release
        /// (<c>:174-179</c>) and on Escape (<c>:182-189</c>). Before the fix its <c>onRemove</c> told the
        /// ARBITER only, so <c>_modalStack</c> still held <c>(Edit, entity)</c> ⇒ the next <c>Activate</c>
        /// matched the <c>ToggleOnReactivate</c> branch (<c>ToolController.cs:120-126</c>) and CANCELLED.</para>
        ///
        /// <para>⚠ <b>Inverse-edit red-proof:</b> drop <c>controller.NotifyToolEnded(...)</c> from the
        /// <c>onRemove</c> pair and the second <c>Assert.True(armed)</c> FAILS while <c>Activate</c> still
        /// returns <c>true</c> — ⭐ which is exactly why the operator saw "nothing happens" and no error.
        /// ⛔ <c>Activate</c>'s return value is NOT a proof of arming; assert the arbiter.</para>
        /// </summary>
        [Fact]
        public void AToolThatEndedItselfCanBeArmedAgainImmediately()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            // The production shape, ScenarioToolRegistrations.ToggleEntityGizmo: ToggleOnReactivate, and an
            // onRemove that updates BOTH the arbiter and the controller.
            var descriptor = new ToolDescriptor(
                "edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped,
                ToggleOnReactivate: true);

            ProbeGizmo? armed = null;
            fx.Controller.Register(descriptor, target =>
            {
                if (fx.DataDriven.HasInjectedGizmo(target))
                {
                    fx.DataDriven.DeactivateGizmo(target);
                    return ToolActivationOutcome.Dismissed;
                }

                armed = new ProbeGizmo();
                fx.DataDriven.ActivateGizmo(target, armed);
                return ToolActivationOutcome.Armed;
            });

            // ① arm it — the operator's first "Edit Shape"
            fx.Controller.Activate("edit", entity);
            Assert.True(fx.DataDriven.HasInjectedGizmo(entity));

            // ② the gizmo ENDS ITSELF (right-release / Escape). This is the paired onRemove.
            fx.DataDriven.DeactivateGizmo(entity);
            fx.Controller.NotifyToolEnded("edit", entity);

            Assert.False(fx.DataDriven.HasInjectedGizmo(entity));
            Assert.Null(fx.Controller.ActiveModal);   // 🔴 was the stale (Edit, entity) entry

            // ③ arm it again — this is the try that used to do nothing
            fx.Controller.Activate("edit", entity);
            Assert.True(fx.DataDriven.HasInjectedGizmo(entity));   // 🔴 was FALSE: the toggle cancelled
            Assert.Same(descriptor, fx.Controller.ActiveModal);
        }

        /// <summary>
        /// ⛔⛔⛔ <b><c>CE-259q</c> guard — ENDING A TOOL MUST NOT SWEEP THE STACK.</b>
        ///
        /// <para>🔒 The naive fix for the rail above is <c>onRemove: () => tools.Cancel()</c>. ⛔ That is
        /// WRONG and this rail is what forbids it: <c>Cancel</c> unwinds the WHOLE stack by deliberate design
        /// (<c>ToolController.cs:254-267</c>), so a self-removing picker would destroy the tool it had
        /// INTERRUPTED — the <c>PushModal</c> suspend/resume capability an operator confirmed working for the
        /// first time on <c>2026-09-09</c> (the half-drawn route surviving a pick).</para>
        ///
        /// <para>⚠ <b>Red-proof:</b> replace the <c>NotifyToolEnded</c> body with <c>Cancel()</c> and the
        /// <c>Assert.False(underneath.Disposed)</c> FAILS.</para>
        /// </summary>
        [Fact]
        public void AToolEndingItselfResumesWhatItInterruptedInsteadOfDestroyingIt()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            // The tool underneath — the half-drawn route/shape.
            var underneath = new ProbeGizmo();
            fx.Controller.Register(
                new ToolDescriptor("edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped),
                target => { fx.DataDriven.ActivateGizmo(target, underneath); return ToolActivationOutcome.Armed; });
            fx.Controller.Activate("edit", entity);
            Assert.True(underneath.IsFocused);

            // The interrupter — a picker, pushed rather than activated.
            // ⚠⚠ ToolArbiter.Global, on GlobalGizmoManager, because that is what production does:
            //    PickerToolHost.cs:116 registers every picker descriptor as Global and :173 arms it with
            //    g.Register(id, gizmo). 🔴 A first draft of this rail put the picker on the ENTITY-scoped
            //    arbiter keyed by the same entity — and DataDrivenGizmoSystem.ActivateGizmo:90 deactivates
            //    the previous injection for that entity first, so the push DISPOSED the tool underneath
            //    before NotifyToolEnded was ever reached. ⇒ the rail failed for a reason that does not
            //    exist in the product. Mirror the arbiters, not just the gesture.
            var picker = new ProbeGizmo();
            long pickerId = GlobalGizmoManager.NewId();
            fx.Controller.Register(
                new ToolDescriptor("pick", "Pick Entity", ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(pickerId, picker); return ToolActivationOutcome.Armed; });
            fx.Controller.PushModal("pick", entity);

            Assert.False(underneath.IsFocused);   // suspended, NOT disposed — Q27-F
            Assert.False(underneath.Disposed);

            // The picker ends ITSELF, and reports it.
            fx.Global.Unregister(pickerId);
            fx.Controller.NotifyToolEnded("pick", entity);

            Assert.False(underneath.Disposed);    // 🔴 Cancel() would have destroyed it
            Assert.True(underneath.IsFocused);    // ⭐ and it got its focus BACK
        }

        /// <summary>
        /// ⚠ <c>NotifyToolEnded</c> is IDEMPOTENT and tolerates an unknown tool. ⭐ Not defensive padding:
        /// the arbiter sweeps dispose a gizmo without routing through its <c>onRemove</c> today, and entity
        /// death can remove one while suspended ⇒ a second notification for an entry already gone is a
        /// NORMAL event, and it must not pop somebody else's entry.
        /// </summary>
        [Fact]
        public void NotifyToolEndedIsIdempotentAndIgnoresUnknownTools()
        {
            var fx     = new Fixture();
            var entity = _world.CreateEntity();

            var descriptor = new ToolDescriptor(
                "edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped);
            fx.Controller.Register(descriptor,
                target => { fx.DataDriven.ActivateGizmo(target, new ProbeGizmo()); return ToolActivationOutcome.Armed; });

            fx.Controller.Activate("edit", entity);
            Assert.Same(descriptor, fx.Controller.ActiveModal);

            fx.Controller.NotifyToolEnded("never-registered", entity);
            Assert.Same(descriptor, fx.Controller.ActiveModal);   // untouched

            fx.Controller.NotifyToolEnded("edit", _world.CreateEntity());
            Assert.Same(descriptor, fx.Controller.ActiveModal);   // right tool, WRONG target ⇒ untouched

            fx.Controller.NotifyToolEnded("edit", entity);
            Assert.Null(fx.Controller.ActiveModal);

            fx.Controller.NotifyToolEnded("edit", entity);         // again — must not throw or over-pop
            Assert.Null(fx.Controller.ActiveModal);
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
        /// ⭐⭐⭐ <b>THE SAME TOOL ON A DIFFERENT ENTITY RE-TARGETS — it does not toggle off.</b>
        ///
        /// <para>⛔⛔ <b>This rail exists because a descriptor-keyed rule gets it WRONG, and a measurement is
        /// what caught it.</b> 📐 <c>2026-09-09</c>: both arbiters' <c>CancelInteractiveTools()</c> tear the
        /// armed gizmo down BEFORE the next activation runs (<c>DataDrivenGizmoSystem.cs:124-137</c> clears
        /// <b>every</b> injected gizmo), so an activation can no longer see whether it was already armed on
        /// that entity — and <c>ToolActivationDrainSystem.ToggleEntityGizmo:176</c> keys its toggle on
        /// exactly that. ⇒ the CONTROLLER has to remember the target, which is what <see cref="ArmedTool"/>
        /// is for. ⚠ Without it, <c>Edit</c> pressed twice would re-arm instead of toggling — a capability
        /// lost to unification, which <c>R-137</c> forbids.</para>
        ///
        /// <para>⚠ <b>The behaviour this deliberately CHANGES:</b> before <c>UXI-07</c>, Edit on A then Edit
        /// on B left injected gizmos on BOTH. 🔒 <c>Q27</c> ruling C allows one modal per subsystem, and the
        /// second gizmo could never take focus anyway (<c>DataDrivenGizmoSystem.cs:91</c> grants focus on
        /// <c>_focusedGizmo == null</c>) — drawable-but-inert, not a capability. Folded into §4.7.</para>
        /// </summary>
        [Fact]
        public void TheSameToolOnADifferentEntityRetargetsRatherThanToggling()
        {
            var fx = new Fixture();
            var a  = _world.CreateEntity();
            var b  = _world.CreateEntity();

            var armed = new List<Entity>();
            fx.Controller.Register(
                new ToolDescriptor("edit", "Edit Shape", ToolModality.Modal, ToolArbiter.EntityScoped,
                                   ToggleOnReactivate: true),
                target =>
                {
                    armed.Add(target);
                    fx.DataDriven.ActivateGizmo(target, new ProbeGizmo());
                    return ToolActivationOutcome.Armed;
                });

            fx.Controller.Activate("edit", a);
            fx.Controller.Activate("edit", b);                // ⭐ a DIFFERENT entity — re-target

            Assert.Equal(new[] { a, b }, armed);               // it armed twice …
            Assert.Equal(b, fx.Controller.ActiveModalTarget);  // … and B is the live one
            Assert.False(fx.DataDriven.HasInjectedGizmo(a));   // 🔴 A let go — Q27 ruling C

            fx.Controller.Activate("edit", b);                 // now the SAME entity — toggle off
            Assert.Null(fx.Controller.ActiveModal);
            Assert.Equal(2, armed.Count);
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

        // ══ UXI-07 / Q27-F — PushModal: SUSPEND and RESUME ════════════════════════════════════════
        //
        // ⚠⚠ RE-HOMED, not deleted. The old rail here — PushModalRefusesLoudlyUntilItIsBuilt — asserted
        //    that PushModal threw NotSupportedException, whose whole premise ("declared and NOT built")
        //    is now void. ⭐ Its ACTUAL claim was "it must not quietly behave like Activate", and that
        //    claim survives as the two rails below: a push SUSPENDS where Activate DISPOSES, and an
        //    unserviceable push still refuses rather than pretending.

        /// <summary>Registers a Global-arbiter modal that arms <paramref name="gizmo"/>.</summary>
        private static void RegisterGlobalTool(Fixture fx, string id, ProbeGizmo gizmo)
            => fx.Controller.Register(
                new ToolDescriptor(id, id, ToolModality.Modal, ToolArbiter.Global),
                _ => { fx.Global.Register(GlobalGizmoManager.NewId(), gizmo); return ToolActivationOutcome.Armed; });

        /// <summary>
        /// ⭐⭐⭐ <b>THE ONE THAT MATTERS — a push SUSPENDS the tool beneath; it does NOT destroy it.</b>
        /// 🔒 <c>Q27-F</c>: <i>"suspend = <c>SetFocus(false)</c> WITHOUT the <c>Dispose()</c> that both
        /// teardown paths currently pair it with."</i>
        ///
        /// <para>⛔ <b>This is exactly what 4b needs and why 4b could not be built first:</b> converting the
        /// pickers to <c>Activate</c> would have DISPOSED the half-drawn route underneath — a regression,
        /// not a conversion.</para>
        /// </summary>
        [Fact]
        public void PushingAModalSuspendsTheOneBeneathRatherThanDisposingIt()
        {
            var fx     = new Fixture();
            var route  = new ProbeGizmo();
            var picker = new ProbeGizmo();

            RegisterGlobalTool(fx, "route",  route);
            RegisterGlobalTool(fx, "picker", picker);

            Assert.True(fx.Controller.Activate("route"));
            Assert.True(route.IsFocused);

            using (fx.Controller.PushModal("picker"))
            {
                Assert.True(picker.IsFocused);       // the interrupter owns the input …
                Assert.False(route.IsFocused);       // … the route yields focus …
                Assert.False(route.Disposed);        // ⭐⭐ … but is ALIVE. THE WHOLE POINT.
                Assert.Equal(2, fx.Controller.ModalStack.Count);
            }
        }

        /// <summary>
        /// ⭐⭐ <b>Popping RESUMES the tool beneath, and tears the interrupter down.</b>
        /// ⚠ Without the resume half this would be an ordinary cancel with extra steps.
        /// </summary>
        [Fact]
        public void PoppingResumesTheToolBeneathAndDisposesTheInterrupter()
        {
            var fx     = new Fixture();
            var route  = new ProbeGizmo();
            var picker = new ProbeGizmo();

            RegisterGlobalTool(fx, "route",  route);
            RegisterGlobalTool(fx, "picker", picker);

            fx.Controller.Activate("route");
            var scope = fx.Controller.PushModal("picker");
            scope.Dispose();

            Assert.True(picker.Disposed);            // the interruption is over
            Assert.False(route.Disposed);            // the route survived it …
            Assert.True(route.IsFocused);            // ⭐⭐ … and has the input back
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
            Assert.Single(fx.Controller.ModalStack);
        }

        /// <summary>⭐ Disposing twice is a no-op — <c>using</c> plus an explicit dispose must not double-pop.</summary>
        [Fact]
        public void DisposingTheScopeTwiceDoesNotPopTwice()
        {
            var fx     = new Fixture();
            var route  = new ProbeGizmo();
            var picker = new ProbeGizmo();

            RegisterGlobalTool(fx, "route",  route);
            RegisterGlobalTool(fx, "picker", picker);

            fx.Controller.Activate("route");
            var scope = fx.Controller.PushModal("picker");
            scope.Dispose();
            scope.Dispose();

            Assert.Single(fx.Controller.ModalStack);        // ⛔ not zero — the route was not popped too
            Assert.Equal("route", fx.Controller.ActiveModal?.Id);
        }

        /// <summary>
        /// ⭐⭐ <b><c>Activate</c> UNWINDS THE WHOLE STACK.</b> 🔒 It is <i>"a deliberate switch"</i>, so
        /// nothing interrupted is still wanted. ⛔ Popping only the top would strand the suspended tool:
        /// its scope pops BY DEPTH, and that depth now belongs to the new tool ⇒ nothing could ever
        /// resume it.
        /// </summary>
        [Fact]
        public void ADeliberateSwitchUnwindsTheWholeStackRatherThanStrandingSuspendedTools()
        {
            var fx      = new Fixture();
            var route   = new ProbeGizmo();
            var picker  = new ProbeGizmo();
            var measure = new ProbeGizmo();

            RegisterGlobalTool(fx, "route",   route);
            RegisterGlobalTool(fx, "picker",  picker);
            RegisterGlobalTool(fx, "measure", measure);

            fx.Controller.Activate("route");
            fx.Controller.PushModal("picker");
            Assert.Equal(2, fx.Controller.ModalStack.Count);

            Assert.True(fx.Controller.Activate("measure"));

            Assert.Single(fx.Controller.ModalStack);
            Assert.Equal("measure", fx.Controller.ActiveModal?.Id);
            Assert.True(picker.Disposed);
            Assert.True(route.Disposed);             // ⭐ unwound, NOT left suspended forever
            Assert.True(measure.IsFocused);
        }

        /// <summary>
        /// ⭐ The re-homed half of the deleted rail: an unserviceable push still REFUSES — ⛔ it does not
        /// quietly behave like <c>Activate</c>. ⚠ It reports instead of throwing, and returns a usable
        /// handle, because the caller writes <c>using var _ = tools.PushModal(...)</c> and a null or a
        /// throw there turns a reported refusal into a crash.
        /// </summary>
        [Fact]
        public void PushingAnUnknownToolRefusesAndArmsNothing()
        {
            var reports    = new List<string>();
            var controller = new ToolController(() => null, () => null, reports.Add);

            using (controller.PushModal("no.such.tool"))
            {
                Assert.Empty(controller.ModalStack);
            }

            Assert.Contains(reports, r => r.Contains("no.such.tool") && r.Contains("not registered"));
        }

        /// <summary>⭐ A modeless tool has nothing to suspend and nothing to come back to ⇒ refuse, loudly.</summary>
        [Fact]
        public void PushingAModelessToolRefuses()
        {
            var fx      = new Fixture();
            var reports = new List<string>();
            var controller = new ToolController(() => fx.Global, () => fx.DataDriven, reports.Add);

            controller.Register(
                new ToolDescriptor("grid", "Grid", ToolModality.Modeless, ToolArbiter.Global),
                _ => ToolActivationOutcome.Armed);

            using (controller.PushModal("grid"))
            {
                Assert.Empty(controller.ModalStack);
            }

            Assert.Contains(reports, r => r.Contains("modeless"));
        }
    }
}
