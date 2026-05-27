using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// All-in-one ECS module that wires up fake navigation providers for single-process
    /// (non-DDS) integration tests.
    ///
    /// Construct the module, call <see cref="RegisterProviders"/> once with the
    /// <see cref="EntityRepository"/>, then drive the simulation manually.
    /// </summary>
    public sealed class NavigationFakesModule : IEcsModule, IDisposable
    {
        /// <summary>Fake navmesh provider. Exposed for direct manipulation in tests.</summary>
        public FakeNavmeshProvider Navmesh { get; }

        /// <summary>Fake DT crowd provider. Exposed for direct manipulation in tests.</summary>
        public FakeDtCrowdProvider Crowd { get; }

        /// <summary>Fake volumetric path provider. Exposed for direct manipulation in tests.</summary>
        public FakeVolumetricPathProvider Volumetric { get; }

        /// <summary>Shared path registry (muscle + brain in one). Exposed for test writes.</summary>
        public SharedPathRegistry PathRegistry { get; }

        /// <summary>The <see cref="NavTestMap"/> that was used to build the providers, or null.</summary>
        public NavTestMap? Map { get; }

        // ── Constructors ─────────────────────────────────────────────────────────

        /// <summary>Construct the module from explicit provider instances.</summary>
        public NavigationFakesModule(
            FakeNavmeshProvider        navmesh,
            FakeDtCrowdProvider        crowd,
            FakeVolumetricPathProvider volumetric,
            SharedPathRegistry         pathRegistry)
        {
            Navmesh      = navmesh      ?? throw new ArgumentNullException(nameof(navmesh));
            Crowd        = crowd        ?? throw new ArgumentNullException(nameof(crowd));
            Volumetric   = volumetric   ?? throw new ArgumentNullException(nameof(volumetric));
            PathRegistry = pathRegistry ?? throw new ArgumentNullException(nameof(pathRegistry));
        }

        /// <summary>Construct the module from a <see cref="NavTestMap"/>.</summary>
        public NavigationFakesModule(NavTestMap map)
            : this(
                new FakeNavmeshProvider(map),
                new FakeDtCrowdProvider(),
                new FakeVolumetricPathProvider(map),
                new SharedPathRegistry())
        {
            Map = map;
        }

        /// <summary>Construct the module with default (empty) providers.</summary>
        public NavigationFakesModule()
            : this(
                new FakeNavmeshProvider(),
                new FakeDtCrowdProvider(),
                new FakeVolumetricPathProvider(),
                new SharedPathRegistry())
        {
        }

        // ── IEcsModule ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "NavigationFakesModule";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // Navigation fakes do not register any ECS systems.
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
        {
            // Navigation fakes do not drive logic during Tick.
        }

        // ── Setup ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Register the fake providers as managed singletons in <paramref name="repo"/>.
        /// Call this once after creating the test world, before running any navigation systems.
        ///
        /// Only <see cref="INavmeshProvider"/> is registered in the ECS (it has a
        /// <c>[ComponentId]</c>). The crowd, volumetric, and path-registry providers are
        /// accessible directly via the module's properties.
        /// </summary>
        public void RegisterProviders(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            repo.SetSingletonManaged<INavmeshProvider>(Navmesh);
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose()
        {
            // No unmanaged resources. Provided for symmetry with test fixture teardown.
        }
    }
}
