using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Translators;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ CE-3036 (S3) — the unit's TKB sensor list, the one sensor-child factory, the config codec and the
    /// <see cref="Sensors"/> API. ⭐ CE-3045 — derived parts are never saved. 📄 docs/DESIGN_Sensors_And_Doctrine.md §4, §5.
    /// </summary>
    public class TkbSensorTests : IDisposable
    {
        private static readonly Guid ThermalTemplate = new("3a7f8e21-0000-4000-8000-00000000c036");
        private readonly EntityRepository _repo;

        public TkbSensorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<PartMetadata>();
            _repo.RegisterComponent<EqsSensor>();
            _repo.RegisterComponent<EqsCognitiveBuffer>();
            _repo.RegisterComponent<SensorTag>();
            _repo.RegisterComponent<ScenarioIgnoreTag>();
            _repo.RegisterComponent<BehaviorOwnedPart>();
            _repo.RegisterComponent<PerceptionReceptor>();
        }

        public void Dispose() => _repo.Dispose();

        private static SensorEntryDto Thermal(float range, bool disabled = false) => new()
        {
            Kind = SensorModality.Thermal, Template = ThermalTemplate, Disabled = disabled,
            Thermal = new ThermalSensorDto { Range = range, FieldOfViewDegrees = 40f },
        };

        private static TkbTemplate Unit(params SensorEntryDto[] sensors)
        {
            var t = new TkbTemplate("SensorUnit", 1);
            t.AddDescriptor(new SensorCapabilitiesDto { Sensors = new List<SensorEntryDto>(sensors) });
            return t;
        }

        private List<Entity> ChildrenOf(Entity parent)
        {
            var list = new List<Entity>();
            foreach (var e in _repo.Query().With<PartMetadata>().With<EqsSensor>().Build())
                if (_repo.GetComponentRO<PartMetadata>(e).ParentEntity.Equals(parent)) list.Add(e);
            return list;
        }

        /// <summary>⭐ Each TKB entry becomes ONE child (part 1000 + index) carrying its kind, its EQS sensor (template, the
        /// kind's range, OFF when Disabled) and its capability — and re-running the translator builds nothing twice.</summary>
        [Fact]
        public void TheTranslator_BuildsOneChildPerEntry_AtPart1000PlusIndex_Idempotently_CE3036()
        {
            var unit = _repo.CreateEntity();
            var template = Unit(Thermal(800f), Thermal(300f, disabled: true));
            var translator = new PerceptionTkbTranslator();
            translator.Inject(_repo, unit, template);
            translator.Inject(_repo, unit, template);   // spawn + ghost promotion

            var children = ChildrenOf(unit);
            Assert.Equal(2, children.Count);
            var byPart = new Dictionary<int, Entity>();
            foreach (var c in children) byPart[_repo.GetComponentRO<PartMetadata>(c).InstanceId] = c;
            Assert.True(byPart.ContainsKey(1000) && byPart.ContainsKey(1001));

            var first = _repo.GetComponentRO<EqsSensor>(byPart[1000]);
            Assert.Equal(EqsTemplateRegistry.BlueprintIdOf(ThermalTemplate), first.BlueprintId);
            Assert.Equal(800f, first.SearchRadius);
            Assert.False(first.Suspended);
            Assert.True(_repo.GetComponentRO<EqsSensor>(byPart[1001]).Suspended);   // R-187: Disabled ⇒ off at spawn

            var tag = _repo.GetComponentRO<SensorTag>(byPart[1001]);
            Assert.Equal(SensorModality.Thermal, tag.Kind);
            Assert.Equal(1, tag.TkbIndex);
            Assert.Equal(1, tag.FromTkb);
            Assert.Equal(800f, ((ISimulationView)_repo).GetManagedComponentRO<SensorCapability>(byPart[1000]).Current.Range);
            Assert.True(_repo.HasComponent<ScenarioIgnoreTag>(byPart[1000]));      // CE-3045: derived, never saved
        }

        /// <summary>⭐ An entry with no template yet, or a malformed one, builds nothing; a node that does not host sensors
        /// (the EQS types are not registered — an IG) builds nothing.</summary>
        [Fact]
        public void NoTemplate_Malformed_OrANodeWithoutSensors_BuildsNoChild_CE3036()
        {
            var unit = _repo.CreateEntity();
            new PerceptionTkbTranslator().Inject(_repo, unit, Unit(
                Thermal(800f) with { Template = Guid.Empty },
                new SensorEntryDto { Kind = SensorModality.Thermal, Template = ThermalTemplate }));   // no Thermal sub-record
            Assert.Empty(ChildrenOf(unit));

            using var ig = new EntityRepository();
            ig.RegisterComponent<PerceptionReceptor>();
            var igUnit = ig.CreateEntity();
            new PerceptionTkbTranslator().Inject(ig, igUnit, Unit(Thermal(800f)));
            Assert.Equal(1, ig.EntityCount);
        }

        /// <summary>⭐ A behaviour-made sensor never takes a TKB part id: allocation stays below 1000 while TKB children hold 1000+.</summary>
        [Fact]
        public void BehaviourSensors_AllocateBelow1000_BesideTkbSensors_CE3036()
        {
            var unit = _repo.CreateEntity();
            new PerceptionTkbTranslator().Inject(_repo, unit, Unit(Thermal(800f)));
            Assert.Equal(1, EqsChildSensor.AllocatePartId(_repo, unit));
        }

        /// <summary>⭐ The codec reads exactly what it writes, and REFUSES an unknown format, malformed JSON, and an entry
        /// that lacks the sub-record of its own kind (R-186 M′ — never half-configured).</summary>
        [Fact]
        public void Codec_RoundTrips_AndRefusesWhatIsNotASensor_CE3036()
        {
            var entry = Thermal(650f);
            Assert.True(SensorConfigCodec.TryDecode(SensorConfigCodec.KindSensorEntry, SensorConfigCodec.Encode(entry), out var back, out _));
            Assert.Equal(650f, back!.Range);
            Assert.Equal(SensorModality.Thermal, back.Kind);

            Assert.False(SensorConfigCodec.TryDecode("Bogus", SensorConfigCodec.Encode(entry), out _, out var e1));
            Assert.Contains("unknown", e1);
            Assert.False(SensorConfigCodec.TryDecode(SensorConfigCodec.KindSensorEntry, "{ not json", out _, out _));
            Assert.False(SensorConfigCodec.TryDecode(SensorConfigCodec.KindSensorEntry, "{\"Kind\":\"Radar\"}", out _, out var e3));
            Assert.Contains("Radar", e3);
        }

        /// <summary>⭐ The TKB file form: the same options the TKB loader uses read a sensor list — the kind by name, an absent
        /// <c>Disabled</c> as ON (R-187), and an absent list as empty.</summary>
        [Fact]
        public void TkbJson_ReadsTheSensorList_AbsentDisabledIsOn_AbsentListIsEmpty_CE3036()
        {
            const string json = "{\"VisionRange\":0,\"Sensors\":[{\"Kind\":\"Thermal\",\"Template\":\"3a7f8e21-0000-4000-8000-00000000c036\",\"Thermal\":{\"Range\":900}}]}";
            var dto = JsonSerializer.Deserialize<SensorCapabilitiesDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Assert.Single(dto.Sensors);
            Assert.False(dto.Sensors[0].Disabled);
            Assert.Equal(900f, dto.Sensors[0].Range);
            Assert.Empty(JsonSerializer.Deserialize<SensorCapabilitiesDto>("{\"VisionRange\":50}", FdpJsonOptionsRegistry.DefaultRelaxed)!.Sensors);
        }

        /// <summary>⭐ R-185 L / R-187 N′ — AI finds a sensor by KIND; switching a TKB sensor off is an override (payload, epoch
        /// moves); clearing it restores the TKB default at a NEWER epoch; Configure replaces the capability in force.</summary>
        [Fact]
        public void SensorsApi_FindByKind_Switch_Override_Clear_CE3036()
        {
            var unit = _repo.CreateEntity();
            new PerceptionTkbTranslator().Inject(_repo, unit, Unit(Thermal(800f)));
            var sensor = UnitSensors.Of(_repo, unit, SensorModality.Thermal);
            Assert.False(sensor.IsNull);
            Assert.True(UnitSensors.Of(_repo, unit, SensorModality.Radar).IsNull);
            uint epoch0 = _repo.GetComponentRO<EqsSensor>(sensor).Epoch;

            UnitSensors.SetEnabled(_repo, sensor, false);
            Assert.True(_repo.GetComponentRO<EqsSensor>(sensor).Suspended);
            Assert.True(UnitSensors.IsOverridden(_repo, sensor));
            uint epoch1 = _repo.GetComponentRO<EqsSensor>(sensor).Epoch;
            Assert.NotEqual(epoch0, epoch1);

            UnitSensors.Configure(_repo, sensor, Thermal(400f));
            Assert.Equal(400f, _repo.GetComponentRO<EqsSensor>(sensor).SearchRadius);
            Assert.Equal(400f, ((ISimulationView)_repo).GetManagedComponentRO<SensorCapability>(sensor).Current.Range);
            Assert.Equal(800f, ((ISimulationView)_repo).GetManagedComponentRO<SensorCapability>(sensor).Default.Range);

            UnitSensors.ClearOverride(_repo, sensor);
            var restored = _repo.GetComponentRO<EqsSensor>(sensor);
            Assert.False(UnitSensors.IsOverridden(_repo, sensor));
            Assert.False(restored.Suspended);
            Assert.Equal(800f, restored.SearchRadius);
            Assert.NotEqual(epoch1, restored.Epoch);
        }

        /// <summary>⭐ CE-3045 — a behaviour-made sensor child and a weapon-mount child are tagged not-saved. 🔴 Both were saved
        /// into the scenario AND rebuilt on load: a duplicate plus an orphan.</summary>
        [Fact]
        public void DerivedParts_BehaviourSensorsAndWeaponMounts_AreNeverSaved_CE3045()
        {
            var unit = _repo.CreateEntity();
            var child = EqsChildSensor.Ensure(_repo, unit, siteId: 7, new EqsSensor { BlueprintId = 1u, Epoch = 1u });
            Assert.True(_repo.HasComponent<ScenarioIgnoreTag>(child));

            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<WeaponMountInfo>();
            var platform = _repo.CreateEntity();
            var t = new TkbTemplate("TwoGuns", 2);
            t.AddDescriptor(new WeaponSuiteDto { Mounts = new List<WeaponMountDto> { new() { InitialAmmunition = 1 }, new() { InitialAmmunition = 1 } } });
            new CombatTkbTranslator().Inject(_repo, platform, t);
            int mounts = 0;
            foreach (var e in _repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build())
            {
                mounts++;
                Assert.True(_repo.HasComponent<ScenarioIgnoreTag>(e));
            }
            Assert.Equal(1, mounts);
        }
    }
}
