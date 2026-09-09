using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.IG.Gizmos;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-MT: MeasureToolGizmoAdapter unit tests.
    //
    // ⭐⭐⭐ RE-HOMED for UXI-07 step 4a (docs/UX/UX_Feature_Tool_Model.md §4.9c).
    //
    // 🔴 The adapter no longer OWNS a MeasureGizmo — it was a second implementation of Measure next to
    //    the shared ScenarioToolRegistrations arm (ruling 9), and it armed straight on GlobalGizmoManager,
    //    bypassing the arbiter. ⇒ TestHook_ActiveGizmo is gone.
    //
    // ⚠⚠ Each claim was RE-HOMED to its new owner rather than mechanically renamed (the HN-037 lesson):
    //      "a gizmo is registered"  ⇒ still asserted on the manager, but it is now the SHARED arm's gizmo,
    //                                 so the tests also assert WHO armed it (controller.ActiveModal).
    //      "units reach the gizmo"  ⇒ the adapter no longer pushes them; it EXPOSES them as a pull source
    //                                 (ReadUnits), which is what MapInteractionContext.MeasureUnits hands
    //                                 to the one gizmo. The claim is now about that source.
    // ============================================================================

    public sealed class MeasureToolGizmoAdapterTests
    {
        private sealed class Harness
        {
            public GlobalGizmoManager    Manager    = null!;
            public GizmoSettingsRegistry Settings   = null!;
            public ToolController        Controller = null!;
            public MeasureToolGizmoAdapter Adapter  = null!;

            public uint ActiveHash => GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            public uint UnitsHash  => GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            public void SetActive(bool v) => Settings.Write(ActiveHash, GizmoSettingValue.From(v));
            public void SetUnits(int v)   => Settings.Write(UnitsHash,  GizmoSettingValue.From(v));
            public bool ReadActive()      => Settings.Read(ActiveHash).BoolValue;
        }

        /// <summary>
        /// ⭐⭐ Builds the PRODUCTION registration path — <see cref="ScenarioToolRegistrations.RegisterAll"/>
        /// — rather than a hand-rolled tool set, so these rails exercise the arm IG actually gets.
        /// ⛔ A hand-registered Measure would pass even if the shared arm were broken.
        /// </summary>
        private static Harness Build(bool withArbiter = true)
        {
            var h = new Harness
            {
                Manager  = new GlobalGizmoManager(new DebugPrimitiveBuffer()),
                Settings = new GizmoSettingsRegistry(),
            };
            MeasureToolGizmoSettings.Register(h.Settings);

            if (withArbiter)
            {
                h.Controller = new ToolController(() => h.Manager, () => null);
                ScenarioToolRegistrations.RegisterAll(
                    h.Controller,
                    world:              () => null,
                    gizmos:             () => null,
                    globalGizmos:       () => h.Manager,
                    // ⭐ a no-op so Spawn is SERVICEABLE — MT_8 needs a second Global tool to arm.
                    startPlacementMode: () => { },
                    measureUnits:       () => h.Adapter.ReadUnits());
            }

            h.Adapter = new MeasureToolGizmoAdapter(
                h.Manager, h.Settings, withArbiter ? h.Controller : null);
            return h;
        }

        // ---- Settings registration ----------------------------------------

        [Fact]
        public void SC_GZ021_MT_1_Register_RegistersActiveAndUnitsSetting()
        {
            var settings = new GizmoSettingsRegistry();
            MeasureToolGizmoSettings.Register(settings);

            // Active defaults to false.
            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            Assert.False(settings.Read(activeHash).BoolValue);

            // Units defaults to 0 (meters).
            uint unitsHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);
            Assert.Equal(0, settings.Read(unitsHash).IntValue);
        }

        // ---- Arm / disarm behaviour ----------------------------------------

        [Fact]
        public void SC_GZ021_MT_2_Update_WhenActiveIsFalse_ArmsNothing()
        {
            var h = Build();

            h.Adapter.Update();

            Assert.Equal(0, h.Manager.ActiveCount);
            Assert.Null(h.Controller.ActiveModal);
        }

        [Fact]
        public void SC_GZ021_MT_3_Update_WhenActiveBecomesTrue_ArmsMeasureThroughTheArbiter()
        {
            var h = Build();
            h.SetActive(true);

            h.Adapter.Update();

            // ⭐ The gizmo exists (the original claim) …
            Assert.Equal(1, h.Manager.ActiveCount);
            // … ⭐⭐ and the ARBITER is what armed it — the half the old rail could not see, and the half
            //    that makes this a fix rather than a rename. 🔴 Before, the adapter registered its own
            //    gizmo while ActiveModal stayed null, which IS the bypass (§4.8).
            Assert.Equal(ScenarioToolIds.Measure, h.Controller.ActiveModal?.Id);
        }

        [Fact]
        public void SC_GZ021_MT_4_Update_WhenActiveTurnsFalse_CancelsThroughTheArbiter()
        {
            var h = Build();

            h.SetActive(true);
            h.Adapter.Update();
            Assert.Equal(1, h.Manager.ActiveCount);

            h.SetActive(false);
            h.Adapter.Update();

            Assert.Equal(0, h.Manager.ActiveCount);
            Assert.Null(h.Controller.ActiveModal);
        }

        [Fact]
        public void SC_GZ021_MT_5_Update_WhenActiveRemainsTrue_DoesNotDuplicateRegister()
        {
            var h = Build();
            h.SetActive(true);

            h.Adapter.Update();
            h.Adapter.Update();   // second frame -- must not arm a second gizmo

            Assert.Equal(1, h.Manager.ActiveCount);
        }

        // ---- Units -----------------------------------------------------------
        //
        // ⚠ RE-HOMED: the adapter used to PUSH units onto a gizmo it owned. It now EXPOSES them, and the
        //   pack hands that source to the one shared gizmo (MapInteractionContext.MeasureUnits). ⇒ the
        //   claim these two rails carry is "the setting is read correctly", which is what IG contributes.

        [Fact]
        public void SC_GZ021_MT_6_UnitsZero_ReadsAsMeters()
        {
            var h = Build();
            h.SetUnits(0);

            Assert.Equal(MeasureDisplayUnits.Meters, h.Adapter.ReadUnits());
        }

        [Fact]
        public void SC_GZ021_MT_7_UnitsOne_ReadsAsKilometers()
        {
            var h = Build();
            h.SetUnits(1);

            Assert.Equal(MeasureDisplayUnits.Kilometers, h.Adapter.ReadUnits());
        }

        // ---- UXI-07 step 4a — the two NEW claims ------------------------------

        /// <summary>
        /// 🔴🔴 <b>THE DEAD-TOGGLE DEFECT, pinned.</b> 📐 <c>MeasureGizmo.RequiresExclusiveFocus</c> is true,
        /// so arming any other tool sweeps it through <c>CancelInteractiveTools()</c> — which calls
        /// <c>OnCancel()</c> and <c>Dispose()</c>, ⛔ <b>both EMPTY on MeasureGizmo</b>. ⇒ the old adapter
        /// never learned, its <c>_wasActive</c> stayed true, and Measure was <b>dead until the operator
        /// cycled the setting</b>. ⭐ The checkbox must now follow the arbiter back down.
        /// </summary>
        [Fact]
        public void SC_GZ021_MT_8_WhenAnotherToolDisplacesMeasure_TheSettingFollowsItDown()
        {
            var h = Build();
            h.SetActive(true);
            h.Adapter.Update();
            Assert.Equal(ScenarioToolIds.Measure, h.Controller.ActiveModal?.Id);

            // Something else takes the modal slot — exactly what the toolbar does.
            h.Controller.Activate(ScenarioToolIds.Spawn);

            Assert.Equal(ScenarioToolIds.Spawn, h.Controller.ActiveModal?.Id);
            Assert.False(h.ReadActive());

            // ⭐⭐ And the toggle is NOT dead: switching it back on re-arms. 🔴 This is the assertion that
            //    would have failed before the fix — the old adapter's _wasActive was still true, so this
            //    edge read as "no change" and nothing armed.
            h.SetActive(true);
            h.Adapter.Update();
            Assert.Equal(ScenarioToolIds.Measure, h.Controller.ActiveModal?.Id);
        }

        /// <summary>
        /// ⭐⭐ <b>The bypass is GONE by construction.</b> With no arbiter the adapter arms NOTHING — ⛔ it
        /// no longer has a private path to <c>GlobalGizmoManager</c>. 🔒 That is the whole point of step 4a:
        /// one implementation of Measure, reached one way.
        /// </summary>
        [Fact]
        public void SC_GZ021_MT_9_WithNoArbiter_TheAdapterRegistersNothingItself()
        {
            var h = Build(withArbiter: false);
            h.SetActive(true);

            h.Adapter.Update();

            Assert.Equal(0, h.Manager.ActiveCount);
        }
    }
}
