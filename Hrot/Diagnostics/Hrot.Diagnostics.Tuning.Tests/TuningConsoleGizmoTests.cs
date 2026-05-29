using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Diagnostics.Tuning;
using Hrot.Diagnostics.Tuning.Gizmos;
using Xunit;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.Diagnostics.Tuning.Tests
{
    // Stub that counts DrawMainMenuBinding and EmitRaw calls separately.
    // The abstract draw methods increment OtherCount so none are forgotten.
    internal sealed class TuningDrawBuilder : IGizmoDrawBuilder
    {
        public int MainMenuCount;
        public int EmitRawCount;
        public int OtherCount;

        // Override default no-op to count invocations.
        public void DrawMainMenuBinding(string menuJson) => MainMenuCount++;
        public void EmitRaw(in DebugPrimitive prim)      => EmitRawCount++;

        public void DrawLine(
            Vector3 start, Vector3 end, Rgba32 color,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid) => OtherCount++;

        public void DrawLineGradient(
            Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor,
            float thickness = 1f,
            SizeMode sizeMode = SizeMode.ScreenPixels,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            LineStyle style = LineStyle.Solid) => OtherCount++;

        public void DrawSphere(
            Vector3 center, float radius, Rgba32 color,
            float thickness = 0f,
            SizeMode sizeMode = SizeMode.WorldMeters,
            PipelineTarget target = PipelineTarget.All,
            byte layer = 0,
            Rgba32 fillColor = default,
            LineStyle style = LineStyle.Solid) => OtherCount++;

        public void DrawArrow(
            Vector3 from, Vector3 to, Rgba32 color,
            float headSize = 1f,
            byte layer = 0) => OtherCount++;

        public void DrawText(
            float x, float y, FixedString32 text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0) => OtherCount++;

        public void DrawTextLong(
            float x, float y, string text, Rgba32 color,
            CoordinateSpace space = CoordinateSpace.World,
            byte layer = 0) => OtherCount++;
    }

    public sealed class TuningConsoleGizmoTests
    {
        private static TuningRegistry MakeRegistry() => new TuningRegistry();

        [Fact]
        public void UpdateAndDraw_AlwaysEmitsMainMenuBinding()
        {
            var gizmo = new TuningConsoleGizmo(MakeRegistry());
            var draw  = new TuningDrawBuilder();

            gizmo.UpdateAndDraw(0f, draw);

            Assert.Equal(1, draw.MainMenuCount);
        }

        [Fact]
        public void UpdateAndDraw_NotEditing_NoStructInspector()
        {
            var gizmo = new TuningConsoleGizmo(MakeRegistry());
            var draw  = new TuningDrawBuilder();

            gizmo.UpdateAndDraw(0f, draw);

            Assert.Equal(0, draw.EmitRawCount);
        }

        [Fact]
        public void OnStructUpdate_ValidJson_AppliesValueAfterBeginFrame()
        {
            float val = 0f;
            var reg = new TuningRegistry();
            var key = new TuningKey("ui.param");
            reg.Register(key, new Tunable
            {
                Kind  = TuningKind.Float,
                Min   = 0f,
                Max   = 10f,
                Read  = () => val,
                Write = v => val = v,
            });
            var gizmo = new TuningConsoleGizmo(reg);

            gizmo.OnStructUpdate("{\"ui.param\":7.5}");
            reg.BeginFrame();

            Assert.Equal(7.5f, val, 4);
        }

        [Fact]
        public void OnStructUpdate_EmptyJson_DoesNotThrow()
        {
            var gizmo = new TuningConsoleGizmo(MakeRegistry());
            // Must not throw on empty / whitespace-only input.
            gizmo.OnStructUpdate(string.Empty);
            gizmo.OnStructUpdate("   ");
        }

        [Fact]
        public void OnStructUpdate_InvalidJson_DoesNotThrow()
        {
            var gizmo = new TuningConsoleGizmo(MakeRegistry());
            // Must not propagate JSON parsing exceptions to the caller.
            gizmo.OnStructUpdate("{not valid json}");
        }

        [Fact]
        public void OnMenuAction_OpenActionId_TogglesEditing()
        {
            var gizmo = new TuningConsoleGizmo(MakeRegistry());
            var draw  = new TuningDrawBuilder();

            // Before toggle: no StructInspector emitted.
            gizmo.UpdateAndDraw(0f, draw);
            Assert.Equal(0, draw.EmitRawCount);

            // Toggle on via menu action.
            gizmo.OnMenuAction(TuningConsoleGizmo.OpenActionId);
            gizmo.UpdateAndDraw(0f, draw);
            Assert.Equal(1, draw.EmitRawCount);

            // Toggle off via menu action.
            gizmo.OnMenuAction(TuningConsoleGizmo.OpenActionId);
            gizmo.UpdateAndDraw(0f, draw);
            // EmitRawCount must still be 1 (no new emission).
            Assert.Equal(1, draw.EmitRawCount);
        }
    }
}
