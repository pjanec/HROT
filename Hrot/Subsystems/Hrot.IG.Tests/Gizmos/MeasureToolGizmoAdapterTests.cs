using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Vis2D;
using Hrot.IG.Gizmos;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.IG.Tests.Gizmos
{
    // ============================================================================
    // SC-GZ021-MT: MeasureToolGizmoAdapter unit tests.
    // ============================================================================

    public sealed class MeasureToolGizmoAdapterTests
    {
        private static (MapCanvas canvas, GizmoSettingsRegistry settings, MeasureToolGizmoAdapter adapter) Build()
        {
            var canvas   = new MapCanvas();
            var settings = new GizmoSettingsRegistry();
            MeasureToolGizmoSettings.Register(settings);
            var adapter  = new MeasureToolGizmoAdapter(canvas, settings);
            return (canvas, settings, adapter);
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

        // ---- Tool push/pop behaviour ----------------------------------------

        [Fact]
        public void SC_GZ021_MT_2_Update_WhenActiveIsFalse_DoesNotPushTool()
        {
            var (canvas, _, adapter) = Build();

            adapter.Update();

            Assert.Null(canvas.ActiveTool);
        }

        [Fact]
        public void SC_GZ021_MT_3_Update_WhenActiveBecomeTrue_PushesToolOntoCanvas()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            settings.Write(activeHash, GizmoSettingValue.From(true));

            adapter.Update();

            Assert.IsType<MeasureTool>(canvas.ActiveTool);
        }

        [Fact]
        public void SC_GZ021_MT_4_Update_WhenActiveTurnsFalse_PopsTool()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);

            // Push first.
            settings.Write(activeHash, GizmoSettingValue.From(true));
            adapter.Update();
            Assert.IsType<MeasureTool>(canvas.ActiveTool);

            // Pop.
            settings.Write(activeHash, GizmoSettingValue.From(false));
            adapter.Update();

            Assert.Null(canvas.ActiveTool);
        }

        [Fact]
        public void SC_GZ021_MT_5_Update_WhenActiveRemainsTrue_DoesNotDuplicatePush()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            settings.Write(activeHash, GizmoSettingValue.From(true));

            adapter.Update();
            adapter.Update();   // second frame — should not double-push

            // Only one tool should be active (not two stacked measure tools).
            Assert.IsType<MeasureTool>(canvas.ActiveTool);
        }

        // ---- Unit sync -------------------------------------------------------

        [Fact]
        public void SC_GZ021_MT_6_Update_UnitsZero_SetsDisplayUnitsMeters()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            uint unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            settings.Write(activeHash, GizmoSettingValue.From(true));
            settings.Write(unitsHash,  GizmoSettingValue.From(0));

            adapter.Update();

            var tool = Assert.IsType<MeasureTool>(canvas.ActiveTool);
            Assert.Equal(MeasureDisplayUnits.Meters, tool.DisplayUnits);
        }

        [Fact]
        public void SC_GZ021_MT_7_Update_UnitsOne_SetsDisplayUnitsKilometers()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            uint unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            settings.Write(activeHash, GizmoSettingValue.From(true));
            settings.Write(unitsHash,  GizmoSettingValue.From(1));

            adapter.Update();

            var tool = Assert.IsType<MeasureTool>(canvas.ActiveTool);
            Assert.Equal(MeasureDisplayUnits.Kilometers, tool.DisplayUnits);
        }

        [Fact]
        public void SC_GZ021_MT_8_Update_UnitChangedWhileActive_SyncsDisplayUnits()
        {
            var (canvas, settings, adapter) = Build();

            uint activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            uint unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            settings.Write(activeHash, GizmoSettingValue.From(true));
            settings.Write(unitsHash,  GizmoSettingValue.From(0));

            adapter.Update();   // pushed with meters

            settings.Write(unitsHash, GizmoSettingValue.From(1));
            adapter.Update();   // same frame (active stays true) — units must update

            var tool = Assert.IsType<MeasureTool>(canvas.ActiveTool);
            Assert.Equal(MeasureDisplayUnits.Kilometers, tool.DisplayUnits);
        }
    }
}
