using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S3 (docs/DESIGN_World_Survey_Sensor.md §3) — the WorldSurvey sensor in ONE world (the editor's shape): a brain
    /// creates it, the EQS solver answers it on its survey branch, the apply system holds the answer and announces
    /// <see cref="SensorChange.SurveyReady"/> once; a re-pointed sensor drops an answer for its old inputs; an old answer reads stale.
    /// </summary>
    public sealed class WorldSurveySensorTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly TerrainWorld _terrain;
        private readonly EqsSolverSystem _solver = new();
        private readonly WorldSurveyApplySystem _apply = new();

        public WorldSurveySensorTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.BehaviorState>())
                _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            var folder = System.IO.Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.TerrainAssets(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "bt-range");
            _terrain = TerrainWorldParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "bt-range.world.geojson")), "bt-range",
                TerrainAssets.ForFolder(folder));
            _world.RegisterManagedComponent<TerrainWorld>();
            _world.SetSingletonManaged(_terrain);
        }

        public void Dispose() => _world.Dispose();

        private int HouseA => _terrain.Buildings.ToList().FindIndex(b => b.Label == "House A");

        private Entity Unit()
        {
            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new Fdp.Toolkit.Behavior.Components.BehaviorState { InstanceId = 5 });
            // a child sensor is solved only for a unit with a network id (EqsSensorKey) — as every unit has, in the editor too
            _world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(42));
            return unit;
        }

        // One solver tick, its command buffer played back, then the brain's apply. Returns the SurveyReady edges raised.
        private int Tick()
        {
            var view = (ISimulationView)_world;
            _solver.Execute(view, 0.1f);
            ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
            _apply.Execute(view, 0.1f);
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<SensorChangedEvent>().ToArray().Count(e => e.What == SensorChange.SurveyReady);
        }

        [Fact]
        public void CE3249_ASurvey_IsAnswered_Held_AndAnnouncedOnce()
        {
            var unit = Unit();
            var child = WorldSurveyChildSensor.Ensure(_world, unit, siteId: 3, WorldSurveyRequest.OfBuildings(HouseA));
            Assert.False(child.IsNull);
            Assert.False(WorldSurveyChildSensor.Read(_world, child, 0).Ready);

            Assert.Equal(1, Tick());
            var read = WorldSurveyChildSensor.Read(_world, child, _world.GetComponentRO<WorldSurveyStatus>(child).LastUpdateTimeSeconds);
            Assert.True(read.Ready);
            Assert.False(read.Stale);
            Assert.Equal(1u, read.Version);
            Assert.Equal(4, read.Payload!.Rooms.Count);
            Assert.Equal("House A", Assert.Single(read.Payload.Buildings).Label);

            Assert.Equal(0, Tick());   // not due again (RefreshSeconds), nothing new: no second edge
        }

        [Fact]
        public void CE3249_ARePointedSurvey_DropsTheOldAnswer_AndIsAnnouncedAgain()
        {
            var unit = Unit();
            var child = WorldSurveyChildSensor.Ensure(_world, unit, siteId: 3, WorldSurveyRequest.OfBuildings(HouseA));
            Assert.Equal(1, Tick());
            uint oldEpoch = _world.GetComponentRO<EqsSensor>(child).Epoch;

            Assert.True(WorldSurveyChildSensor.Configure(_world, child, WorldSurveyRequest.OfPoints(new[] { new Vector3(102f, 104f, 6f) })));
            Assert.False(WorldSurveyChildSensor.Read(_world, child, 0).Ready, "a new epoch clears the answer");

            // a late answer for the OLD inputs arrives — it must not count
            int part = _world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(child).InstanceId;
            _world.Bus.PublishManaged(new WorldSurveyResultEvent { ParentNetworkId = 42, LocalChildIndex = part, Epoch = oldEpoch,
                                                                   RefreshTick = 9, PayloadJson = "{}", ContentHash = 7 });
            _world.Bus.SwapBuffers();
            _apply.Execute(_world, 0.1f);
            Assert.Equal(1, _apply.StaleEpochCount);
            Assert.False(WorldSurveyChildSensor.Read(_world, child, 0).Ready);

            Assert.Equal(1, Tick());
            var read = WorldSurveyChildSensor.Read(_world, child, _world.GetComponentRO<WorldSurveyStatus>(child).LastUpdateTimeSeconds);
            var roof = Assert.Single(read.Payload!.Points);
            Assert.Equal(HouseA, roof.Building);
            Assert.False(roof.Indoors);
        }

        [Fact]
        public void CE3249_AnAnswerOlderThanThreeSeconds_ReadsStale()
        {
            var child = WorldSurveyChildSensor.Ensure(_world, Unit(), siteId: 3, WorldSurveyRequest.OfBuildings(HouseA));
            Tick();
            float at = _world.GetComponentRO<WorldSurveyStatus>(child).LastUpdateTimeSeconds;
            Assert.False(WorldSurveyChildSensor.Read(_world, child, at + 2.9).Stale);
            Assert.True(WorldSurveyChildSensor.Read(_world, child, at + 3.1).Stale);
        }
    }
}
