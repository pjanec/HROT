using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Eqs
{
    // T-VIS1 through T-VIS5: unit tests for EQS visualizer classes (TASK-EQS-022).
    public sealed class EqsVisualizersTests
    {
        // T-VIS1: EqsSensorGizmo must carry the [GizmoProjector] attribute
        // referencing both SimTransform and EqsSensor as required components.
        [Fact]
        public void EqsSensorGizmo_HasGizmoProjectorAttribute_WithCorrectTypes()
        {
            var attr = typeof(EqsSensorGizmo)
                .GetCustomAttribute<GizmoProjectorAttribute>();
            Assert.NotNull(attr);
            Assert.Contains(typeof(SimTransform), attr.RequiredComponents);
            Assert.Contains(typeof(EqsSensor),    attr.RequiredComponents);
        }

        // T-VIS2: EqsCognitiveBufferRenderer must carry [ImGuiRenderer] targeting
        // EqsCognitiveBuffer so the registry auto-discovers it.
        [Fact]
        public void EqsCognitiveBufferRenderer_HasImGuiRendererAttribute_ForCognitiveBuffer()
        {
            var attrs = typeof(EqsCognitiveBufferRenderer)
                .GetCustomAttributes<ImGuiRendererAttribute>();
            Assert.Contains(attrs, a => a.TargetType == typeof(EqsCognitiveBuffer));
        }

        // T-VIS3: GetSummary returns a "Ready" string containing the candidate count
        // when LastUpdateTick > 0 (IsReady == true).
        [Fact]
        public void EqsCognitiveBufferRenderer_GetSummary_ReadyBuffer_ReturnsCorrectString()
        {
            var renderer = new EqsCognitiveBufferRenderer();
            // IsReady is a computed property: LastUpdateTick > 0.
            var buffer   = new EqsCognitiveBuffer { Count = 3, LastUpdateTick = 1 };
            var summary  = renderer.GetSummary(buffer);
            Assert.NotNull(summary);
            Assert.Contains("3",     summary);
            Assert.Contains("Ready", summary);
        }

        // T-VIS4: GetSummary returns an "Awaiting" string when the buffer is not ready
        // (LastUpdateTick == 0).
        [Fact]
        public void EqsCognitiveBufferRenderer_GetSummary_NotReady_ReturnsAwaitingString()
        {
            var renderer = new EqsCognitiveBufferRenderer();
            // LastUpdateTick == 0 means IsReady == false.
            var buffer   = new EqsCognitiveBuffer { Count = 0, LastUpdateTick = 0 };
            var summary  = renderer.GetSummary(buffer);
            Assert.NotNull(summary);
            Assert.Contains("Awaiting", summary);
        }

        // T-VIS5: The three EqsGizmoSettings key strings must produce distinct FNV-1a hashes
        // so settings lookups never collide.
        [Fact]
        public void EqsGizmoSettings_KeyHashes_AreDistinct()
        {
            uint h1 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowRadius);
            uint h2 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowCandidates);
            uint h3 = GizmoSettingsRegistry.ComputeHash(EqsGizmoSettings.ShowScores);
            Assert.NotEqual(h1, h2);
            Assert.NotEqual(h2, h3);
            Assert.NotEqual(h1, h3);
        }
    }
}
