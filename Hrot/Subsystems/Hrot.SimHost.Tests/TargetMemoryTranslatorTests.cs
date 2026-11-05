using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the updated <see cref="TargetMemoryTranslator"/> — TASK-S406.
    /// Verifies that Inject writes <see cref="InitialTargetsIntent"/> (not <see cref="TargetMemory"/>).
    /// </summary>
    public sealed class TargetMemoryTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public TargetMemoryTranslatorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<InitialTargetsIntent>();
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

        // ── Tests ──────────────────────────────────────────────────────────────

        [Fact]
        public void Inject_WritesInitialTargetsIntent_NotTargetMemory()
        {
            var target = _repo.CreateEntity();
            _repo.SetComponent(target, new NetworkIdentity { Value = 33 });

            var resolver = new StubResolver();
            resolver.Register(target, "f2763bca-f26e-5ee6-846a-a57d20ee4f29");

            var dom = new Dictionary<string, object>
            {
                ["TargetMemory"] = new JsonObject
                {
                    ["Entries"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Entity"]   = "f2763bca-f26e-5ee6-846a-a57d20ee4f29",
                            ["PosX"]     = 10f,
                            ["PosY"]     = 20f,
                            ["Score"]    = 0.8f,
                            ["Tick"]     = 5L,
                            ["Modality"] = 3,
                            ["Stance"]   = 2,   // ⭐ CE-2326 — StanceId + 1
                        }
                    }
                }
            };

            var entity = _repo.CreateEntity();
            new TargetMemoryTranslator().Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialTargetsIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialTargetsIntent>(entity);
            Assert.NotNull(intent);
            Assert.Equal(1, intent!.Entries.Count);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(System.Guid.Parse("f2763bca-f26e-5ee6-846a-a57d20ee4f29")), intent.Entries[0].NetworkId.Spec);
            Assert.Equal(10f, intent.Entries[0].PosX);
            Assert.Equal(20f, intent.Entries[0].PosY);
            Assert.Equal(0.8f, intent.Entries[0].Score, precision: 5);
            Assert.Equal(5u, intent.Entries[0].LastSeenTick);
            Assert.Equal(3, intent.Entries[0].Modality);
            Assert.Equal(2, intent.Entries[0].SeenStance);
        }

        [Fact]
        public void Inject_DeadGuidSkipped_IntentHasFewerEntries()
        {
            // Create target with NetworkIdentity
            var target = _repo.CreateEntity();
            _repo.SetComponent(target, new NetworkIdentity { Value = 11 });

            // "4ff5972e-b957-57a5-ac84-86e2e0993691" is not in the resolver — simulates GUID that can't resolve to a live entity
            var resolver = new StubResolver();
            resolver.Register(target, "773bd61f-d8bd-5fc2-b5db-c290eef1a366");
            // "4ff5972e-b957-57a5-ac84-86e2e0993691" resolves to Entity.Null

            var dom = new Dictionary<string, object>
            {
                ["TargetMemory"] = new JsonObject
                {
                    ["Entries"] = new JsonArray
                    {
                        new JsonObject { ["Entity"] = "773bd61f-d8bd-5fc2-b5db-c290eef1a366",  ["PosX"] = 1f, ["PosY"] = 2f, ["Score"] = 0.5f, ["Tick"] = 0L, ["Modality"] = 0 },
                        new JsonObject { ["Entity"] = "4ff5972e-b957-57a5-ac84-86e2e0993691",  ["PosX"] = 3f, ["PosY"] = 4f, ["Score"] = 0.9f, ["Tick"] = 0L, ["Modality"] = 0 },
                    }
                }
            };

            var entity = _repo.CreateEntity();
            new TargetMemoryTranslator().Inject(_repo, entity, dom, resolver);

            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialTargetsIntent>(entity);
            // S8: the translator no longer resolves - both entries are kept as links; a dead GUID fails the extractor loudly
            Assert.Equal(2, intent!.Entries.Count);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(System.Guid.Parse("773bd61f-d8bd-5fc2-b5db-c290eef1a366")), intent.Entries[0].NetworkId.Spec);
        }

        /// <summary>
        /// ⭐ <c>CE-502</c> — a target saved AFTER the entity that remembers it survives a save → load through the real
        /// serializer. Load creates every entity first, then injects them in file order, so a translator reading the
        /// target's <see cref="NetworkIdentity"/> during inject saw nothing yet for a later entity and dropped the entry.
        /// </summary>
        [Fact]
        public unsafe void RoundTrip_ATargetSavedLater_IsKept_CE502()
        {
            _repo.RegisterComponent<TargetMemory>();
            var soldier   = _repo.CreateEntity();                       // saved first
            var insurgent = _repo.CreateEntity();                       // saved second — the forward reference
            _repo.SetComponent(soldier,   new NetworkIdentity { Value = 501 });
            _repo.SetComponent(insurgent, new NetworkIdentity { Value = 502 });
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)insurgent.PackedValue, posX: 1f, posY: 2f, scoreBoost: 100f, tick: 0u, seenStance: 3);
            _repo.SetComponent(soldier, mem);

            var serializer = HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry());
            var json = serializer.Serialize(_repo, new ScenarioHeader(Hrot.Common.Scenario.HrotSubsystemTypes.Scenario)).ToJsonString();

            using var loaded = new EntityRepository();
            loaded.RegisterComponent<NetworkIdentity>();
            loaded.RegisterComponent<TargetMemory>();
            loaded.RegisterManagedComponent<InitialTargetsIntent>();
            serializer.Deserialize(loaded, json);

            InitialTargetsIntent? intent = null;
            foreach (var e in loaded.Query().WithManaged<InitialTargetsIntent>().Build())
                intent = ((ISimulationView)loaded).GetManagedComponentRO<InitialTargetsIntent>(e);
            Assert.NotNull(intent);
            Assert.Single(intent!.Entries);
            Assert.Equal(Fdp.Core.RefForm.Exact, intent.Entries[0].NetworkId.Spec.Form);   // S8: the saved GUID of the later-saved target
            Assert.Equal(3, intent.Entries[0].SeenStance);   // ⭐ CE-2326 (R-3030) — the stance he was last seen in survives the save
        }
    
        /// <summary>
        /// ⭐⭐ <c>CE-4250</c> — a save while a Preview runs: a unit's target memory names one SAVED enemy and one the preview spawned (outside the save
        /// map — modelled by <c>ScenarioIgnoreTag</c>, which keeps an entity out of the map exactly as a preview spawn is). The whole save goes through the
        /// real Hrot serializer: ⛔ no throw (it killed the editor), the saved enemy's entry survives, the other is dropped. 🔴 Mutation:
        /// <c>TryResolve</c> back to <c>Resolve</c> in the translator ⇒ <c>InvalidOperationException … not in the save map</c> ⇒ red.
        /// </summary>
        [Fact]
        public void CE4250_ASaveWhoseTargetMemoryNamesAnUnsavedEntity_DropsThatEntry_AndDoesNotThrow()
        {
            using var w = new EntityRepository();
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(w);
            if (!w.IsComponentTypeRegistered<TargetMemory>()) w.RegisterComponent<TargetMemory>();
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>()) w.RegisterComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>();
            Entity Unit(string name)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new Fdp.Core.EntityInfo { Name = new FixedString64(name), ForceId = ForceId.Friend });
                w.AddComponent(e, new Fdp.Toolkit.Scenario.EntityGuid(Guid.NewGuid()));
                return e;
            }
            var me = Unit("me"); var saved = Unit("saved enemy"); var spawned = Unit("preview spawn");
            w.AddComponent(spawned, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, (long)saved.PackedValue, 1f, 2f, 1f, 1);
            TargetMemory.AddOrUpdateTarget(ref mem, (long)spawned.PackedValue, 3f, 4f, 1f, 1);
            w.AddComponent(me, mem);

            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
            var json = serializer.Serialize(w, new ScenarioHeader("Hrot.Scenario"));   // ⛔ threw "not in the save map" before CE-4250

            var savedGuid = w.GetComponent<Fdp.Toolkit.Scenario.EntityGuid>(saved).Value.ToString();
            var spawnedGuid = w.GetComponent<Fdp.Toolkit.Scenario.EntityGuid>(spawned).Value.ToString();
            var text = json.ToJsonString();
            Assert.Contains(savedGuid, text);
            Assert.DoesNotContain(spawnedGuid, text);
        }
}
}
