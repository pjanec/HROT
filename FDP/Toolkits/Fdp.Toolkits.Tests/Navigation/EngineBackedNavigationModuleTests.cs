using System;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.EngineBacked;
using Moq;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for <see cref="EngineBackedNavigationModule"/> (module registration)
    /// and <see cref="EngineBackedPathResponseSystem"/> (NAV-P6-T5).
    /// </summary>
    public sealed class EngineBackedNavigationModuleTests : IDisposable
    {
        private readonly TrajectoryPoolManager _pool;

        public EngineBackedNavigationModuleTests()
        {
            _pool = new TrajectoryPoolManager();
        }

        public void Dispose()
        {
            _pool.Dispose();
        }

        // ── Module registration helpers ───────────────────────────────────────────

        private static EntityRepository CreateMinimalRepo()
        {
            return new EntityRepository();
        }

        private EngineBackedNavigationModule CreateModule()
        {
            return new EngineBackedNavigationModule(default(RoadNetworkBlob), _pool);
        }

        // ── Module registration tests ─────────────────────────────────────────────

        [Fact]
        public void RegisterProviders_WithoutPriorProviders_Succeeds()
        {
            var repo   = CreateMinimalRepo();
            var module = CreateModule();
            var mockReg = new Mock<ISystemRegistry>();
            module.RegisterSystems(mockReg.Object);

            // Should not throw.
            module.RegisterProviders(repo);
        }

        [Fact]
        public void RegisterProviders_WithExistingProvider_Throws()
        {
            var repo   = CreateMinimalRepo();
            var module = CreateModule();
            var mockReg = new Mock<ISystemRegistry>();
            module.RegisterSystems(mockReg.Object);
            module.RegisterProviders(repo);

            // Second module tries to register again.
            var module2 = CreateModule();
            var mockReg2 = new Mock<ISystemRegistry>();
            module2.RegisterSystems(mockReg2.Object);

            Assert.Throws<InvalidOperationException>(() => module2.RegisterProviders(repo));
        }

        [Fact]
        public void RegisterProviders_SetsINavmeshProviderSingleton()
        {
            var repo   = CreateMinimalRepo();
            var module = CreateModule();
            var mockReg = new Mock<ISystemRegistry>();
            module.RegisterSystems(mockReg.Object);
            module.RegisterProviders(repo);

            var provider = repo.GetSingletonManaged<INavmeshProvider>();
            Assert.NotNull(provider);
            Assert.IsType<EngineBackedNavmeshProvider>(provider);
        }

        [Fact]
        public void RegisterProviders_SetsIPathRegistrySingleton()
        {
            var repo   = CreateMinimalRepo();
            var module = CreateModule();
            var mockReg = new Mock<ISystemRegistry>();
            module.RegisterSystems(mockReg.Object);
            module.RegisterProviders(repo);

            var registry = repo.GetSingletonManaged<IPathRegistry>();
            Assert.NotNull(registry);
        }

        [Fact]
        public void RegisterProviders_BeforeRegisterSystems_Throws()
        {
            var repo   = CreateMinimalRepo();
            var module = CreateModule();

            Assert.Throws<InvalidOperationException>(() => module.RegisterProviders(repo));
        }

        [Fact]
        public void RegisterSystems_RegistersResponseSystem()
        {
            var module  = CreateModule();
            var mockReg = new Mock<ISystemRegistry>();

            module.RegisterSystems(mockReg.Object);

            mockReg.Verify(r => r.RegisterSystem(It.IsAny<EngineBackedPathResponseSystem>()), Times.Once);
        }

        // ── EngineBackedPathResponseSystem tests ──────────────────────────────────

        /// <summary>
        /// Creates a minimal world that can run <see cref="EngineBackedPathResponseSystem"/>.
        /// </summary>
        private static EntityRepository CreateResponseSystemWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterEvent<PathfindingResultEvent>();
            repo.RegisterComponent<NavState>();
            return repo;
        }

        [Fact]
        public void Execute_WithReachableResult_RegistersInRegistry()
        {
            var registry = new EngineBackedPathRegistry(_pool);
            var system   = new EngineBackedPathResponseSystem(registry);
            var repo     = CreateResponseSystemWorld();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

            _pool.RegisterTrajectoryWithKey(new[] { new Vector2(0f, 0f), new Vector2(5f, 5f) }, 7);

            long requestId = ((long)entity.Index << 32) | 1L;
            repo.Bus.Publish(new PathfindingResultEvent
            {
                RequestId           = requestId,
                IsReachable         = true,
                RouteHandle         = 7,
                TotalDistanceMeters = 7.07f,
                PrimaryBackend      = NavigationBackend.NavRoadGraph,
            });
            repo.Bus.SwapBuffers();

            system.Execute((ISimulationView)repo, 0.016f);

            Assert.True(registry.IsCached(7));
        }

        [Fact]
        public void Execute_WithReachableResult_SetsNavStateTrajectoryId()
        {
            var registry = new EngineBackedPathRegistry(_pool);
            var system   = new EngineBackedPathResponseSystem(registry);
            var repo     = CreateResponseSystemWorld();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

            _pool.RegisterTrajectoryWithKey(new[] { new Vector2(0f, 0f), new Vector2(5f, 5f) }, 7);

            long requestId = ((long)entity.Index << 32) | 1L;
            repo.Bus.Publish(new PathfindingResultEvent
            {
                RequestId           = requestId,
                IsReachable         = true,
                RouteHandle         = 7,
                TotalDistanceMeters = 7.07f,
                PrimaryBackend      = NavigationBackend.NavRoadGraph,
            });
            repo.Bus.SwapBuffers();

            system.Execute((ISimulationView)repo, 0.016f);

            ref readonly var navState = ref repo.GetComponent<NavState>(entity);
            Assert.Equal(7, navState.TrajectoryId);
        }

        [Fact]
        public void Execute_WithReachableResult_SetsNavStateModeCustomTrajectory()
        {
            var registry = new EngineBackedPathRegistry(_pool);
            var system   = new EngineBackedPathResponseSystem(registry);
            var repo     = CreateResponseSystemWorld();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

            _pool.RegisterTrajectoryWithKey(new[] { new Vector2(0f, 0f), new Vector2(5f, 5f) }, 7);

            long requestId = ((long)entity.Index << 32) | 1L;
            repo.Bus.Publish(new PathfindingResultEvent
            {
                RequestId           = requestId,
                IsReachable         = true,
                RouteHandle         = 7,
                TotalDistanceMeters = 7.07f,
                PrimaryBackend      = NavigationBackend.NavRoadGraph,
            });
            repo.Bus.SwapBuffers();

            system.Execute((ISimulationView)repo, 0.016f);

            ref readonly var navState = ref repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.CustomTrajectory, navState.Mode);
        }

        [Fact]
        public void Execute_WithUnreachableResult_DoesNotRegister()
        {
            var registry = new EngineBackedPathRegistry(_pool);
            var system   = new EngineBackedPathResponseSystem(registry);
            var repo     = CreateResponseSystemWorld();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

            long requestId = ((long)entity.Index << 32) | 1L;
            repo.Bus.Publish(new PathfindingResultEvent
            {
                RequestId   = requestId,
                IsReachable = false,
                RouteHandle = 7,
            });
            repo.Bus.SwapBuffers();

            system.Execute((ISimulationView)repo, 0.016f);

            Assert.False(registry.IsCached(7));
        }

        [Fact]
        public void Execute_WithUnreachableResult_DoesNotModifyNavState()
        {
            var registry = new EngineBackedPathRegistry(_pool);
            var system   = new EngineBackedPathResponseSystem(registry);
            var repo     = CreateResponseSystemWorld();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

            long requestId = ((long)entity.Index << 32) | 1L;
            repo.Bus.Publish(new PathfindingResultEvent
            {
                RequestId   = requestId,
                IsReachable = false,
                RouteHandle = 7,
            });
            repo.Bus.SwapBuffers();

            system.Execute((ISimulationView)repo, 0.016f);

            ref readonly var navState = ref repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, navState.Mode);
            Assert.Equal(0, navState.TrajectoryId);
        }
    }
}
