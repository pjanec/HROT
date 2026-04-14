using System;
using System.Numerics;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
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

        private static SystemGroup CreateGroup(EntityRepository world)
        {
            var g = new SystemGroup();
            g.Create(world);
            return g;
        }

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
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            group.Run();

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
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            group.Run();

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
            using var group = CreateGroup(_world);

            simModule.RegisterSystems(group);

            var systems = group.GetSystems();
            Assert.Single(systems);
            Assert.Contains(systems, s => s is SimMapRenderSystem);
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
