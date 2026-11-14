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
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Serializers;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="VisHierarchyNodeTranslator"/>,
    /// <see cref="IsEmbarkedTagTranslator"/>, and <see cref="PersonalRouteRefTranslator"/>
    /// — TASK-S402.
    /// </summary>
    public sealed class IntentTranslatorTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public IntentTranslatorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<VisHierarchyNode>();
            _repo.RegisterComponent<IsEmbarkedTag>();
            _repo.RegisterComponent<PersonalRouteRef>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<InitialHierarchyIntent>();
            _repo.RegisterManagedComponent<InitialVehicleIntent>();
            _repo.RegisterManagedComponent<InitialRouteIntent>();
        }

        public void Dispose() => _repo.Dispose();

        // ── Stub IGuidResolver ────────────────────────────────────────────────

        private sealed class StubResolver : IGuidResolver
        {
            private readonly Dictionary<Entity, string> _save  = new();
            private readonly Dictionary<string, Entity> _load  = new();

            public void Register(Entity entity, string guid)
            {
                _save[entity] = guid;
                _load[guid]   = entity;
            }

            public string  Resolve(Entity e)         => _save.TryGetValue(e, out var g) ? g : e.ToString();
            public Entity  Resolve(string guidStr)   => _load.TryGetValue(guidStr, out var e) ? e : Entity.Null;
        }

        private static StubResolver MakeResolver(EntityRepository repo, params Entity[] entities)
        {
            var resolver = new StubResolver();
            for (int i = 0; i < entities.Length; i++)
                resolver.Register(entities[i], new System.Guid(i + 1, 0, 0, new byte[8]).ToString());
            return resolver;
        }

        // ── VisHierarchyNodeTranslator ─────────────────────────────────────────

        [Fact]
        public void VisHierarchyNodeTranslator_Extract_WritesParentGuidToDOM()
        {
            var parent = _repo.CreateEntity();
            var node   = _repo.CreateEntity();
            _repo.SetComponent(node, new VisHierarchyNode { Parent = parent });

            var resolver = MakeResolver(_repo, parent, node);
            var translator = new VisHierarchyNodeTranslator();

            var dom = translator.Extract(_repo, node, resolver);

            Assert.True(dom.ContainsKey("VisHierarchyNode"));
            var obj = (JsonObject)dom["VisHierarchyNode"];
            Assert.Equal(new System.Guid(1, 0, 0, new byte[8]).ToString(), obj["Parent"]?.GetValue<string?>());
        }

        [Fact]
        public void VisHierarchyNodeTranslator_Inject_WritesInitialHierarchyIntent()
        {
            var parent = _repo.CreateEntity();
            _repo.SetComponent(parent, new NetworkIdentity { Value = 999 });

            var entity = _repo.CreateEntity();

            // GUID new System.Guid(1, 0, 0, new byte[8]).ToString() resolves to parent entity
            var resolver = new StubResolver();
            resolver.Register(parent, new System.Guid(1, 0, 0, new byte[8]).ToString());

            var dom = new Dictionary<string, object>
            {
                ["VisHierarchyNode"] = new JsonObject
                {
                    ["Parent"]      = new System.Guid(1, 0, 0, new byte[8]).ToString(),
                    ["FirstChild"]  = (string?)null,
                    ["NextSibling"] = (string?)null,
                }
            };

            var translator = new VisHierarchyNodeTranslator();
            translator.Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialHierarchyIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialHierarchyIntent>(entity);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(new System.Guid(1, 0, 0, new byte[8])), intent!.ParentNetworkId.Spec);
            Assert.True(intent.FirstChildNetworkId.IsNone);
            Assert.True(intent.NextSiblingNetworkId.IsNone);
        }

        [Fact]
        public void VisHierarchyNodeTranslator_CanTranslate_ReturnsFalseWhenAbsent()
        {
            var entity = _repo.CreateEntity();
            Assert.False(new VisHierarchyNodeTranslator().CanTranslate(_repo, entity));
        }

        [Fact]
        public void VisHierarchyNodeTranslator_CanTranslate_ReturnsTrueWhenPresent()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new VisHierarchyNode());
            Assert.True(new VisHierarchyNodeTranslator().CanTranslate(_repo, entity));
        }

        // ── IsEmbarkedTagTranslator ────────────────────────────────────────────

        [Fact]
        public void IsEmbarkedTagTranslator_Extract_WritesVehicleGuidToDOM()
        {
            var vehicle = _repo.CreateEntity();
            var soldier = _repo.CreateEntity();
            _repo.SetComponent(soldier, new IsEmbarkedTag { VehicleEntity = vehicle });

            var resolver = MakeResolver(_repo, vehicle, soldier);
            var translator = new IsEmbarkedTagTranslator();

            var dom = translator.Extract(_repo, soldier, resolver);

            Assert.True(dom.ContainsKey("IsEmbarkedTag"));
            var obj = (JsonObject)dom["IsEmbarkedTag"];
            Assert.Equal(new System.Guid(1, 0, 0, new byte[8]).ToString(), obj["Vehicle"]?.GetValue<string?>());
        }

        [Fact]
        public void IsEmbarkedTagTranslator_Inject_WritesInitialVehicleIntent()
        {
            var vehicle = _repo.CreateEntity();
            _repo.SetComponent(vehicle, new NetworkIdentity { Value = 777 });

            var entity = _repo.CreateEntity();

            var resolver = new StubResolver();
            resolver.Register(vehicle, "a1d44232-99a1-5ce1-87ff-b5c86966c093");

            var dom = new Dictionary<string, object>
            {
                ["IsEmbarkedTag"] = new JsonObject { ["Vehicle"] = "a1d44232-99a1-5ce1-87ff-b5c86966c093" }
            };

            new IsEmbarkedTagTranslator().Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialVehicleIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialVehicleIntent>(entity);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(System.Guid.Parse("a1d44232-99a1-5ce1-87ff-b5c86966c093")), intent!.VehicleNetworkId.Spec);
        }

        [Fact]
        public void IsEmbarkedTagTranslator_CanTranslate_ReturnsFalseWhenAbsent()
        {
            var entity = _repo.CreateEntity();
            Assert.False(new IsEmbarkedTagTranslator().CanTranslate(_repo, entity));
        }

        // ── PersonalRouteRefTranslator ─────────────────────────────────────────

        [Fact]
        public void PersonalRouteRefTranslator_Extract_WritesRouteGuidToDOM()
        {
            var route  = _repo.CreateEntity();
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new PersonalRouteRef { RouteEntity = route });

            var resolver = MakeResolver(_repo, route, entity);
            var dom = new PersonalRouteRefTranslator().Extract(_repo, entity, resolver);

            Assert.True(dom.ContainsKey("PersonalRouteRef"));
            var obj = (JsonObject)dom["PersonalRouteRef"];
            Assert.Equal(new System.Guid(1, 0, 0, new byte[8]).ToString(), obj["Route"]?.GetValue<string?>());
        }

        [Fact]
        public void PersonalRouteRefTranslator_Inject_WritesInitialRouteIntent()
        {
            var route = _repo.CreateEntity();
            _repo.SetComponent(route, new NetworkIdentity { Value = 555 });

            var entity = _repo.CreateEntity();

            var resolver = new StubResolver();
            resolver.Register(route, "378acf61-8f50-5aae-8e7d-d9abc821ae0a");

            var dom = new Dictionary<string, object>
            {
                ["PersonalRouteRef"] = new JsonObject { ["Route"] = "378acf61-8f50-5aae-8e7d-d9abc821ae0a" }
            };

            new PersonalRouteRefTranslator().Inject(_repo, entity, dom, resolver);

            Assert.True(_repo.HasManagedComponent<InitialRouteIntent>(entity));
            var intent = ((ISimulationView)_repo).GetManagedComponentRO<InitialRouteIntent>(entity);
            Assert.Equal(Fdp.Core.RefSpec.ByExact(System.Guid.Parse("378acf61-8f50-5aae-8e7d-d9abc821ae0a")), intent!.RouteNetworkId.Spec);
        }

        [Fact]
        public void PersonalRouteRefTranslator_CanTranslate_ReturnsFalseWhenAbsent()
        {
            var entity = _repo.CreateEntity();
            Assert.False(new PersonalRouteRefTranslator().CanTranslate(_repo, entity));
        }
    }
}
