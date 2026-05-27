using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    public class CorridorPreviewSystemTests
    {
        private static Entity CreateEntityWithCorridor(
            EntityRepository repo,
            MusclePathRegistry registry,
            int routeHandle,
            int totalWaypoints,
            int currentSegment = 0,
            byte intentFlags = 0)
        {
            // Register a path with `totalWaypoints` waypoints.
            var waypoints = new NavWaypoint[totalWaypoints];
            for (int i = 0; i < totalWaypoints; i++)
                waypoints[i] = new NavWaypoint
                {
                    Position  = new Vector3(i * 10f, 0f, 0f),
                    Traversal = TraversalKind.Walk,
                    Surface   = SurfaceType.Generic,
                };
            registry.StoreOrReplace(routeHandle, waypoints);

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode     = NavigationMode.DirectPoint,
                IntentId = 1,
                Flags    = intentFlags,
            });
            repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle         = routeHandle,
                CurrentSegmentIndex = currentSegment,
                TotalSegmentCount   = totalWaypoints,
            });
            return entity;
        }

        [Fact]
        public void StreamFlag_Set_PopulatesComponent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
            var entity = CreateEntityWithCorridor(repo, registry,
                             routeHandle: 1, totalWaypoints: 10, intentFlags: previewBit);

            sys.Execute(view, 0.016f);

            Assert.True(repo.HasComponent<NavigationCorridorPreview>(entity));
            var preview = repo.GetComponent<NavigationCorridorPreview>(entity);
            Assert.True(preview.WaypointCount > 0);
            Assert.Equal(0, preview.GlobalSegmentStart);
        }

        [Fact]
        public void StreamFlag_NotSet_ComponentAbsent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            var entity = CreateEntityWithCorridor(repo, registry,
                             routeHandle: 1, totalWaypoints: 10, intentFlags: 0 /* no flag */);

            sys.Execute(view, 0.016f);

            Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
        }

        [Fact]
        public void WaypointCount_Capped_At8()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
            var entity = CreateEntityWithCorridor(repo, registry,
                             routeHandle: 1, totalWaypoints: 20, intentFlags: previewBit);

            sys.Execute(view, 0.016f);

            var preview = repo.GetComponent<NavigationCorridorPreview>(entity);
            Assert.Equal(8, preview.WaypointCount);
        }

        [Fact]
        public void SegmentAdvance_BumpsPreviewVersion()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
            var entity = CreateEntityWithCorridor(repo, registry,
                             routeHandle: 1, totalWaypoints: 20,
                             currentSegment: 0, intentFlags: previewBit);

            sys.Execute(view, 0.016f);
            uint version1 = repo.GetComponent<NavigationCorridorPreview>(entity).PreviewVersion;

            // Advance the corridor.
            var corridor = repo.GetComponent<NavigationCorridorMuscle>(entity);
            corridor.CurrentSegmentIndex = 3;
            repo.SetComponent(entity, corridor);

            sys.Execute(view, 0.016f);
            uint version2 = repo.GetComponent<NavigationCorridorPreview>(entity).PreviewVersion;

            Assert.True(version2 > version1);
            Assert.Equal(3, repo.GetComponent<NavigationCorridorPreview>(entity).GlobalSegmentStart);
        }

        [Fact]
        public void FlagCleared_RemovesComponent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
            var entity = CreateEntityWithCorridor(repo, registry,
                             routeHandle: 1, totalWaypoints: 10, intentFlags: previewBit);

            sys.Execute(view, 0.016f);
            Assert.True(repo.HasComponent<NavigationCorridorPreview>(entity));

            // Clear the flag.
            var intent = repo.GetComponent<NavigationIntent>(entity);
            intent.Flags = 0;
            repo.SetComponent(entity, intent);

            sys.Execute(view, 0.016f);
            Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
        }

        [Fact]
        public void InvalidRouteHandle_NoComponent()
        {
            using var repo = NavigationTestWorldFactory.Create();
            var view       = (ISimulationView)repo;
            var registry   = new MusclePathRegistry();
            var sys        = new CorridorPreviewSystem(registry);

            const byte previewBit = 1 << NavigationConstants.FlagBitStreamCorridorPreview;
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NavigationIntent
            {
                Mode     = NavigationMode.DirectPoint,
                IntentId = 1,
                Flags    = previewBit,
            });
            // RouteHandle = 99 is not registered in the registry.
            repo.AddComponent(entity, new NavigationCorridorMuscle
            {
                RouteHandle         = 99,
                CurrentSegmentIndex = 0,
                TotalSegmentCount   = 0,
            });

            sys.Execute(view, 0.016f);

            Assert.False(repo.HasComponent<NavigationCorridorPreview>(entity));
        }
    }
}
