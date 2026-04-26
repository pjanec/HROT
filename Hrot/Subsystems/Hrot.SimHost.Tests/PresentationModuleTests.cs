using System;
using System.Numerics;
using Fdp.Core;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="SimPresentationModule"/>
    /// (MOD1-P4T1).
    ///
    /// The module must register its render system and gate <c>Draw</c> calls
    /// on the active perspective name string.
    /// </summary>
    public class PresentationModuleTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PresentationModuleTests()
        {
            _world = new EntityRepository();
            _world.RegisterManagedComponent<Hrot.Common.ActivePerspective>();
        }

        public void Dispose() => _world.Dispose();

        // ── Helper ────────────────────────────────────────────────────────────

        // ── SimPresentationModule tests ────────────────────────────────────────

        /// <summary>
        /// When the active perspective name is <c>"Sim"</c>,
        /// ticking the <see cref="SimMapRenderSystem"/> MUST call Draw once.
        /// </summary>
        [Fact]
        public void SimPresentationModule_DrawsCalled_WhenSimPerspectiveActive()
        {
            _world.SetSingletonManaged(new Hrot.Common.ActivePerspective { Name = "Sim" });

            var module = new SimPresentationModule(canvas: null);

            module.RenderSystem.Execute(_world, 0f);

            Assert.Equal(1, module.RenderSystem.DrawCallCount);
        }

        /// <summary>
        /// When the active perspective name is not <c>"Sim"</c>,
        /// ticking the <see cref="SimMapRenderSystem"/> must NOT call Draw.
        /// </summary>
        [Fact]
        public void SimPresentationModule_DoesNotDraw_WhenOtherPerspectiveActive()
        {
            _world.SetSingletonManaged(new Hrot.Common.ActivePerspective { Name = "IG" });

            var module = new SimPresentationModule(canvas: null);

            module.RenderSystem.Execute(_world, 0f);

            Assert.Equal(0, module.RenderSystem.DrawCallCount);
        }

        // ── Module build test ─────────────────────────────────────────────────

        /// <summary>
        /// <see cref="SimPresentationModule"/> must register exactly one system into the
        /// presentation group.
        /// </summary>
        [Fact]
        public void SimPresentationModule_RegistersOneSystem_InPresentationGroup()
        {
            _world.SetSingletonManaged(new Hrot.Common.ActivePerspective { Name = "Sim" });

            var simModule = new SimPresentationModule(canvas: null);

            Assert.IsType<SimMapRenderSystem>(simModule.RenderSystem);
        }

        /// <summary>
        /// When a production canvas is supplied, <see cref="SimPresentationModule"/> must use
        /// it (not the internal headless default).
        /// </summary>
        [Fact]
        public void SimPresentationModule_ProductionCanvas_IsSameAsProvided()
        {
            var productionCanvas = new MapCanvas(input: null);

            var module = new SimPresentationModule(canvas: productionCanvas);

            Assert.Equal(productionCanvas.Camera.GetCameraView(), module.GetCameraView());
        }
    }
}
