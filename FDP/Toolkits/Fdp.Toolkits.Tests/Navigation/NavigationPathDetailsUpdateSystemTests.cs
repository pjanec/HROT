using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavigationPathDetailsUpdateSystem"/> (DD-Tests-Nav §4.6, 5 rows).
    /// </summary>
    public class NavigationPathDetailsUpdateSystemTests
    {
        private const int RouteHandle = 55;

        private static (EntityRepository world, MusclePathRegistry muscleRegistry,
                        BrainPathRegistry brainRegistry, Entity entity)
            CreateWorld(int brainMaxEntries = 32)
        {
            var world = new EntityRepository();

            world.RegisterEvent<NavigationPathDetailsResponseEvent>();
            world.RegisterEvent<NavigationPathDetailsArrivedEvent>();

            world.RegisterComponent<NavigationPathDetailsBuffer>();

            var muscleRegistry = new MusclePathRegistry();
            var brainRegistry  = new BrainPathRegistry(brainMaxEntries);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new NavigationPathDetailsBuffer());

            return (world, muscleRegistry, brainRegistry, entity);
        }

        private static void PublishResponseEvent(EntityRepository world, Entity entity,
                                                  int routeHandle,
                                                  byte replanCount   = 0,
                                                  byte isAutoRefresh = 0)
        {
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target        = entity,
                RouteHandle   = routeHandle,
                ReplanCount   = replanCount,
                IsAutoRefresh = isAutoRefresh,
            });
            world.Bus.SwapBuffers();
        }

        // ── Test 1: Response event populates Brain path registry ─────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 1: ResponseEventArrives_PopulatesBrainPathRegistry.
        /// After the system processes a response event, <see cref="BrainPathRegistry.IsCached"/>
        /// must return true for the route handle.
        /// </summary>
        [Fact]
        public void ResponseEvent_PopulatesBrainPathRegistry()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero,       Traversal = TraversalKind.Walk },
                new NavWaypoint { Position = new Vector3(10, 0, 0), Traversal = TraversalKind.Walk },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            PublishResponseEvent(world, entity, RouteHandle);
            system.Execute(world, 0.016f);

            Assert.True(brainRegistry.IsCached(RouteHandle));
        }

        // ── Test 2: Response event fires NavigationPathDetailsArrivedEvent ───────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 2: ResponseEventArrives_FiresArrivedEvent.
        /// The system must emit exactly one <see cref="NavigationPathDetailsArrivedEvent"/>
        /// on the bus after processing.
        /// </summary>
        [Fact]
        public void ResponseEvent_FiresArrivedEvent()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            PublishResponseEvent(world, entity, RouteHandle);
            system.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<NavigationPathDetailsArrivedEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(entity,      events[0].Target);
            Assert.Equal(RouteHandle, events[0].RouteHandle);
        }

        // ── Test 3: IsAutoRefresh flag preserved in arrived event ────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 3: ResponseEvent_IsAutoRefresh_PreservesFlag.
        /// When the response event carries IsAutoRefresh=1, the arrived event must echo the flag.
        /// </summary>
        [Fact]
        public void ResponseEvent_IsAutoRefresh_PreservedInArrivedEvent()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target        = entity,
                RouteHandle   = RouteHandle,
                ReplanCount   = 0,
                IsAutoRefresh = 1,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            world.Bus.SwapBuffers();
            var events = world.Bus.Read<NavigationPathDetailsArrivedEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal((byte)1, events[0].IsAutoRefresh);
        }

        // ── Test 4: ReplanCount updated in Brain registry ────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 4: ResponseEventReceived_LastObservedReplanCountUpdated.
        /// After processing, the Brain cache entry must carry the replan count from the event.
        /// </summary>
        [Fact]
        public void ResponseEvent_UpdatesReplanCountInBrainRegistry()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld();
            const byte replanCount = 3;

            muscleRegistry.StoreOrReplace(RouteHandle, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = RouteHandle,
                ReplanCount = replanCount,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            var entries = ((IFakeBrainPathRegistryTestApi)brainRegistry).SnapshotEntityCache(entity);
            Assert.Single(entries);
            Assert.Equal(replanCount, entries[0].LastObservedReplanCount);
        }

        // ── Test 5: LRU cap evicts oldest entry ──────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.6 row 5: LruCapExceeded_OldestEvicted.
        /// When the Brain registry is at capacity (maxEntries=1) and a second response arrives,
        /// the first entry must be evicted to make room for the new one.
        /// </summary>
        [Fact]
        public void ResponseEvent_LruCapExceeded_OldestEvicted()
        {
            var (world, muscleRegistry, brainRegistry, entity) = CreateWorld(brainMaxEntries: 1);
            const int handle1 = 100;
            const int handle2 = 200;

            muscleRegistry.StoreOrReplace(handle1, new[]
            {
                new NavWaypoint { Position = Vector3.Zero },
            });
            muscleRegistry.StoreOrReplace(handle2, new[]
            {
                new NavWaypoint { Position = Vector3.One },
            });

            var system = new NavigationPathDetailsUpdateSystem(muscleRegistry, brainRegistry);

            // Ingest first handle.
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = handle1,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            Assert.True(brainRegistry.IsCached(handle1));

            // Ingest second handle — should evict handle1 (LRU cap = 1).
            world.Bus.Publish(new NavigationPathDetailsResponseEvent
            {
                Target      = entity,
                RouteHandle = handle2,
            });
            world.Bus.SwapBuffers();
            system.Execute(world, 0.016f);

            Assert.False(brainRegistry.IsCached(handle1), "handle1 should have been evicted");
            Assert.True(brainRegistry.IsCached(handle2),  "handle2 should be cached");
        }
    }
}
