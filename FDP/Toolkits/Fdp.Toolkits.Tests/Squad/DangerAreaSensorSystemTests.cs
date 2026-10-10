using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.Systems;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Tests.Squad
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 + B4 — the danger-area sensor end to end in one world (the editor's fused shape): the solve
    /// (<see cref="DangerAlongRouteSolve"/>), the answer applied (<see cref="DangerAreaSensorSystem"/>), the rating from the
    /// unit's memory, and the edges. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// </summary>
    public sealed class DangerAreaSensorSystemTests : IDisposable
    {
        private readonly EntityRepository _repo = new();

        public DangerAreaSensorSystemTests()
        {
            _repo.RegisterComponent<PartMetadata>();
            _repo.RegisterComponent<EqsSensor>();
            _repo.RegisterComponent<EqsCognitiveBuffer>();
            _repo.RegisterComponent<SensorTag>();
            _repo.RegisterComponent<ScenarioIgnoreTag>();
            _repo.RegisterComponent<BehaviorOwnedPart>();
            _repo.RegisterComponent<DangerAreaSensor>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<NavigationIntent>();
            _repo.RegisterManagedEvent<DangerAreaResultEvent>();
            _repo.RegisterEvent<SensorChangedEvent>();
            _repo.SetSingletonManaged(TestTown());
            // ⭐ CE-3128 — the nav node's navmesh (open ground here): the planner compares walking direct with the roads.
            _repo.SetSingletonManaged<INavmeshProvider>(new StraightNavmesh());
        }

        // ⭐ CE-3128 — the roads are the road GRAPH (test-town's, as roads.json declares it), read through a holder like production.
        private readonly global::CarKinem.Road.RoadNetworkHolder _roads = new(TestTownRoads.Build(), takeOwnership: true);
        private readonly RoutePlanner _planner = new();

        public void Dispose() { _repo.Dispose(); _roads.Dispose(); }

        /// <summary>test-town's ground with one building north-east of the Cross Street crossing (the roads are <see cref="_roads"/>).</summary>
        private static TerrainWorld TestTown() => new()
        {
            Name = "test-town", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(400, 400), GroundZ = 0f,
            Prisms = new[] { Block(new Vector2(240, 320), new Vector2(280, 360), 12f) },
        };

        private static TerrainPrism Block(Vector2 min, Vector2 max, float height) => new()
        {
            Kind = TerrainPrismKind.Building,
            Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
            Triangles = new[] { 0, 1, 2, 0, 2, 3 },
            BaseZ = 0f, TopZ = height, Min = min, Max = max,
        };

        private Entity Unit(Vector3 at)
        {
            var u = _repo.CreateEntity();
            _repo.AddComponent(u, new SimTransform { Position = at });
            _repo.AddComponent(u, default(TargetMemory));
            return u;
        }

        private Entity Hostile(Vector3 at)
        {
            var h = _repo.CreateEntity();
            _repo.AddComponent(h, new SimTransform { Position = at });
            _repo.AddComponent(h, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            _repo.AddComponent(h, new Health { Current = 100f, Max = 100f });
            return h;
        }

        private void Remember(Entity unit, Entity contact, Vector3 at)
        {
            ref var mem = ref _repo.GetComponentRW<TargetMemory>(unit);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)contact.PackedValue, posX: at.X, posY: at.Y,
                scoreBoost: PerceptionConstants.FreshnessSaturation, tick: 1, modality: SensorModality.Visual);
        }

        // One solve, published locally (the fused world), then one Brain tick.
        private DangerAreaCognitiveBuffer SolveAndTick(Entity child, DangerAreaSensorSystem system)
        {
            var sensor = _repo.GetComponentRO<EqsSensor>(child);
            var areas = new DangerAreaDescriptor[8];
            int n = DangerAlongRouteSolve.Solve(_repo, child, in sensor, areas, _planner, _roads);
            _repo.Bus.PublishManaged(new DangerAreaResultEvent
            {
                ParentNetworkId = 0, LocalChildIndex = child.Index, Epoch = sensor.Epoch, RefreshTick = 7, Areas = areas, Count = n,
            });
            _repo.Bus.SwapBuffers();
            system.Execute(_repo, 0.1f);
            return _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
        }

        private List<SensorChange> Edges()
        {
            ((EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer()).Playback(_repo);
            _repo.Bus.SwapBuffers();
            return ((ISimulationView)_repo).ReadEvents<SensorChangedEvent>().ToArray().Select(e => e.What).ToList();
        }

        [Fact]
        public void CE3072_B3_ToPoint_ListsTheCrossing_WithNoThreatWhenNothingIsKnown()
        {
            var unit = Unit(new Vector3(100, 100, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(100, 300, 0)));
            _repo.SetSimulationTime(12.5f);

            var b = SolveAndTick(child, new DangerAreaSensorSystem());

            Assert.True(b.IsReady);
            Assert.Equal(12.5f, b.LastUpdateTimeSeconds);               // Q4 — the stamp BecomesStale reads
            Assert.Equal(1, b.Count);                                   // (100,100) → (100,300) crosses Main Street once
            Assert.Equal(DangerAreaKind.StreetCrossing, b.GetSpanRO()[0].Kind);
            Assert.Equal(0f, b.GetSpanRO()[0].ThreatRating);           // nobody known ⇒ no threat
        }

        [Fact]
        public void CE3072_B4_Threat_IsTheKnownArmedContact_WithSightOfTheArea_AndNotABlockedOne()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));           // on the line, sees the Cross Street crossing
            Remember(unit, watcher, new Vector3(260, 300, 0));
            var system = new DangerAreaSensorSystem();

            var b = SolveAndTick(child, system);
            Assert.Equal(1, b.Count);
            Assert.Equal(1f, b.GetSpanRO()[0].ThreatRating);

            // the rifleman watched it walk behind the building: its last-known position no longer sees the crossing
            Remember(unit, watcher, new Vector3(290, 340, 0));
            system.Execute(_repo, 0.1f);
            Assert.Equal(0f, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).GetSpanRO()[0].ThreatRating);
        }

        [Fact]
        public void CE3072_B4_AKilledWatcher_IsNoThreat()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            Remember(unit, watcher, new Vector3(260, 300, 0));
            _repo.GetComponentRW<Health>(watcher).Current = 0f;          // CE-3080
            Assert.Equal(0f, SolveAndTick(child, new DangerAreaSensorSystem()).GetSpanRO()[0].ThreatRating);
        }

        [Fact]
        public void CE3072_B4_Edges_AreaAhead_Threatened_Cleared()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            Remember(unit, watcher, new Vector3(260, 300, 0));
            var system = new DangerAreaSensorSystem();

            SolveAndTick(child, system);
            var first = Edges();
            Assert.Contains(SensorChange.AreaAhead, first);
            Assert.Contains(SensorChange.AreaThreatened, first);

            system.Execute(_repo, 0.1f);
            Assert.Empty(Edges());                                      // no change, no edge

            Remember(unit, watcher, new Vector3(290, 340, 0));           // gone behind the building
            system.Execute(_repo, 0.1f);
            Assert.Equal(new[] { SensorChange.AreaCleared }, Edges());
        }

        [Fact]
        public void CE3072_B3_AStaleAnswer_IsDropped()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var areas = new[] { new DangerAreaDescriptor { FeatureId = 9 } };
            _repo.Bus.PublishManaged(new DangerAreaResultEvent
            {
                LocalChildIndex = child.Index, Epoch = _repo.GetComponentRO<EqsSensor>(child).Epoch + 1, Areas = areas, Count = 1,
            });
            _repo.Bus.SwapBuffers();
            new DangerAreaSensorSystem().Execute(_repo, 0.1f);
            Assert.False(_repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).IsReady);
        }

        [Fact]
        public void CE3072_B3_OwnMove_WatchesTheRouteToTheUnitsDestination()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            _repo.AddComponent(unit, new NavigationIntent { Mode = NavigationMode.PathToPoint, FinalDestination = new Vector3(300, 300, 0) });
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.Default);   // OwnMove
            Assert.Equal(1, SolveAndTick(child, new DangerAreaSensorSystem()).Count);
        }
    }
}
