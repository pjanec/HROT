using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Network.Translators;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3048</c> (V7) — the brain intent on the wire: the owner of <c>dtBrainIntent</c> publishes what its unit runs
    /// when it changes (and only then); every other node keeps the last one, holding a sample for a unit it does not know
    /// yet. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// </summary>
    public sealed class BrainIntentTranslatorTests : IDisposable
    {
        private const int Local = 2, Other = 7;
        private static readonly long Key = OwnershipExtensions.PackKey((long)EDescriptorType.dtBrainIntent, 0);

        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public List<T> Disposed { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) => Disposed.Add(key);
        }

        private readonly EntityRepository _world = new();
        private readonly NetworkEntityMap _map = new();
        private readonly BehaviorRegistry _registry = new();
        private readonly int _taskId;

        public BrainIntentTranslatorTests()
        {
            _world.RegisterComponent<BehaviorState>();
            _world.RegisterComponent<SopState>();
            _world.RegisterComponent<Roe>();
            _world.RegisterComponent<NetworkIdentity>();
            _world.RegisterComponent<NetworkAuthority>();
            _world.RegisterManagedComponent<DescriptorOwnership>();
            _registry.Register("BiT_Task", new BehaviorDefinition { Name = "BiT_Task", BrainTier = BehaviorConstants.BrainTierBTree });
            Assert.True(_registry.TryGetId("BiT_Task", out _taskId));
        }

        public void Dispose() => _world.Dispose();

        private Entity Unit(long netId, int owner)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(netId));
            _world.AddComponent(e, new NetworkAuthority(owner, Local));
            _world.SetManagedComponent(e, new DescriptorOwnership { Map = new Dictionary<long, int> { [Key] = owner } });
            _world.AddComponent(e, new BehaviorState { ActiveBehaviorHash = _taskId, InstanceId = 1, Origin = BehaviorOrigin.Superior });
            _map.Register(netId, e);
            return e;
        }

        [Fact]
        public void CE3048_TheOwnerPublishes_OnceAndOnChange_InTheScenarioShape()
        {
            var writer = new CapturingWriter<EntityBrainIntent>();
            var egress = new BrainIntentEgressTranslator(writer, _registry);
            var e = Unit(1001, owner: Local);

            egress.ScanAndPublish(_world);
            egress.ScanAndPublish(_world);                                        // nothing moved
            Assert.Single(writer.Written);
            var sent = JsonSerializer.Deserialize<InitialBrainIntent>(writer.Written[0].IntentJson, FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Assert.Equal(1001, writer.Written[0].EntityId);
            Assert.Equal("BiT_Task", sent.Behavior!.Name);
            Assert.Equal(BehaviorOrigin.Superior, sent.Behavior.Origin);

            _world.AddComponent(e, new Roe { Fire = RoeFire.HoldFire, SetBy = BehaviorOrigin.Operator });
            egress.ScanAndPublish(_world);
            Assert.Equal(2, writer.Written.Count);
            var second = JsonSerializer.Deserialize<InitialBrainIntent>(writer.Written[1].IntentJson, FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Assert.Equal(RoeFire.HoldFire, second.Roe!.Fire);

            egress.Dispose(1001);
            Assert.Single(writer.Disposed);                                       // a gone unit ends its durable instance
        }

        [Fact]
        public void CE3048_ANodeThatDoesNotOwnTheIntent_PublishesNothing()
        {
            var writer = new CapturingWriter<EntityBrainIntent>();
            var egress = new BrainIntentEgressTranslator(writer, _registry);
            Unit(1002, owner: Other);
            egress.ScanAndPublish(_world);
            Assert.Empty(writer.Written);
        }

        [Fact]
        public void CE3048_EveryOtherNodeKeepsIt_EvenForAUnitThatArrivesLater_ButNotItsOwnSample()
        {
            var ingress = new BrainIntentIngressTranslator(null, _map);
            string json = JsonSerializer.Serialize(new InitialBrainIntent
            {
                Behavior = new SavedBrainSlot { Name = "BiT_Task", Params = "{}", Origin = BehaviorOrigin.Operator },
            }, FdpJsonOptionsRegistry.DefaultRelaxed);

            ingress.Receive(new EntityBrainIntent { EntityId = 1003, IntentJson = json });   // unit not known yet
            Play(ingress);
            var e = Unit(1003, owner: Other);
            Play(ingress);                                                        // held, now applied
            Assert.True(_world.HasManagedComponent<ReplicatedBrainIntent>(e));
            Assert.Equal(BehaviorOrigin.Operator, _world.GetComponent<ReplicatedBrainIntent>(e).Intent.Behavior!.Origin);

            var own = Unit(1004, owner: Local);
            ingress.Receive(new EntityBrainIntent { EntityId = 1004, IntentJson = json });
            Play(ingress);
            Assert.False(_world.HasManagedComponent<ReplicatedBrainIntent>(own));  // our own sample came back
        }

        private void Play(BrainIntentIngressTranslator ingress)
        {
            var cmd = new EntityCommandBuffer();
            ingress.Apply(cmd, _world);
            cmd.Playback(_world);
        }
    }
}
