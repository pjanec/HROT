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
            _repo.SetComponent(target, new NetworkIdentity { Value = 33L });

            var resolver = new StubResolver();
            resolver.Register(target, "guid-target");

            var dom = new Dictionary<string, object>
            {
                ["TargetMemory"] = new JsonObject
                {
                    ["Entries"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["Entity"]   = "guid-target",
                            ["PosX"]     = 10f,
                            ["PosY"]     = 20f,
                            ["Score"]    = 0.8f,
                            ["Tick"]     = 5L,
                            ["Modality"] = 3,
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
            Assert.Equal(33L, intent.Entries[0].NetworkId);
            Assert.Equal(10f, intent.Entries[0].PosX);
            Assert.Equal(20f, intent.Entries[0].PosY);
            Assert.Equal(0.8f, intent.Entries[0].Score, precision: 5);
            Assert.Equal(5u, intent.Entries[0].LastSeenTick);
            Assert.Equal(3, intent.Entries[0].Modality);
        }

        [Fact]
        public void Inject_DeadGuidSkipped_IntentHasFewerEntries()
        {
            // Create target with NetworkIdentity
            var target = _repo.CreateEntity();
            _repo.SetComponent(target, new NetworkIdentity { Value = 11L });

            // "guid-dead" is not in the resolver — simulates GUID that can't resolve to a live entity
            var resolver = new StubResolver();
            resolver.Register(target, "guid-live");
            // "guid-dead" resolves to Entity.Null

            var dom = new Dictionary<string, object>
            {
                ["TargetMemory"] = new JsonObject
                {
                    ["Entries"] = new JsonArray
                    {
                        new JsonObject { ["Entity"] = "guid-live",  ["PosX"] = 1f, ["PosY"] = 2f, ["Score"] = 0.5f, ["Tick"] = 0L, ["Modality"] = 0 },
                        new JsonObject { ["Entity"] = "guid-dead",  ["PosX"] = 3f, ["PosY"] = 4f, ["Score"] = 0.9f, ["Tick"] = 0L, ["Modality"] = 0 },
                    }
                }
            };

            var entity = _repo.CreateEntity();
            new TargetMemoryTranslator().Inject(_repo, entity, dom, resolver);

            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialTargetsIntent>(entity);
            // Only the live entry should be added
            Assert.Equal(1, intent!.Entries.Count);
            Assert.Equal(11L, intent.Entries[0].NetworkId);
        }
    }
}
