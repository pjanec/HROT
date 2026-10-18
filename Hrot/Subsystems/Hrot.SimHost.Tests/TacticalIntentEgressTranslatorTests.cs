using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class TacticalIntentEgressTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public TacticalIntentEgressTranslatorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<BehaviorState>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        private (TacticalIntentEgressTranslator translator, CapturingWriter<TacticalIntentRequest> writer)
            BuildTranslator()
        {
            var writer     = new CapturingWriter<TacticalIntentRequest>();
            var translator = new TacticalIntentEgressTranslator(writer, _entityMap);
            return (translator, writer);
        }

        // SC-1: Entity in map, no BehaviorState authority -> DDS write happens
        [Fact]
        public void ScanAndPublish_NoAuthority_WritesDdsSample()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            // Authority NOT set (remote entity)
            _entityMap.Register(42L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(1, writer.Written.Count);
            Assert.Equal(42L, writer.Written[0].TargetEntityId);
            Assert.Equal("DefendArea", writer.Written[0].IntentId);
            Assert.Equal(1, translator.SentSampleCount);
        }

        // SC-2: Entity NOT in NetworkEntityMap -> no DDS write
        [Fact]
        public void ScanAndPublish_EntityNotInMap_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            // Entity not registered in entityMap

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }

        // SC-3: Two events, no authority for either -> two DDS writes
        [Fact]
        public void ScanAndPublish_TwoEvents_NoAuthority_TwoWrites()
        {
            var (translator, writer) = BuildTranslator();

            var e1 = _world.CreateEntity();
            _world.AddComponent(e1, new BehaviorState());
            _entityMap.Register(1L, e1);

            var e2 = _world.CreateEntity();
            _world.AddComponent(e2, new BehaviorState());
            _entityMap.Register(2L, e2);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e1, IntentId = "DefendArea", JsonParams = "{}" });
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = e2, IntentId = "ConvoyEscort", JsonParams = "{}" });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Equal(2, writer.Written.Count);
            Assert.Equal(2, translator.SentSampleCount);
        }

        // SC-4: Entity HAS BehaviorState authority -> no DDS write
        [Fact]
        public void ScanAndPublish_HasAuthority_NoDdsWrite()
        {
            var (translator, writer) = BuildTranslator();

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            _world.SetAuthority<BehaviorState>(entity, true);  // locally owned
            _entityMap.Register(99L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = entity,
                IntentId   = "DefendArea",
                JsonParams = "{}",
            });
            _world.Bus.SwapBuffers();

            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
            Assert.Equal(0, translator.SentSampleCount);
        }

        /// <summary>⭐ CE-3167 — the order's Kind and ClearsPriorIntent leave on the wire (Kind byte, Flags bit 0).</summary>
        [Fact]
        public void CE3167_KindAndClearsPriorIntent_CrossTheWire()
        {
            var (translator, writer) = BuildTranslator();
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            _entityMap.Register(42L, entity);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity = entity, Kind = TacticalIntentKind.Clear, ClearsPriorIntent = true,
                Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Operator,
            });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            Assert.Single(writer.Written);
            Assert.Equal((byte)TacticalIntentKind.Clear, writer.Written[0].Kind);
            Assert.Equal(TacticalIntentFlags.ClearsPriorIntent, writer.Written[0].Flags);
        }

        /// <summary>⭐ CE-3167 — on a node that registers no brain state (a SimHost / map node) the egress forwards the order instead
        /// of throwing: such a node owns no brain.</summary>
        [Fact]
        public void CE3167_ANodeWithoutBrainState_ForwardsTheOrder()
        {
            using var bare = new EntityRepository();
            var writer = new CapturingWriter<TacticalIntentRequest>();
            var map = new NetworkEntityMap();
            var translator = new TacticalIntentEgressTranslator(writer, map);
            var entity = bare.CreateEntity();
            map.Register(7L, entity);

            bare.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = entity, IntentId = "Idle" });
            bare.Bus.SwapBuffers();
            translator.ScanAndPublish(bare);

            Assert.Equal(7L, Assert.Single(writer.Written).TargetEntityId);
        }

        /// <summary>⭐ CE-3251 (BR-13) — an ROE change for a unit whose brain runs elsewhere goes to that node on the order's road:
        /// kind SetRoe, its fields as JSON, the origin beside them. An order and an ROE raised in one frame leave in that order.</summary>
        [Fact]
        public void CE3251_AnRoeForARemoteBrain_IsForwarded_AfterTheFramesOrder()
        {
            var (translator, writer) = BuildTranslator();
            _world.Bus.Register<SetRoeEvent>();
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());   // authority NOT set: the brain runs elsewhere
            _entityMap.Register(42L, entity);

            _world.Bus.Publish(new SetRoeEvent { Entity = entity, Fire = RoeFire.HoldFire, Origin = BehaviorOrigin.Superior,
                                                 ReturnFireWindowSeconds = 7.5f, ForceLevel = RoeForceLevel.NonLethal });
            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = entity, IntentId = "DefendArea", JsonParams = "{}" });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            Assert.Equal(2, writer.Written.Count);
            Assert.Equal(TacticalIntentWireKind.Assign, writer.Written[0].Kind);
            var roe = writer.Written[1];
            Assert.Equal(TacticalIntentWireKind.SetRoe, roe.Kind);
            Assert.Equal(42L, roe.TargetEntityId);
            Assert.Equal((byte)BehaviorOrigin.Superior, roe.Origin);
            Assert.True(RoeDirectiveJson.TryDecode(roe.JsonParams, entity, BehaviorOrigin.Superior, out var back));
            Assert.Equal(RoeFire.HoldFire, back.Fire);
            Assert.Equal(RoeReactions.ReactionsUnset, back.Reactions);
            Assert.Equal(7.5f, back.ReturnFireWindowSeconds);
            Assert.Equal(RoeForceLevel.NonLethal, back.ForceLevel);
            Assert.Equal(2, translator.SentSampleCount);
        }

        /// <summary>⭐ <c>CE-2299</c> (behaviors, CC-2) — a superior's reporting requirements for a unit whose brain runs elsewhere go there on
        /// the order's road as kind 3, after the frame's order; for a brain owned here nothing goes on the wire.</summary>
        [Fact]
        public void CE2299_ReportingForARemoteBrain_IsForwarded_ForALocalOne_Stays()
        {
            var (translator, writer) = BuildTranslator();
            _world.Bus.Register<Fdp.Toolkit.Squad.Command.SetReportingEvent>();
            var remote = _world.CreateEntity();
            _world.AddComponent(remote, new BehaviorState());   // authority NOT set: the brain runs elsewhere
            _entityMap.Register(42L, remote);
            var local = _world.CreateEntity();
            _world.AddComponent(local, new BehaviorState());
            _world.SetAuthority<BehaviorState>(local, true);
            _entityMap.Register(43L, local);

            _world.Bus.PublishManaged(new AssignTacticalIntentEvent { Entity = remote, IntentId = "Order.Move", JsonParams = "{}" });
            _world.Bus.Publish(new Fdp.Toolkit.Squad.Command.SetReportingEvent { Entity = remote, Origin = BehaviorOrigin.Superior,
                Pir = Fdp.Toolkit.Squad.Command.PirFlags.PirMachineGun, SitrepEverySeconds = 20f });
            _world.Bus.Publish(new Fdp.Toolkit.Squad.Command.SetReportingEvent { Entity = local, Origin = BehaviorOrigin.Superior,
                SitrepEverySeconds = 5f });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            Assert.Equal(2, writer.Written.Count);
            Assert.Equal(TacticalIntentWireKind.Assign, writer.Written[0].Kind);
            var req = writer.Written[1];
            Assert.Equal((TacticalIntentWireKind.SetReporting, 42L, (byte)BehaviorOrigin.Superior), (req.Kind, req.TargetEntityId, req.Origin));
            Assert.True(ReportingDirectiveJson.TryDecode(req.JsonParams, remote, BehaviorOrigin.Superior, out var back));
            Assert.Equal((Fdp.Toolkit.Squad.Command.PirFlags.PirMachineGun, 20f), (back.Pir, back.SitrepEverySeconds));
        }

        /// <summary>⭐ CE-3251 — the brain's own node applies the ROE itself; nothing goes on the wire.</summary>
        [Fact]
        public void CE3251_AnRoeForABrainOwnedHere_StaysHere()
        {
            var (translator, writer) = BuildTranslator();
            _world.Bus.Register<SetRoeEvent>();
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BehaviorState());
            _world.SetAuthority<BehaviorState>(entity, true);
            _entityMap.Register(42L, entity);

            _world.Bus.Publish(new SetRoeEvent { Entity = entity, Fire = RoeFire.HoldFire });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }
    }
}
