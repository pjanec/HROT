using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Perception
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B0 — the contract the behaviours lane builds against (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.6):
    /// the kind registry, the danger child's form, the per-family accessors, and the QA-037 id fix that lets the danger types
    /// be registered in production.
    /// </summary>
    public sealed class DangerAreaSensorContractTests : IDisposable
    {
        private readonly EntityRepository _repo = new();

        public DangerAreaSensorContractTests()
        {
            _repo.RegisterComponent<PartMetadata>();
            _repo.RegisterComponent<EqsSensor>();
            _repo.RegisterComponent<EqsCognitiveBuffer>();
            _repo.RegisterComponent<SensorTag>();
            _repo.RegisterComponent<ScenarioIgnoreTag>();
            _repo.RegisterComponent<BehaviorOwnedPart>();
            _repo.RegisterComponent<DangerAreaSensor>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
        }

        public void Dispose() => _repo.Dispose();

        [Fact]
        public void CE3072_B0_Registry_NamesEachKindsFamilyAndTypes()
        {
            Assert.Equal(SensorResultFamily.Area, SensorKindRegistry.FamilyOf(SensorModality.DangerArea));
            foreach (var k in new[] { SensorKindRegistry.EqsQuery, SensorModality.Visual, SensorModality.Radar, SensorModality.Thermal, SensorModality.Acoustic })
                Assert.Equal(SensorResultFamily.Ranked, SensorKindRegistry.FamilyOf(k));

            Assert.True(SensorKindRegistry.TryGet(SensorModality.DangerArea, out var area));
            Assert.Equal(typeof(DangerAreaSettings), area.SettingsType);
            Assert.Equal(typeof(DangerAreaCognitiveBuffer), area.ResultComponent);
            Assert.Equal(typeof(DangerAreaDescriptor), area.ElementType);
            Assert.Contains("NextAreaChanged", area.Triggers);
            Assert.Contains("ThreatCrossed", area.Triggers);
            Assert.DoesNotContain("ScoreCrossed", area.Triggers);   // a score trigger on a family with no score
            Assert.True(SensorKindRegistry.TryGet(SensorModality.Visual, out var visual));
            Assert.Equal(typeof(EqsCognitiveBuffer), visual.ResultComponent);
            foreach (var header in SensorKindRegistry.HeaderTriggers)
            {
                Assert.Contains(header, area.Triggers);
                Assert.Contains(header, visual.Triggers);
            }
            Assert.Equal(SensorKindRegistry.All.Select(i => i.Kind).OrderBy(k => k), SensorKindRegistry.All.Select(i => i.Kind));
        }

        [Fact]
        public void CE3072_B0_Ensure_CreatesAnAreaFamilyChild_FoundByKind_Idempotent()
        {
            var unit = _repo.CreateEntity();
            var settings = DangerAreaSettings.ToPoint(new Vector3(350, 350, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, siteId: 7, settings);

            Assert.False(child.IsNull);
            Assert.True(_repo.HasComponent<DangerAreaCognitiveBuffer>(child));
            Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(child));                   // ⛔ never the ranked answer
            Assert.Equal(SensorModality.DangerArea, _repo.GetComponentRO<SensorTag>(child).Kind);
            Assert.Equal(unit, _repo.GetComponentRO<PartMetadata>(child).ParentEntity);
            var stored = _repo.GetComponentRO<DangerAreaSensor>(child);
            Assert.Equal(DangerRouteSource.ToPoint, stored.Settings.RouteSource);
            Assert.Equal(new Vector3(350, 350, 0), stored.Settings.RoutePoint);
            var transport = _repo.GetComponentRO<EqsSensor>(child);
            Assert.Equal(DangerAreaChildSensor.TemplateId, transport.BlueprintId);
            Assert.Equal(settings.CorridorHalfWidth, transport.SearchRadius);
            Assert.Equal(EqsSensor.Point1Bit, transport.ContextPointMask);

            Assert.Equal(child, DangerAreaChildSensor.Ensure(_repo, unit, siteId: 7, settings));   // the same run, the same site
            Assert.Equal(child, UnitSensors.Of(_repo, unit, SensorModality.DangerArea));
            Assert.False(_repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).IsReady);
        }

        [Fact]
        public void CE3072_B0_TypedAccessor_ReadsTheKindsOwnResult_AndRefusesAnotherFamily()
        {
            var unit = _repo.CreateEntity();
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 7, DangerAreaSettings.Default);
            ref var buffer = ref _repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);   // what B3/B4 will write
            buffer.GetSpanRW()[0] = new DangerAreaDescriptor { FeatureId = 42, ThreatRating = 0.8f, Kind = DangerAreaKind.StreetCrossing, DistanceAlongRoute = 31f };
            buffer.Count = 1;
            buffer.LastUpdateTick = 5;

            Assert.True(UnitSensors.TryGetResults<DangerAreaCognitiveBuffer>(_repo, unit, SensorModality.DangerArea, out var read));
            Assert.True(read.IsReady);
            Assert.Equal(42u, read.GetSpanRO()[0].FeatureId);
            Assert.Equal(31f, read.GetSpanRO()[0].DistanceAlongRoute);

            Assert.False(UnitSensors.TryGetResults(_repo, unit, SensorModality.DangerArea, out EqsCognitiveBuffer _));   // no cast
            Assert.Equal(SensorReadStatus.WrongFamily, UnitSensors.ReadRanked(_repo, unit, SensorModality.DangerArea, out _));
        }

        [Fact]
        public void CE3072_B0_ReadRanked_TellsNoSensorFromNoAnswerFromOk()
        {
            var unit = _repo.CreateEntity();
            Assert.Equal(SensorReadStatus.NoSensor, UnitSensors.ReadRanked(_repo, unit, SensorModality.Visual, out _));

            var child = EqsChildSensor.Ensure(_repo, unit, 3, new EqsSensor { BlueprintId = 1u, Epoch = 1 });
            _repo.AddComponent(child, new SensorTag { Kind = SensorModality.Visual });
            Assert.Equal(SensorReadStatus.NoAnswerYet, UnitSensors.ReadRanked(_repo, unit, SensorModality.Visual, out _));

            ref var b = ref _repo.GetComponentRW<EqsCognitiveBuffer>(child);
            b.Count = 1;
            b.LastUpdateTick = 9;
            Assert.Equal(SensorReadStatus.Ok, UnitSensors.ReadRanked(_repo, unit, SensorModality.Visual, out var ranked));
            Assert.Equal(1, ranked.Count);
        }

        [Fact]
        public void CE3072_B0_Configure_RepointsAndClearsTheAreaAnswer_WithoutAddingARankedOne()
        {
            var unit = _repo.CreateEntity();
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 7, DangerAreaSettings.Default);
            ref var buffer = ref _repo.GetComponentRW<DangerAreaCognitiveBuffer>(child);
            buffer.Count = 2;
            buffer.LastUpdateTick = 3;
            uint epoch = _repo.GetComponentRO<EqsSensor>(child).Epoch;

            Assert.True(DangerAreaChildSensor.Configure(_repo, child, DangerAreaSettings.ToPoint(new Vector3(10, 20, 0))));

            Assert.NotEqual(epoch, _repo.GetComponentRO<EqsSensor>(child).Epoch);
            Assert.False(_repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).IsReady);
            Assert.Equal(0, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).Count);
            Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(child));
            Assert.Equal(new Vector3(10, 20, 0), _repo.GetComponentRO<EqsSensor>(child).ContextPoint1);
            Assert.Equal(DangerRouteSource.ToPoint, _repo.GetComponentRO<DangerAreaSensor>(child).Settings.RouteSource);
        }

        [Fact]
        public void CE3072_B0_TkbChildOfTheDangerKind_CarriesTheAreaResult()
        {
            var unit = _repo.CreateEntity();
            var entry = new SensorEntryDto { Kind = SensorModality.DangerArea, Template = new Guid("d4a6e1c0-3072-4b0a-9d1e-da9ae0a10001") };
            var child = SensorChildFactory.EnsureTkbChild(_repo, unit, 0, entry);

            Assert.False(child.IsNull);
            Assert.True(_repo.HasComponent<DangerAreaCognitiveBuffer>(child));
            Assert.True(_repo.HasComponent<DangerAreaSensor>(child));
            Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(child));
        }

        [Fact]
        public void CE3072_B0_AnAnswerWithNoAreas_IsAnAnswer()
        {
            var b = default(DangerAreaCognitiveBuffer);
            Assert.False(b.IsReady);
            b.LastUpdateTick = 1;   // a clear route: Count 0, but answered
            Assert.True(b.IsReady);
        }

        [Fact]
        public void CE3072_B0_Sizes()
        {
            Assert.Equal(DangerAreaDescriptor.PinnedSize, Unsafe.SizeOf<DangerAreaDescriptor>());
            Assert.Equal(8 + 8 * DangerAreaDescriptor.PinnedSize, Unsafe.SizeOf<DangerAreaCognitiveBuffer>());
            Assert.True(Unsafe.SizeOf<DangerAreaCognitiveBuffer>() <= 1024);   // EntityCommandBuffer.MaxComponentSize
        }

        [Fact]
        public void QA037_TheSquadIds_CollideWithNoNavigationId()
        {
            var nav = ConstInts(typeof(NavFakeIds)).Concat(ConstInts(typeof(NavigationContractsComponentIds))).ToList();
            foreach (var id in new[] { GlobalComponentIds.DangerAreaSensor, GlobalComponentIds.DangerAreaCognitiveBuffer, GlobalComponentIds.MovementModeIntent })
                Assert.DoesNotContain(id, nav);
            var global = ConstInts(typeof(GlobalComponentIds)).ToList();
            Assert.Equal(global.Count, global.Distinct().Count());
        }

        private static IEnumerable<int> ConstInts(Type t) => t
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (int)f.GetRawConstantValue()!);
    }
}
