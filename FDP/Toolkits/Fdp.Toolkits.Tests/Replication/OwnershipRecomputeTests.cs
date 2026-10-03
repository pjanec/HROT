using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐ Ownership build S5 (R-159): the record follows the claim, except a granted descriptor still in its F7
    /// window. And the shared <see cref="OwnershipApplier"/>'s master-move pin (Transfer design §3, <c>MasterOnly</c>).
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S5.
    /// </summary>
    public class OwnershipRecomputeTests
    {
        private const int  Local     = 1;
        private const int  Remote    = 2;
        private const long Master    = 7;   // stand-in for dtEntityMaster: NetworkIdentity
        private const long Info      = 8;   // a creator-kept descriptor: TkbIdentity
        private const long Kinematic = 9;   // a granted descriptor: NetworkTransform + NetworkVelocity
        private const long NetId     = 4242;

        private static long Key(long d) => OwnershipExtensions.PackKey(d, 0);

        private sealed class Node
        {
            public EntityRepository Repo = null!;
            public Entity E;
            public NetworkEntityMap Map = null!;
            public DescriptorOwnershipMap Descriptors = null!;
            public OwnershipIngressSystem Ingress = null!;
            public OwnershipRecomputeSystem Recompute = null!;

            public bool RecordMine(long d) => ((ISimulationView)Repo).HasAuthority(E, Key(d));
            public bool Claims<T>() where T : unmanaged => Repo.HasAuthority<T>(E);
            public int? Recorded(long d)
                => Repo.HasManagedComponent<DescriptorOwnership>(E) &&
                   Repo.GetComponent<DescriptorOwnership>(E).TryGetOwner(Key(d), out int o) ? o : null;

            /// <summary>One frame of the Input phase: ingress, then the recompute (its declared order).</summary>
            public void Frame()
            {
                Repo.Bus.SwapBuffers();
                Ingress.Execute(Repo, 0f);
                Recompute.Execute(Repo, 0f);
            }

            public void Update(long d, int newOwner, int origin)
                => Repo.Bus.Publish(new OwnershipUpdate
                {
                    NetworkId = new NetworkIdentity(NetId), PackedKey = Key(d), NewOwnerNodeId = newOwner, OriginNodeId = origin,
                });
        }

        /// <summary>A node <paramref name="local"/> holding the entity, primary owner <paramref name="primary"/>,
        /// claiming every component iff it is the primary owner (a creator, or a replica).</summary>
        private static Node Build(int local, int primary)
        {
            var n = new Node { Repo = new EntityRepository() };
            n.Repo.RegisterComponent<NetworkIdentity>();
            n.Repo.RegisterComponent<NetworkAuthority>();
            n.Repo.RegisterComponent<TkbIdentity>();
            n.Repo.RegisterComponent<NetworkTransform>();
            n.Repo.RegisterComponent<NetworkVelocity>();
            n.Repo.RegisterManagedComponent<DescriptorOwnership>();
            n.Repo.RegisterManagedComponent<OutgoingGrantsPending>();
            n.Repo.RegisterComponent<PartMetadata>();
            n.Repo.RegisterEvent<OwnershipUpdate>();
            n.Repo.RegisterEvent<ConstructionOrder>();
            n.Repo.RegisterEvent<Fdp.Toolkit.Replication.Messages.DescriptorAuthorityChanged>();

            n.E = n.Repo.CreateEntity();
            n.Repo.AddComponent(n.E, new NetworkIdentity(NetId));
            n.Repo.AddComponent(n.E, new NetworkAuthority(primary, local));
            n.Repo.AddComponent(n.E, new TkbIdentity { TkbType = 1 });
            n.Repo.AddComponent(n.E, new NetworkTransform());
            n.Repo.AddComponent(n.E, new NetworkVelocity());
            bool creator = primary == local;
            n.Repo.SetAuthority<NetworkIdentity>(n.E, creator);
            n.Repo.SetAuthority<TkbIdentity>(n.E, creator);
            n.Repo.SetAuthority<NetworkTransform>(n.E, creator);
            n.Repo.SetAuthority<NetworkVelocity>(n.E, creator);

            n.Map = new NetworkEntityMap();
            n.Map.Register(NetId, n.E);

            n.Descriptors = new DescriptorOwnershipMap { PrimaryOwnerDescriptorOrdinal = Master };
            n.Descriptors.RegisterMapping(Master, ComponentType<NetworkIdentity>.ID);
            n.Descriptors.RegisterMapping(Info, ComponentType<TkbIdentity>.ID);
            n.Descriptors.RegisterMapping(Kinematic, ComponentType<NetworkTransform>.ID, ComponentType<NetworkVelocity>.ID);

            n.Ingress   = new OwnershipIngressSystem(n.Map, local, n.Descriptors);
            n.Recompute = new OwnershipRecomputeSystem(n.Map, local, n.Descriptors);
            return n;
        }

        /// <summary>What the creator's yield does for a grant of <paramref name="d"/> (NedReplicationModule).</summary>
        private static void YieldGrant(Node n, long d)
        {
            foreach (int cid in n.Descriptors.GetComponentIdsForDescriptor(d))
                n.Repo.SetAuthority(n.E, cid, false);
            var pending = new OutgoingGrantsPending();
            pending.Descriptors[d] = Remote;
            n.Repo.SetManagedComponent(n.E, pending);
        }

        // ── F7: the creator keeps publishing a granted descriptor until the grantee confirms ─────────────────

        [Fact]
        public void F7_TheCreatorsRecordStaysMine_UntilTheGranteeConfirms_ThenFollowsIt()
        {
            var creator = Build(Local, primary: Local);
            YieldGrant(creator, Kinematic);

            // Frame: something else about the entity changes (an unrelated update, a construction order).
            creator.Repo.Bus.Publish(new ConstructionOrder { Entity = creator.E });
            creator.Update(Info, Local, Local);
            creator.Frame();

            Assert.False(creator.Claims<NetworkTransform>());      // the claim left at the yield…
            Assert.True(creator.RecordMine(Kinematic));            // …but the record still sends the first samples (F7)
            Assert.Null(creator.Recorded(Kinematic));
            Assert.True(creator.Repo.HasManagedComponent<OutgoingGrantsPending>(creator.E));

            // The grantee takes over and confirms.
            creator.Update(Kinematic, Remote, Remote);
            creator.Frame();

            Assert.False(creator.RecordMine(Kinematic));
            Assert.Equal(Remote, creator.Recorded(Kinematic));
            Assert.False(creator.Repo.HasManagedComponent<OutgoingGrantsPending>(creator.E));   // window closed
            Assert.True(creator.RecordMine(Info));                 // the creator's remainder is untouched
        }

        // ── The recompute: the record is made to agree with the claim, both ways ────────────────────────────

        [Fact]
        public void AClaimedDescriptorWhoseRecordSaysOtherwise_IsRecordedAsMine()
        {
            var n = Build(Local, primary: Local);
            n.Repo.GetComponent<DescriptorOwnership>(EnsureRecord(n)).SetOwner(Key(Info), Remote);   // stale record
            n.Repo.Bus.Publish(new ConstructionOrder { Entity = n.E });
            n.Frame();

            Assert.Equal(Local, n.Recorded(Info));
        }

        /// <summary>
        /// ⚠ Measured (<c>EqsTranslatorTests.T8</c> red after S6): <c>AddComponent</c> sets no claim, so a component the
        /// owner adds after birth is unclaimed. The record says "mine" — the claim follows it. (The first S5 rule wrote
        /// "not me" here and the owner stopped publishing what it owns.)
        /// </summary>
        [Fact]
        public void AComponentTheOwnerAddsAfterBirth_TakesTheRecordsClaim_AndTheRecordStaysMine()
        {
            var n = Build(Local, primary: Local);
            // The state a component added after spawn is in: present, unclaimed. (Set directly — RemoveComponent keeps
            // the claim bit, so a remove + re-add of the same type would not reproduce it.)
            n.Repo.SetAuthority<TkbIdentity>(n.E, false);
            Assert.False(n.Claims<TkbIdentity>());

            n.Update(Kinematic, Remote, Remote);                              // any ownership change for the entity
            n.Frame();

            Assert.True(n.Claims<TkbIdentity>());
            Assert.True(n.RecordMine(Info));
            Assert.Null(n.Recorded(Info));
            Assert.Equal(1, n.Recompute.LateComponentsClaimed);
        }

        /// <summary>⭐ <c>CE-3001</c> (S8) — the same late component with NO ownership or construction event: it is claimed
        /// in the next frame anyway. 📌 Found live: a blueprint's <c>BlueprintBlackboard256</c>, added after spawn, stayed
        /// unclaimed on its owner on every hill-attack tank, because the claim was only fixed when an event next touched the
        /// entity. 📄 <c>DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S8.</summary>
        [Fact]
        public void AComponentTheOwnerAddsAfterBirth_IsClaimedNextFrame_WithNoOwnershipEvent()
        {
            var n = Build(Local, primary: Local);
            n.Repo.SetAuthority<TkbIdentity>(n.E, false);                     // present, unclaimed: added after spawn

            n.Frame();                                                         // no event at all

            Assert.True(n.Claims<TkbIdentity>());
            Assert.Equal(1, n.Recompute.LateComponentsClaimed);
            Assert.Null(n.Recorded(Info));                                     // the record is not written
        }

        /// <summary>⭐ <c>CE-3001</c> — the per-frame pass claims only what the RECORD gives this node: a replica's late
        /// component stays unclaimed, and a descriptor still handing over (the creator's F7 window) is not re-claimed.</summary>
        [Fact]
        public void TheLateClaimPass_NeverClaimsForAReplica_NorDuringAPendingHandover()
        {
            var replica = Build(Local, primary: Remote);
            replica.Frame();
            Assert.False(replica.Claims<TkbIdentity>());

            var creator = Build(Local, primary: Local);
            YieldGrant(creator, Kinematic);                                    // claim cleared, handover pending
            creator.Frame();
            Assert.False(creator.Claims<NetworkTransform>());
            Assert.Equal(0, creator.Recompute.LateComponentsClaimed);
        }

        [Fact]
        public void WhereClaimAndRecordAgree_NothingIsWritten()
        {
            var replica = Build(Local, primary: Remote);           // claims nothing, record follows the remote primary
            replica.Repo.Bus.Publish(new ConstructionOrder { Entity = replica.E });
            replica.Frame();

            Assert.False(replica.Repo.HasManagedComponent<DescriptorOwnership>(replica.E));
        }

        [Fact]
        public void APartlyClaimedDescriptorRecordedElsewhere_IsLeftAlone_AndCounted()
        {
            var n = Build(Local, primary: Local);
            EnsureRecord(n);
            n.Repo.GetComponent<DescriptorOwnership>(n.E).SetOwner(Key(Kinematic), Remote);
            n.Repo.SetAuthority<NetworkVelocity>(n.E, false);      // Kinematic split: Transform claimed, Velocity not
            n.Repo.Bus.Publish(new ConstructionOrder { Entity = n.E });
            n.Frame();

            Assert.Equal(Remote, n.Recorded(Kinematic));
            Assert.Equal(1, n.Recompute.SplitDescriptorsSkipped);
        }

        // ── MasterOnly: every node keeps the other descriptors where they were (Transfer design §3) ─────────

        [Theory]
        [InlineData(Local)]     // the giver
        [InlineData(Remote)]    // the receiver
        [InlineData(3)]         // a third node
        public void AMasterMove_LeavesEveryOtherDescriptorWithTheOldOwner_OnEveryNode(int node)
        {
            var n = Build(node, primary: Local);
            n.Update(Master, Remote, Local);
            n.Frame();

            Assert.Equal(Remote, n.Repo.GetComponentRO<NetworkAuthority>(n.E).PrimaryOwnerId);
            Assert.Equal(Local, n.Recorded(Info));
            Assert.Equal(Local, n.Recorded(Kinematic));
            Assert.Equal(node == Local, n.RecordMine(Info));       // the giver still publishes what it still writes
            Assert.Equal(node == Local, n.Claims<TkbIdentity>());  // and the claim agrees: one truth
        }

        // ── S6: a part's claim follows its record — the group's, unless its instance was moved on its own ────────

        private static Entity AddPart(Node n, int instance)
        {
            var part = n.Repo.CreateEntity();
            n.Repo.AddComponent(part, new PartMetadata { ParentEntity = n.E, InstanceId = instance });
            n.Repo.AddComponent(part, new TkbIdentity { TkbType = 1 });   // carries Info
            n.Repo.AddComponent(part, new NetworkTransform());           // carries Kinematic (one of its two components)
            return part;
        }

        [Fact]
        public void ANewPart_TakesTheClaimOfItsRootsGroups()
        {
            var creator = Build(Local, primary: Local);
            EnsureRecord(creator);
            creator.Repo.GetComponent<DescriptorOwnership>(creator.E).SetOwner(Key(Kinematic), Remote);   // granted away
            var part = AddPart(creator, instance: 2);

            creator.Frame();   // no event needed: the parts pass runs every frame

            Assert.True(creator.Repo.HasAuthority<TkbIdentity>(part));        // (Info,2) → (Info,0) none → primary: us
            Assert.False(creator.Repo.HasAuthority<NetworkTransform>(part));  // (Kinematic,2) → (Kinematic,0): Remote
        }

        [Fact]
        public void APerInstanceUpdate_MovesOnlyThatPart_AndNeverTheRoot()
        {
            var n = Build(Local, primary: Local);
            var two   = AddPart(n, instance: 2);
            var three = AddPart(n, instance: 3);
            n.Frame();
            Assert.True(n.Repo.HasAuthority<NetworkTransform>(three));

            n.Repo.Bus.Publish(new OwnershipUpdate
            {
                NetworkId = new NetworkIdentity(NetId), PackedKey = OwnershipExtensions.PackKey(Kinematic, 3),
                NewOwnerNodeId = Remote, OriginNodeId = Remote,
            });
            n.Frame();

            Assert.False(n.Repo.HasAuthority<NetworkTransform>(three));   // that instance moved
            Assert.True(n.Repo.HasAuthority<NetworkTransform>(two));      // its sibling did not
            Assert.True(n.Claims<NetworkTransform>());                    // nor did the root (Q79 §0.10 ③)
            Assert.True(n.RecordMine(Kinematic));
        }

        private static Entity EnsureRecord(Node n)
        {
            if (!n.Repo.HasManagedComponent<DescriptorOwnership>(n.E))
                n.Repo.SetManagedComponent(n.E, new DescriptorOwnership());
            return n.E;
        }
    }
}
