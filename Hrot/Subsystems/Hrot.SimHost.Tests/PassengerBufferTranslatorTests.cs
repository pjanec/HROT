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
            _repo.SetComponent(passenger, new NetworkIdentity { Value = 42 });

            var resolver = new StubResolver();
            resolver.Register(passenger, "1c9229ae-cdcd-5eb5-9bc0-77969ab97539");

            var dom = new Dictionary<string, object>
            {
                ["PassengerBuffer"] = new JsonObject
                {
                    ["Count"]      = 1,
                    ["Passengers"] = new JsonArray { "1c9229ae-cdcd-5eb5-9bc0-77969ab97539" },
                }
            };

            var entity = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialPassengersIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(entity);
            Assert.Equal(1, intent!.PassengerNetworkIds.Count);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(System.Guid.Parse("1c9229ae-cdcd-5eb5-9bc0-77969ab97539")), intent.PassengerNetworkIds[0].Spec);
        }

        [Fact]
        public void Inject_DoesNotWritePassengerBuffer()
        {
            var passenger = _repo.CreateEntity();
            _repo.SetComponent(passenger, new NetworkIdentity { Value = 42 });

            var resolver = new StubResolver();
            resolver.Register(passenger, "1c9229ae-cdcd-5eb5-9bc0-77969ab97539");

            var dom = new Dictionary<string, object>
            {
                ["PassengerBuffer"] = new JsonObject
                {
                    ["Count"]      = 1,
                    ["Passengers"] = new JsonArray { "1c9229ae-cdcd-5eb5-9bc0-77969ab97539" },
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
            resolver.Register(passenger, "74210a8c-691d-5428-b54a-d9f587de9f88");

            var dom = new PassengerBufferTranslator().Extract(_repo, vehicle, resolver);

            Assert.True(dom.ContainsKey("PassengerBuffer"));
            var obj = (JsonObject)dom["PassengerBuffer"];
            var arr = (JsonArray)obj["Passengers"]!;
            Assert.Equal(1, arr.Count);
            Assert.Equal("74210a8c-691d-5428-b54a-d9f587de9f88", arr[0]?.GetValue<string>());
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
            _repo.SetComponent(gunner, new NetworkIdentity { Value = 41 });
            var m2 = _repo.CreateEntity();
            _repo.SetComponent(m2, new NetworkIdentity { Value = 42 });
            var pickup = _repo.CreateEntity();
            var buffer = new PassengerBuffer();
            buffer.Passengers[1] = gunner;
            buffer.Passengers[2] = m2;
            buffer.Count = 2;
            _repo.SetComponent(pickup, buffer);
            var resolver = new StubResolver();
            resolver.Register(gunner, "a1f1260e-379b-5ad2-b3ea-2ee258695232");
            resolver.Register(m2, "cbfa0d1f-2250-5f93-b4e0-ee1ee846183f");

            var dom = new PassengerBufferTranslator().Extract(_repo, pickup, resolver);
            var loaded = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, loaded, dom, resolver);

            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(loaded)!;
            Assert.Equal(2, intent.PassengerNetworkIds.Count);   // S8: links, resolved by the extractor
            Assert.Equal(new[] { 1, 2 }, intent.Seats);

            var older = new Dictionary<string, object> { ["PassengerBuffer"] = new JsonObject
                { ["Count"] = 1, ["Passengers"] = new JsonArray { "a1f1260e-379b-5ad2-b3ea-2ee258695232" } } };
            var old = _repo.CreateEntity();
            new PassengerBufferTranslator().Inject(_repo, old, older, resolver);
            Assert.Empty(((ISimulationView)_repo).GetManagedComponentRO<InitialPassengersIntent>(old)!.Seats);
        }
    }
}
