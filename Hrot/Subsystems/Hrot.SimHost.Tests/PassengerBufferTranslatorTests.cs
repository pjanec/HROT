using Fdp.Toolkit.Replication;
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
            Assert.Equal(42L, intent.PassengerNetworkIds[0].NetworkId);
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

        /// <summary>
        /// ⭐⭐ Weapon systems WS-4 (W-I) — a save keeps each passenger's SEAT: a technical's gunner (seat 1) and its M2 (seat 2, the
        /// station) come back into those seats, not into the first free one of a role (a pickup has no passenger seat at all, so the old
        /// load refused both). A save written before seats were saved still loads, with the seats left to the boarding rules.
        /// ✅ Red-proof: Extract without "Seats" ⇒ the intent carries no seats ⇒ red.
        /// </summary>
        [Fact]
        public void WS4_TheSeats_SurviveASaveAndLoad()
        {
            var gunner = _repo.CreateEntity();
            _repo.SetComponent(gunner, new NetworkIdentity { Value = 41L });
            var m2 = _repo.CreateEntity();
            _repo.SetComponent(m2, new NetworkIdentity { Value = 42L });
            var pickup = _repo.CreateEntity();
            var buffer = new PassengerBuffer();
            buffer.Passengers[1] = gunner;
            buffer.Passengers[2] = m2;
            buffer.Count = 2;
            _repo.SetComponent(pickup, buffer);
            var resolver = new StubResolver();
            resolver.Register(gunner, "guid-gunner");
            resolver.Register(m2, "guid-m2");

            var dom = new PassengerBufferTranslator().Extract(_repo, pickup, resolver);
            var loaded = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, loaded, dom, resolver);

            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(loaded)!;
            Assert.Equal(new[] { 41L, 42L }, intent.PassengerNetworkIds.ConvertAll(r => r.NetworkId));
            Assert.Equal(new[] { 1, 2 }, intent.Seats);

            var older = new Dictionary<string, object> { ["PassengerBuffer"] = new JsonObject
                { ["Count"] = 1, ["Passengers"] = new JsonArray { "guid-gunner" } } };
            var old = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, old, older, resolver);
            Assert.Empty(((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(old)!.Seats);
        }
    }
}
