using System;
using System.Numerics;
using Bagira.SimHost.Components;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using Xunit;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="IgPresentationModule"/> and <see cref="SimPresentationModule"/>
    /// (MOD1-P4T1).
    ///
    /// Both modules must register their render systems and gate <c>Draw</c> calls
    /// on <see cref="ActivePerspective.Current"/>.
    /// </summary>
    public class PresentationModuleTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PresentationModuleTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<ActivePerspective>();
        }

        public void Dispose() => _world.Dispose();

        // ── Helper ────────────────────────────────────────────────────────────

        private static SystemGroup CreateGroup(EntityRepository world)
        {
            var g = new SystemGroup();
            g.Create(world);
            return g;
        }

        // ── IgPresentationModule tests ─────────────────────────────────────────

        /// <summary>
        /// When <see cref="ActivePerspective.Current"/> is <see cref="PerspectiveType.Sim"/>,
        /// ticking the <see cref="IgMapRenderSystem"/> must NOT call Draw (verified
        /// via <see cref="IgMapRenderSystem.DrawCallCount"/> == 0).
        /// </summary>
        [Fact]
        public void IgPresentationModule_DoesNotDraw_WhenSimPerspectiveActive()
        {
            // Seed perspective: Sim is active.
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var module = new IgPresentationModule(canvas: null);   // null = no Raylib draw
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            // Act: tick presentation group.
            group.Run();

            // Assert: no draw should have occurred.
            Assert.Equal(0, module.RenderSystem.DrawCallCount);
        }

        /// <summary>
        /// When <see cref="ActivePerspective.Current"/> is <see cref="PerspectiveType.IG"/>,
        /// ticking the <see cref="IgMapRenderSystem"/> MUST call Draw once.
        /// </summary>
        [Fact]
        public void IgPresentationModule_Draws_WhenIgPerspectiveActive()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.IG });

            var module = new IgPresentationModule(canvas: null);
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            group.Run();

            Assert.Equal(1, module.RenderSystem.DrawCallCount);
        }

        // ── SimPresentationModule tests ────────────────────────────────────────

        /// <summary>
        /// When <see cref="ActivePerspective.Current"/> is <see cref="PerspectiveType.Sim"/>,
        /// ticking the <see cref="SimMapRenderSystem"/> MUST call Draw once.
        /// </summary>
        [Fact]
        public void SimPresentationModule_DrawsCalled_WhenSimPerspectiveActive()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var module = new SimPresentationModule(canvas: null);
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            group.Run();

            Assert.Equal(1, module.RenderSystem.DrawCallCount);
        }

        /// <summary>
        /// When <see cref="ActivePerspective.Current"/> is <see cref="PerspectiveType.IG"/>,
        /// ticking the <see cref="SimMapRenderSystem"/> must NOT call Draw.
        /// </summary>
        [Fact]
        public void SimPresentationModule_DoesNotDraw_WhenIgPerspectiveActive()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.IG });

            var module = new SimPresentationModule(canvas: null);
            using var group = CreateGroup(_world);
            module.RegisterSystems(group);

            group.Run();

            Assert.Equal(0, module.RenderSystem.DrawCallCount);
        }

        // ── Module build tests ─────────────────────────────────────────────────

        /// <summary>
        /// Both modules must register exactly one system each into the presentation group.
        /// </summary>
        [Fact]
        public void BothModules_RegisterOneSystemEach_InPresentationGroup()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var igModule  = new IgPresentationModule(canvas: null);
            var simModule = new SimPresentationModule(canvas: null);
            using var group = CreateGroup(_world);

            igModule.RegisterSystems(group);
            simModule.RegisterSystems(group);

            var systems = group.GetSystems();
            Assert.Equal(2, systems.Count);
            Assert.Contains(systems, s => s is IgMapRenderSystem);
            Assert.Contains(systems, s => s is SimMapRenderSystem);
        }
    }
}
