using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PassengerBufferTranslator"/> — TASK-S403.
    /// Verifies that Inject writes <see cref="InitialPassengersIntent"/> (not <see cref="PassengerBuffer"/>).
    /// </summary>
    public sealed class PassengerBufferTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public PassengerBufferTranslatorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<PassengerBuffer>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<InitialPassengersIntent>();
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
        public void Inject_WritesInitialPassengersIntent_WithCorrectNetworkIds()
        {
            var passenger = _repo.CreateEntity();
            _repo.SetComponent(passenger, new NetworkIdentity { Value = 42L });

            var resolver = new StubResolver();
            resolver.Register(passenger, "guid-passenger");

            var dom = new Dictionary<string, object>
            {
                ["PassengerBuffer"] = new JsonObject
                {
                    ["Count"]      = 1,
                    ["Passengers"] = new JsonArray { "guid-passenger" },
                }
            };

            var entity = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialPassengersIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(entity);
            Assert.Equal(1, intent!.PassengerNetworkIds.Count);
            Assert.Equal(42L, intent.PassengerNetworkIds[0]);
        }

        [Fact]
        public void Inject_DoesNotWritePassengerBuffer()
        {
            var passenger = _repo.CreateEntity();
            _repo.SetComponent(passenger, new NetworkIdentity { Value = 42L });

            var resolver = new StubResolver();
            resolver.Register(passenger, "guid-passenger");

            var dom = new Dictionary<string, object>
            {
                ["PassengerBuffer"] = new JsonObject
                {
                    ["Count"]      = 1,
                    ["Passengers"] = new JsonArray { "guid-passenger" },
                }
            };

            var entity = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, entity, dom, resolver);

            Assert.False(_repo.HasComponent<PassengerBuffer>(entity));
        }

        [Fact]
        public void Extract_StillProducesGuidStrings()
        {
            var passenger = _repo.CreateEntity();

            var vehicle = _repo.CreateEntity();
            var buffer = new PassengerBuffer();
            buffer.Passengers[0] = passenger;
            buffer.Count = 1;
            _repo.SetComponent(vehicle, buffer);

            var resolver = new StubResolver();
            resolver.Register(passenger, "guid-p");

            var dom = new PassengerBufferTranslator().Extract(_repo, vehicle, resolver);

            Assert.True(dom.ContainsKey("PassengerBuffer"));
            var obj = (JsonObject)dom["PassengerBuffer"];
            var arr = (JsonArray)obj["Passengers"]!;
            Assert.Equal(1, arr.Count);
            Assert.Equal("guid-p", arr[0]?.GetValue<string>());
        }
    }
}
