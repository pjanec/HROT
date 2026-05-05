using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.IG.Gizmos;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    /// <summary>
    /// SC-GZ020: Verifies that IgApplication correctly wires the gizmo subsystem
    /// after InitializeEmbedded (headless mode).
    /// </summary>
    public sealed class GizmoRendererWiringTests : System.IDisposable
    {
        private readonly IgApplication _app;

        public GizmoRendererWiringTests()
        {
            _app = new IgApplication();
            _app.InitializeEmbedded(headless: true, domainIdOverride: 231);
        }

        public void Dispose() => _app.Dispose();

        [Fact]
        public void SC_GZ020_1_GizmoRegistry_IsNotNull_AfterInit()
        {
            Assert.NotNull(_app.GizmoRegistry);
        }

        [Fact]
        public void SC_GZ020_2_GizmoBuffer_IsNotNull_AfterInit()
        {
            Assert.NotNull(_app.GizmoBuffer);
        }

        [Fact]
        public void SC_GZ020_3_RegisterHealthBarGizmo_DoesNotThrow()
        {
            // IgHealthState is registered during InitializeEmbedded (world.RegisterComponent<IgHealthState>).
            // GizmoRegistry.Register resolves the type via ComponentTypeRegistry.GetId — must not throw.
            var settings = new GizmoSettingsRegistry();
            var def = new HealthBarGizmoDefinition(settings);

            var ex = Record.Exception(() => _app.GizmoRegistry!.Register(def));

            Assert.Null(ex);
        }
    }
}
