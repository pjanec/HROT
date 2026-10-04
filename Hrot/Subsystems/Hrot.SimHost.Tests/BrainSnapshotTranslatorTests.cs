using System;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Configuration;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3042</c> (R-192) — the scenario saves a unit's AI as a snapshot of what is needed and starts it through the
    /// ingress on load. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.5. Runs the PRODUCTION serializer
    /// (<see cref="HrotScenarioSerializerFactory"/>) and the PRODUCTION behaviour registry.
    /// </summary>
    public sealed class BrainSnapshotTranslatorTests
    {
        private sealed class Node
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry;
            public readonly BehaviorIngressSystem Ingress;
            public readonly InitialBrainMaterializationSystem Materialize = new();
            public readonly RoeSystem Roe = new();

            public Node(BehaviorRegistry registry)
            {
                Registry = registry;
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                Ingress = new BehaviorIngressSystem(registry);
            }

            public Entity Unit()
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new BehaviorState());
                return e;
            }

            /// <summary>One Input phase: materialize, ROE, ingress (events of the previous frame).</summary>
            public void Frame()
            {
                Repo.Bus.SwapBuffers();
                Materialize.Execute(Repo, 0.016f);
                Roe.Execute(Repo, 0.016f);
                Ingress.Execute(Repo, 0.016f);
            }

            public string? TaskName(Entity e)
                => Registry.TryGetName(Repo.GetComponent<BehaviorState>(e).ActiveBehaviorHash, out var n) ? n : null;
        }

        private static BehaviorRegistry Production()
        {
            var r = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(r);
            return r;
        }

        private static Entity Only(EntityRepository repo)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetMetadata(i).Generation);
                if (repo.IsAlive(e)) return e;
            }
            throw new InvalidOperationException("no entity");
        }

        [Fact]
        public void CE3042_AnOrderedUnit_SavesItsTaskSopAndRoe_AndReloadsThemThroughTheGate_AtTheirOrigins()
        {
            var registry = Production();
            var a = new Node(registry);
            var unit = a.Unit();
            a.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            a.Repo.Bus.PublishManaged(new AssignSopEvent { Entity = unit, BehaviorName = "BasicInfantrySop", JsonParams = "{}", Origin = BehaviorOrigin.Operator });
            a.Repo.Bus.Publish(new SetRoeEvent { Entity = unit, Fire = RoeFire.HoldFire, Reactions = RoeReactions.StayOnTask, Origin = BehaviorOrigin.Superior });
            a.Frame();
            Assert.Equal("Idle", a.TaskName(unit));

            var serializer = HrotScenarioSerializerFactory.Build(registry);
            var dom = serializer.Serialize(a.Repo, new ScenarioHeader(Hrot.Common.Scenario.HrotSubsystemTypes.Scenario));
            string json = dom.ToJsonString();
            Assert.DoesNotContain("ActiveBehaviorHash", json);                 // never a hash / run token (R-192)
            Assert.DoesNotContain("InstanceId", json);

            var b = new Node(registry);
            serializer.Deserialize(b.Repo, dom);
            var loaded = Only(b.Repo);
            if (!b.Repo.HasComponent<BehaviorState>(loaded)) b.Repo.AddComponent(loaded, new BehaviorState());
            Assert.True(b.Repo.HasManagedComponent<InitialBrainIntent>(loaded));

            b.Frame();   // materialize → events
            b.Frame();   // ingress / ROE consume them

            Assert.False(b.Repo.HasManagedComponent<InitialBrainIntent>(loaded));
            Assert.Equal("Idle", b.TaskName(loaded));
            Assert.Equal(BehaviorOrigin.Superior, b.Repo.GetComponent<BehaviorState>(loaded).Origin);
            Assert.True(registry.TryGetId("BasicInfantrySop", out int sopId));
            Assert.Equal(sopId, b.Repo.GetComponent<SopState>(loaded).SopHash);
            Assert.Equal(BehaviorOrigin.Operator, b.Repo.GetComponent<SopState>(loaded).SopOrigin);
            var roe = b.Repo.GetComponent<Roe>(loaded);
            Assert.Equal(RoeFire.HoldFire, roe.Fire);
            Assert.Equal(RoeReactions.StayOnTask, roe.Reactions);
            Assert.Equal(BehaviorOrigin.Superior, roe.SetBy);
        }

        [Fact]
        public void CE3042_WhatTheTemplateOrTheSopChose_IsNotSaved()
        {
            var registry = Production();
            var a = new Node(registry);
            var unit = a.Unit();
            a.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Sop });
            a.Repo.Bus.PublishManaged(new AssignSopEvent { Entity = unit, BehaviorName = "BasicInfantrySop", JsonParams = "{}", Origin = BehaviorOrigin.Sop });
            a.Repo.AddComponent(unit, new Roe { Fire = RoeFire.FireAtWill, SetBy = BehaviorOrigin.Unmarked });   // the TKB default
            a.Frame();

            var saved = new BrainSnapshotTranslator(registry).Extract(a.Repo, unit, new StubGuidResolver());
            Assert.Empty(saved);
        }

        [Fact]
        public void CE3042_DuringAReaction_TheTaskItPausedIsSaved_NotTheReaction()
        {
            var registry = Production();
            var a = new Node(registry);
            var unit = a.Unit();
            a.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Operator });
            a.Frame();
            a.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = unit, BehaviorName = "Demo_TakeCover", JsonParams = "{}", Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.Hit });
            a.Frame();
            Assert.Equal("Demo_TakeCover", a.TaskName(unit));

            var saved = new BrainSnapshotTranslator(registry).Extract(a.Repo, unit, new StubGuidResolver());
            var node = (System.Text.Json.Nodes.JsonNode)saved[BrainSnapshotTranslator.BehaviorKey];
            Assert.Equal("Idle", (string?)node["Name"]);
            Assert.Equal("Operator", (string?)node["Origin"]);
        }

        [Fact]
        public void CE3042_ASlotAMissionPlanDrives_IsNotSaved_ThePlanIs()
        {
            var registry = Production();
            var a = new Node(registry);
            var unit = a.Unit();
            a.Repo.AddComponent(unit, new MissionPlanQueue { PhaseCount = 2, CurrentPhase = 0 });
            a.Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = unit, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            a.Frame();

            var saved = new BrainSnapshotTranslator(registry).Extract(a.Repo, unit, new StubGuidResolver());
            Assert.False(saved.ContainsKey(BrainSnapshotTranslator.BehaviorKey));
        }
    }
}
