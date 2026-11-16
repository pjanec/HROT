using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Translators;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-3 (docs/designs/animals/DESIGN_Animals.md §6.1) — an animal's <see cref="AnimalState"/> and a flock's
    /// <see cref="FlockState"/> cross from the node running its brain to every other node (the presentation's pose, gait and birds), ON CHANGE
    /// only; a non-owner publishes nothing; the recorded owner never takes its own sample back; the pair rides the shared pack on every node.
    /// 🔴 Red-proofs: the egress change-compare removed ⇒ the "unchanged" assert red; the ingress dropping the band ⇒ red.
    /// </summary>
    [Collection("SimHostDds")]
    public sealed class AnimalStateWireTests
    {
        private sealed class Capture<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private static (EntityRepository World, NetworkEntityMap Map, Entity Cow) Node(int primaryOwner, int localNode)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<AnimalState>();
            w.RegisterComponent<FlockState>();
            var map = new NetworkEntityMap();
            var cow = w.CreateEntity();
            w.AddComponent(cow, new NetworkIdentity { Value = 71 });
            w.AddComponent(cow, new NetworkAuthority(primaryOwner, localNode));
            map.Register(71, cow);
            return (w, map, cow);
        }

        [Fact]
        public void CE4623_TheBrainNode_PublishesTheAnimalState_OnChangeOnly_ANonOwnerNothing()
        {
            var (w, _, cow) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new Capture<EntityAnimalState>();
                var egress = new EntityAnimalStateEgressTranslator(wire);
                w.AddComponent(cow, new AnimalState { Species = AnimalSpecies.Cattle, Mode = AnimalMode.Calm });
                egress.ScanAndPublish(w);
                var first = Assert.Single(wire.Written);
                Assert.Equal((71, (byte)AnimalSpecies.Cattle, (byte)0), (first.EntityId, first.Species, first.FearBand));
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);                                  // unchanged: nothing sent

                w.GetComponentRW<AnimalState>(cow).FearBand = 3;
                w.GetComponentRW<AnimalState>(cow).Mode = AnimalMode.Flee;
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(((byte)3, (byte)AnimalMode.Flee), (wire.Written[1].FearBand, wire.Written[1].Mode));
            }

            var (other, _, theirs) = Node(primaryOwner: 2, localNode: 1);
            using (other)
            {
                var wire = new Capture<EntityAnimalState>();
                other.AddComponent(theirs, new AnimalState { FearBand = 2 });
                new EntityAnimalStateEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        [Fact]
        public void CE4623_ANonOwnersIngress_WritesTheAnimalAndFlockState_TheOwnerNeverTakesItBack()
        {
            var (w, map, cow) = Node(primaryOwner: 2, localNode: 1);
            using (w)
            {
                w.AddComponent(cow, new AnimalState { Species = AnimalSpecies.Cattle });   // the spawn's own calm state on this node
                var sample = EntityAnimalStateEgressTranslator.ToSample(71, new AnimalState
                    { Species = AnimalSpecies.Cattle, Mode = AnimalMode.Flee, FearBand = 3, Flags = AnimalFlags.Calling });
                using (var cmd = new EntityCommandBuffer())
                {
                    new EntityAnimalStateIngressTranslator(null, map).Apply(in sample, cmd, w);
                    cmd.Playback(w);
                }
                var s = w.GetComponentRO<AnimalState>(cow);
                Assert.Equal((AnimalMode.Flee, (byte)3, AnimalFlags.Calling), (s.Mode, s.FearBand, s.Flags));

                var flock = EntityFlockStateEgressTranslator.ToSample(71, new FlockState { Count = 31, InitialCount = 40, Seed = 7, Mode = FlockMode.Flying });
                using (var cmd = new EntityCommandBuffer())
                {
                    new EntityFlockStateIngressTranslator(null, map).Apply(in flock, cmd, w);
                    cmd.Playback(w);
                }
                var f = w.GetComponentRO<FlockState>(cow);
                Assert.Equal(((ushort)31, (ushort)40, 7u, FlockMode.Flying), (f.Count, f.InitialCount, f.Seed, f.Mode));
            }

            var (owner, ownerMap, own) = Node(primaryOwner: 1, localNode: 1);
            using (owner)
            {
                owner.AddComponent(own, new AnimalState { FearBand = 1 });
                using var cmd = new EntityCommandBuffer();
                new EntityAnimalStateIngressTranslator(null, ownerMap).Apply(new EntityAnimalState { EntityId = 71, FearBand = 3 }, cmd, owner);
                cmd.Playback(owner);
                Assert.Equal(1, owner.GetComponentRO<AnimalState>(own).FearBand);
            }
        }

        [Fact]
        public void CE4623_TheSharedPack_CarriesBothAnimalPairs_OnEveryNode()
        {
            using var participant = new DdsParticipant(231u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            Assert.Equal(new[] { GlobalComponentIds.AnimalState }, Assert.Single(pack.OfType<EntityAnimalStateEgressTranslator>()).TargetComponentIds);
            Assert.Single(pack.OfType<EntityAnimalStateIngressTranslator>());
            Assert.Equal((long)EDescriptorType.dtFlockState, Assert.Single(pack.OfType<EntityFlockStateEgressTranslator>()).DescriptorOrdinal);
            Assert.Single(pack.OfType<EntityFlockStateIngressTranslator>());
        }
    }
}
