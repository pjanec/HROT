using Fdp.Toolkit.Combat.Components;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Hrot.Common.Diagnostics.Gizmos;
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
            // Health is registered during InitializeEmbedded (world.RegisterComponent<Health>).
            // StatelessGizmoRegistry.Register resolves the type via ComponentTypeRegistry.GetId -- must not throw.
            var settings          = new GizmoSettingsRegistry();
            var gizmo             = new HealthBarGizmo(settings);
            var statelessRegistry = new StatelessGizmoRegistry();
            var attr              = typeof(HealthBarGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;

            var ex = Record.Exception(() => statelessRegistry.Register(gizmo, attr.RequiredComponents));

            Assert.Null(ex);
        }
    }
}
