using System;
using System.Numerics;
using Hrot.SimHost.Components;
using Hrot.SimHost.Events;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="PerspectiveCoordinatorSystem"/> (MOD1-P4T2).
    ///
    /// Verifies that a <see cref="TogglePerspectiveEvent"/> flips
    /// <see cref="ActivePerspective.Current"/> and snaps the incoming camera
    /// to the outgoing camera's state.
    /// </summary>
    public class PerspectiveCoordinatorTests : IDisposable
    {
        private readonly EntityRepository _world;

        public PerspectiveCoordinatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<ActivePerspective>();
            _world.RegisterEvent<TogglePerspectiveEvent>();
        }

        public void Dispose() => _world.Dispose();

        // ── Helper ────────────────────────────────────────────────────────────

        private static SystemGroup CreateGroup(EntityRepository world)
        {
            var g = new SystemGroup();
            g.Create(world);
            return g;
        }

        // ── Toggle tests ──────────────────────────────────────────────────────

        /// <summary>
        /// Seeding <see cref="PerspectiveType.IG"/> and dispatching a toggle event
        /// must flip <see cref="ActivePerspective.Current"/> to <see cref="PerspectiveType.Sim"/>.
        /// </summary>
        [Fact]
        public void PerspectiveCoordinator_Toggle_FlipsPerspective_IG_To_Sim()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.IG });

            var coordinator = new PerspectiveCoordinatorSystem();
            using var group = CreateGroup(_world);
            group.AddSystem(coordinator);

            // Dispatch toggle.
            var evt = new TogglePerspectiveEvent();
            _world.Bus.Publish(evt);
            _world.Bus.SwapBuffers();

            group.Run();

            var result = _world.GetSingletonUnmanaged<ActivePerspective>();
            Assert.Equal(PerspectiveType.Sim, result.Current);
        }

        /// <summary>
        /// Seeding <see cref="PerspectiveType.Sim"/> and dispatching a toggle event
        /// must flip <see cref="ActivePerspective.Current"/> to <see cref="PerspectiveType.IG"/>.
        /// </summary>
        [Fact]
        public void PerspectiveCoordinator_Toggle_FlipsPerspective_Sim_To_IG()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var coordinator = new PerspectiveCoordinatorSystem();
            using var group = CreateGroup(_world);
            group.AddSystem(coordinator);

            var evt = new TogglePerspectiveEvent();
            _world.Bus.Publish(evt);
            _world.Bus.SwapBuffers();

            group.Run();

            var result = _world.GetSingletonUnmanaged<ActivePerspective>();
            Assert.Equal(PerspectiveType.IG, result.Current);
        }

        /// <summary>
        /// Without a toggle event the perspective must remain unchanged.
        /// </summary>
        [Fact]
        public void PerspectiveCoordinator_NoEvent_PerspectiveUnchanged()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var coordinator = new PerspectiveCoordinatorSystem();
            using var group = CreateGroup(_world);
            group.AddSystem(coordinator);

            // No event published.
            group.Run();

            var result = _world.GetSingletonUnmanaged<ActivePerspective>();
            Assert.Equal(PerspectiveType.Sim, result.Current);
        }

        // ── Camera snap tests ─────────────────────────────────────────────────

        /// <summary>
        /// On a toggle from IG → Sim, the Sim camera's Target and Zoom must match
        /// the IG camera's Target and Zoom after the coordinator ticks
        /// (verifies <see cref="MapCamera.SnapTo"/> call path).
        /// </summary>
        [Fact]
        public void PerspectiveCoordinator_Toggle_SnapsCamera_FromIG_ToSim()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.IG });

            // Configure the outgoing (IG) camera with a known state.
            var igModule  = new IgPresentationModule(canvas: null);
            var simModule = new SimPresentationModule(canvas: null);

            igModule.GetCamera().Target = new Vector2(100f, 200f);
            igModule.GetCamera().Zoom   = 2.5f;

            var coordinator = new PerspectiveCoordinatorSystem(
                igCameraProvider:  igModule,
                simCameraProvider: simModule);

            using var group = CreateGroup(_world);
            group.AddSystem(coordinator);

            var evt = new TogglePerspectiveEvent();
            _world.Bus.Publish(evt);
            _world.Bus.SwapBuffers();
            group.Run();

            // After toggle, the incoming (Sim) camera should have snapped to IG's state.
            Assert.Equal(new Vector2(100f, 200f), simModule.GetCamera().Target);
            Assert.Equal(2.5f, simModule.GetCamera().Zoom, precision: 3);
        }

        /// <summary>
        /// On a toggle from Sim → IG, the IG camera's Target and Zoom must match
        /// the Sim camera's Target and Zoom.
        /// </summary>
        [Fact]
        public void PerspectiveCoordinator_Toggle_SnapsCamera_FromSim_ToIG()
        {
            _world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim });

            var igModule  = new IgPresentationModule(canvas: null);
            var simModule = new SimPresentationModule(canvas: null);

            // Configure outgoing (Sim) camera.
            simModule.GetCamera().Target = new Vector2(500f, 300f);
            simModule.GetCamera().Zoom   = 1.2f;

            var coordinator = new PerspectiveCoordinatorSystem(
                igCameraProvider:  igModule,
                simCameraProvider: simModule);

            using var group = CreateGroup(_world);
            group.AddSystem(coordinator);

            var evt = new TogglePerspectiveEvent();
            _world.Bus.Publish(evt);
            _world.Bus.SwapBuffers();
            group.Run();

            // Incoming (IG) camera must match outgoing (Sim) camera state.
            Assert.Equal(new Vector2(500f, 300f), igModule.GetCamera().Target);
            Assert.Equal(1.2f, igModule.GetCamera().Zoom, precision: 3);
        }
    }
}
