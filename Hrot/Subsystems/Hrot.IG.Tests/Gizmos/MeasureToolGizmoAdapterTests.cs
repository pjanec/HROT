using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.IG.Gizmos;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-MT: MeasureToolGizmoAdapter unit tests.
    // ============================================================================

    public sealed class MeasureToolGizmoAdapterTests
    {
        private static (GlobalGizmoManager manager, GizmoSettingsRegistry settings, MeasureToolGizmoAdapter adapter) Build()
        {
            var buffer   = new DebugPrimitiveBuffer();
            var manager  = new GlobalGizmoManager(buffer);
            var settings = new GizmoSettingsRegistry();
            MeasureToolGizmoSettings.Register(settings);
            var adapter  = new MeasureToolGizmoAdapter(manager, settings);
            return (manager, settings, adapter);
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

        // ---- Gizmo register/unregister behaviour ---------------------------

        [Fact]
        public void SC_GZ021_MT_2_Update_WhenActiveIsFalse_DoesNotRegisterGizmo()
        {
            var (manager, _, adapter) = Build();

            adapter.Update();

            Assert.Equal(0, manager.ActiveCount);
        }

        [Fact]
        public void SC_GZ021_MT_3_Update_WhenActiveBecomeTrue_RegistersGizmoWithManager()
        {
            var (manager, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            settings.Write(activeHash, GizmoSettingValue.From(true));

            adapter.Update();

            Assert.Equal(1, manager.ActiveCount);
            Assert.IsType<MeasureGizmo>(adapter.TestHook_ActiveGizmo);
        }

        [Fact]
        public void SC_GZ021_MT_4_Update_WhenActiveTurnsFalse_UnregistersGizmo()
        {
            var (manager, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);

            // Register first.
            settings.Write(activeHash, GizmoSettingValue.From(true));
            adapter.Update();
            Assert.Equal(1, manager.ActiveCount);

            // Unregister.
            settings.Write(activeHash, GizmoSettingValue.From(false));
            adapter.Update();

            Assert.Equal(0, manager.ActiveCount);
        }

        [Fact]
        public void SC_GZ021_MT_5_Update_WhenActiveRemainsTrue_DoesNotDuplicateRegister()
        {
            var (manager, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            settings.Write(activeHash, GizmoSettingValue.From(true));

            adapter.Update();
            adapter.Update();   // second frame -- should not double-register

            Assert.Equal(1, manager.ActiveCount);
        }

        // ---- Unit sync -------------------------------------------------------

        [Fact]
        public void SC_GZ021_MT_6_Update_UnitsZero_SetsDisplayUnitsMeters()
        {
            var (_, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            uint unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            settings.Write(activeHash, GizmoSettingValue.From(true));
            settings.Write(unitsHash,  GizmoSettingValue.From(0));

            adapter.Update();

            Assert.Equal(MeasureDisplayUnits.Meters, adapter.TestHook_ActiveGizmo!.DisplayUnits);
        }

        [Fact]
        public void SC_GZ021_MT_7_Update_UnitsOne_SetsDisplayUnitsKilometers()
        {
            var (_, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            uint unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            settings.Write(activeHash, GizmoSettingValue.From(true));
            settings.Write(unitsHash,  GizmoSettingValue.From(1));

            adapter.Update();

            Assert.Equal(MeasureDisplayUnits.Kilometers, adapter.TestHook_ActiveGizmo!.DisplayUnits);
        }
    }
}
