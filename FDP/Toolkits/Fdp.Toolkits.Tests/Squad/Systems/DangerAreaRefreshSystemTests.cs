using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Fake;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="DangerAreaRefreshSystem"/>.
    /// Success criteria: SC-P2-03-1 through SC-P2-03-4.
    /// </summary>
    public class DangerAreaRefreshSystemTests : IDisposable
    {
        private EntityRepository _repo;

        public DangerAreaRefreshSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<DangerAreaSensor>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            _repo.RegisterComponent<PartMetadata>();
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static (EntityRepository repo, Entity commander, Entity sensorChild)
            CreateSensorChild(EntityRepository repo, float refreshInterval = 0f)
        {
            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new Blackboard1024());
            repo.AddComponent(commander, new SquadStateMarker());

            var child = repo.CreateEntity();
            repo.AddComponent(child, new DangerAreaSensor());
            repo.AddComponent(child, new DangerAreaCognitiveBuffer());
            repo.AddComponent(child, new PartMetadata());

            ref var sensor = ref repo.GetComponentRW<DangerAreaSensor>(child);
            sensor.BlueprintId = 0xABCD_1234u;
            sensor.RefreshIntervalSeconds = refreshInterval;

            ref var meta = ref repo.GetComponentRW<PartMetadata>(child);
            meta.ParentEntity = commander;

            return (repo, commander, child);
        }

        // ── SC-P2-03-1: SingleChild_WritesDescriptorsAndSetsCount ────────────────

        [Fact]
        public void SingleChild_WritesDescriptorsAndSetsCount()
        {
            var fake = new FakeDangerAreaProvider();
            fake.Add("crossing-01", DangerAreaKind.StreetCrossing, 0.7f);
            fake.Add("crossing-02", DangerAreaKind.StreetCrossing, 0.4f);
            fake.Add("crest-01",    DangerAreaKind.CrestLine,      0.9f);

            var (repo, commander, child) = CreateSensorChild(_repo, refreshInterval: 0f);
            var system = new DangerAreaRefreshSystem(fake);
            system.Run(repo, child, currentSimTime: 0f);

            ref readonly var buffer = ref repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
            Assert.Equal(3, buffer.Count);

            // Smoke check: sensor child's PartMetadata.ParentEntity is the commander.
            ref readonly var meta = ref repo.GetComponentRO<PartMetadata>(child);
            Assert.Equal(commander, meta.ParentEntity);
        }

        // ── SC-P2-03-2: EpochIncrements ─────────────────────────────────────────

        [Fact]
        public void EpochIncrements()
        {
            var fake = new FakeDangerAreaProvider();
            fake.Add("feature-a", DangerAreaKind.OpenGround, 0.5f);

            var (repo, _, child) = CreateSensorChild(_repo, refreshInterval: 0f);
            var system = new DangerAreaRefreshSystem(fake);

            system.Run(repo, child, currentSimTime: 0f);
            Assert.Equal(1u, repo.GetComponentRO<DangerAreaSensor>(child).Epoch);

            system.Run(repo, child, currentSimTime: 0f);
            Assert.Equal(2u, repo.GetComponentRO<DangerAreaSensor>(child).Epoch);
        }

        // ── SC-P2-03-3: TwoSensorChildren_RefreshedIndependently ────────────────

        [Fact]
        public void TwoSensorChildren_RefreshedIndependently()
        {
            // Commander with two sensor children.
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new Blackboard1024());
            _repo.AddComponent(commander, new SquadStateMarker());

            var child1 = _repo.CreateEntity();
            _repo.AddComponent(child1, new DangerAreaSensor());
            _repo.AddComponent(child1, new DangerAreaCognitiveBuffer());
            _repo.AddComponent(child1, new PartMetadata());
            ref var sensor1 = ref _repo.GetComponentRW<DangerAreaSensor>(child1);
            sensor1.BlueprintId = 1u;
            ref var meta1 = ref _repo.GetComponentRW<PartMetadata>(child1);
            meta1.ParentEntity = commander;

            var child2 = _repo.CreateEntity();
            _repo.AddComponent(child2, new DangerAreaSensor());
            _repo.AddComponent(child2, new DangerAreaCognitiveBuffer());
            _repo.AddComponent(child2, new PartMetadata());
            ref var sensor2 = ref _repo.GetComponentRW<DangerAreaSensor>(child2);
            sensor2.BlueprintId = 2u;
            ref var meta2 = ref _repo.GetComponentRW<PartMetadata>(child2);
            meta2.ParentEntity = commander;

            var fake1 = new FakeDangerAreaProvider();
            fake1.Add("f1a", DangerAreaKind.StreetCrossing, 0.6f);
            fake1.Add("f1b", DangerAreaKind.CrestLine,      0.5f);

            var fake2 = new FakeDangerAreaProvider();
            fake2.Add("f2a", DangerAreaKind.ChokePoint, 0.8f);

            var system1 = new DangerAreaRefreshSystem(fake1);
            var system2 = new DangerAreaRefreshSystem(fake2);
            system1.Run(_repo, child1, 0f);
            system2.Run(_repo, child2, 0f);

            Assert.Equal(2, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child1).Count);
            Assert.Equal(1, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child2).Count);
        }

        // ── SC-P2-03-4: ZPreserved ───────────────────────────────────────────────

        [Fact]
        public void ZPreserved()
        {
            var fake = new FakeDangerAreaProvider();
            fake.Add(
                "feature-01",
                DangerAreaKind.ChokePoint,
                0.5f,
                center:   new Vector3(1f, 2f, 3f),
                extentsXY: new Vector2(4f, 5f),
                angleRad: 0f,
                zFloor:   1f,
                zCeiling: 5f);

            var (repo, _, child) = CreateSensorChild(_repo);
            new DangerAreaRefreshSystem(fake).Run(repo, child, 0f);

            ref readonly var buf = ref repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
            var d = buf.GetSpanRO()[0];
            Assert.Equal(1f, d.ZFloor,   precision: 5);
            Assert.Equal(5f, d.ZCeiling, precision: 5);
        }
    }
}
