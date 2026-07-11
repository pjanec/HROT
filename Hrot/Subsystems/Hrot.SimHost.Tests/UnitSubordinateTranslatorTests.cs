using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UnitSubordinateTranslator"/> — CS013.
    /// </summary>
    public sealed class UnitSubordinateTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public UnitSubordinateTranslatorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<InitialUnitSubordinateIntent>();
        }

        public void Dispose() => _repo.Dispose();

        // ── Stub IGuidResolver ────────────────────────────────────────────────

        private sealed class StubResolver : IGuidResolver
        {
            private readonly Dictionary<Entity, string> _save = new();
            private readonly Dictionary<string, Entity> _load = new();

            public void Register(Entity entity, string guid)
            {
                _save[entity] = guid;
                _load[guid]   = entity;
            }

            public string Resolve(Entity e)       => _save.TryGetValue(e, out var g) ? g : e.ToString();
            public Entity Resolve(string guidStr) => _load.TryGetValue(guidStr, out var e) ? e : Entity.Null;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        // CS013-T01: Inject with valid GUID writes InitialUnitSubordinateIntent

        // B (Fixture Gap TH-3): production UnitSubordinateDto.Designation is string?; test must pass
        // the enum name as a string, not an int. Production spec: "serialises designation as a string".
        [Fact]
        public void Inject_WithValidGuid_WritesInitialUnitSubordinateIntent()
        {
            var commander = _repo.CreateEntity();
            _repo.SetComponent(commander, new NetworkIdentity { Value = 77L });

            var resolver = new StubResolver();
            resolver.Register(commander, "guid-commander");

            var dom = new Dictionary<string, object>
            {
                ["UnitSubordinate"] = new JsonObject
                {
                    ["commanderGuid"] = "guid-commander",
                    ["designation"]   = ((TacticalDesignation)3).ToString(),
                }
            };

            var subordinate = _repo.CreateEntity();
            new UnitSubordinateTranslator().Inject(_repo, subordinate, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialUnitSubordinateIntent>(subordinate));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialUnitSubordinateIntent>(subordinate);
            Assert.NotNull(intent);
            Assert.Equal(77L, intent!.CommanderNetworkId);
            Assert.Equal((TacticalDesignation)3, intent.Designation);
        }

        // CS013-T02: Inject with unresolvable GUID writes intent with CommanderNetworkId = 0

        // B (Fixture Gap TH-3): designation must be a string (enum name).
        [Fact]
        public void Inject_WithUnresolvableGuid_WritesIntentWithZeroNetworkId()
        {
            var resolver = new StubResolver(); // nothing registered

            var dom = new Dictionary<string, object>
            {
                ["UnitSubordinate"] = new JsonObject
                {
                    ["commanderGuid"] = "guid-unknown",
                    ["designation"]   = TacticalDesignation.Undefined.ToString(),
                }
            };

            var subordinate = _repo.CreateEntity();
            new UnitSubordinateTranslator().Inject(_repo, subordinate, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialUnitSubordinateIntent>(subordinate));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialUnitSubordinateIntent>(subordinate);
            Assert.NotNull(intent);
            Assert.Equal(0L, intent!.CommanderNetworkId);
        }

        // CS013-T03: Extract with commander produces correct keys

        // B (Fixture Gap TH-3): production serializes designation as a string (enum name via .ToString()).
        // Test must read GetValue<string>() and compare the enum name, not the integer ordinal.
        [Fact]
        public void Extract_WithCommander_ProducesCommanderGuidAndDesignation()
        {
            var commander = _repo.CreateEntity();
            var subordinate = _repo.CreateEntity();

            _repo.SetComponent(subordinate, new UnitSubordinate
            {
                Commander   = commander,
                Designation = TacticalDesignation.Wingman,
            })
;

            var resolver = new StubResolver();
            resolver.Register(commander, "guid-cmdr-abc");

            var dict = new UnitSubordinateTranslator().Extract(_repo, subordinate, resolver);

            Assert.True(dict.ContainsKey("UnitSubordinate"));
            var obj = (JsonObject)dict["UnitSubordinate"];
            Assert.Equal("guid-cmdr-abc", obj["commanderGuid"]!.GetValue<string>());
            Assert.Equal(TacticalDesignation.Wingman.ToString(), obj["designation"]!.GetValue<string>());
        }

        // CS013-T04: CanTranslate returns false when Commander is null

        [Fact]
        public void CanTranslate_WhenCommanderIsNull_ReturnsFalse()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new UnitSubordinate
            {
                Commander   = Entity.Null,
                Designation = TacticalDesignation.Wingman,
            });

            Assert.False(new UnitSubordinateTranslator().CanTranslate(_repo, entity));
        }
    }
}
