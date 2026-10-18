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

        /// <summary>⭐ CE-3251 (BR-13) — a superior's reporting requirements (CE-2291 CC-2) for a subordinate whose brain runs elsewhere
        /// go there as kind SetReporting; for a brain owned here nothing goes on the wire.</summary>
        [Fact]
        public void CE3251_ReportingRequirementsForARemoteBrain_AreForwarded_AndForALocalOne_StayHere()
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

            _world.Bus.Publish(new Fdp.Toolkit.Squad.Command.SetReportingEvent { Entity = remote, Origin = BehaviorOrigin.Superior,
                Ffir = Fdp.Toolkit.Squad.Command.FfirFlags.LaceAfterContact, SitrepEverySeconds = 60f });
            _world.Bus.Publish(new Fdp.Toolkit.Squad.Command.SetReportingEvent { Entity = local, SitrepEverySeconds = 10f });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            var sent = Assert.Single(writer.Written);
            Assert.Equal((TacticalIntentWireKind.SetReporting, 42L, (byte)BehaviorOrigin.Superior), (sent.Kind, sent.TargetEntityId, sent.Origin));
            Assert.True(ReportingDirectiveJson.TryDecode(sent.JsonParams, remote, BehaviorOrigin.Superior, out var back));
            Assert.Equal((Fdp.Toolkit.Squad.Command.FfirFlags.LaceAfterContact, 60f), (back.Ffir, back.SitrepEverySeconds));
        }

        /// <summary>⭐ CE-3261 (behaviors' sectors note ①) — a superior's SECTOR (CE-2282) for a man whose brain runs elsewhere goes there as
        /// kind SetEngagementArea with every field (the arc, the Allow area, the watch, the NoFire ids); for a brain owned here nothing goes.
        /// ✅ Red-proof: ForwardEngagementArea not called ⇒ nothing written ⇒ red.</summary>
        [Fact]
        public void CE3261_ASectorForARemoteBrain_IsForwarded_WithEveryField_AndForALocalOne_StaysHere()
        {
            var (translator, writer) = BuildTranslator();
            _world.Bus.Register<SetEngagementAreaEvent>();
            var remote = _world.CreateEntity();
            _world.AddComponent(remote, new BehaviorState());   // authority NOT set: the brain runs elsewhere
            _entityMap.Register(42L, remote);
            var local = _world.CreateEntity();
            _world.AddComponent(local, new BehaviorState());
            _world.SetAuthority<BehaviorState>(local, true);
            _entityMap.Register(43L, local);

            var evt = new SetEngagementAreaEvent
            {
                Entity = remote, Origin = BehaviorOrigin.Superior, ArcSet = 1, ArcOrigin = new System.Numerics.Vector3(10f, 20f, 1.5f),
                LeftBearing = 300f, RightBearing = 60f, MinRange = 5f, MaxRange = 400f, AllowArea = 4711L, WatchBearing = 15f, WatchSet = 1,
            };
            evt.NoFire.Items[0] = 4712L; evt.NoFire.Items[1] = 4713L; evt.NoFire.Count = 2;
            _world.Bus.Publish(evt);
            _world.Bus.Publish(new SetEngagementAreaEvent { Entity = local, ArcSet = 1 });
            _world.Bus.SwapBuffers();
            translator.ScanAndPublish(_world);

            var sent = Assert.Single(writer.Written);
            Assert.Equal((TacticalIntentWireKind.SetEngagementArea, 42L, (byte)BehaviorOrigin.Superior), (sent.Kind, sent.TargetEntityId, sent.Origin));
            Assert.True(EngagementAreaDirectiveJson.TryDecode(sent.JsonParams, remote, BehaviorOrigin.Superior, out var back));
            Assert.Equal((evt.ArcSet, evt.ArcOrigin, evt.LeftBearing, evt.RightBearing, evt.MinRange, evt.MaxRange),
                         (back.ArcSet, back.ArcOrigin, back.LeftBearing, back.RightBearing, back.MinRange, back.MaxRange));
            Assert.Equal((4711L, 15f, (byte)1), (back.AllowArea, back.WatchBearing, back.WatchSet));
            Assert.Equal((2, 4712L, 4713L), (back.NoFire.Count, back.NoFire.Items[0], back.NoFire.Items[1]));
        }
    }
}
