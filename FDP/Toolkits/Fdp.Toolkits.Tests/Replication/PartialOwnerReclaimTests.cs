using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐ Ownership build S7 (R-167, Q79 §0.11): when a node leaves, what its record names returns to each entity's
    /// primary owner — on every node, by a direct call, no message — and a grant whose target left before taking over is
    /// taken back by its creator. 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.3, §5.6 S7.
    /// </summary>
    public class PartialOwnerReclaimTests
    {
        private const int  Creator   = 1;   // the primary owner
        private const int  Muscle    = 2;   // the grantee that leaves
        private const int  Bystander = 3;
        private const long Info      = 8;   // TkbIdentity
        private const long Kinematic = 9;   // NetworkTransform + NetworkVelocity

        private static long Key(long d, int i = 0) => OwnershipExtensions.PackKey(d, i);

        private static (EntityRepository Repo, Entity E, PartialOwnerReclaimSystem Sys) Node(int local, int primary)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<NetworkTransform>();
            repo.RegisterComponent<NetworkVelocity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterManagedComponent<OutgoingGrantsPending>();
            repo.RegisterEvent<NodeDeparted>();
            repo.RegisterEvent<Fdp.Toolkit.Replication.Messages.DescriptorAuthorityChanged>();

            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkAuthority(primary, local));
            repo.AddComponent(e, new TkbIdentity { TkbType = 1 });
            repo.AddComponent(e, new NetworkTransform());
            repo.AddComponent(e, new NetworkVelocity());

            var map = new DescriptorOwnershipMap();
            map.RegisterMapping(Info, ComponentType<TkbIdentity>.ID);
            map.RegisterMapping(Kinematic, ComponentType<NetworkTransform>.ID, ComponentType<NetworkVelocity>.ID);
            return (repo, e, new PartialOwnerReclaimSystem(local, map));
        }

        private static void Depart(EntityRepository repo, PartialOwnerReclaimSystem sys, int node)
        {
            repo.Bus.Publish(new NodeDeparted { NodeId = node });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
        }

        private static int? Recorded(EntityRepository repo, Entity e, long key)
            => repo.HasManagedComponent<DescriptorOwnership>(e) &&
               repo.GetComponent<DescriptorOwnership>(e).TryGetOwner(key, out int o) ? o : null;

        [Theory]
        [InlineData(Creator)]     // the primary owner takes it back and claims it
        [InlineData(Bystander)]   // every other node records the same owner, claims nothing
        public void WhatTheDepartedGranteeOwned_ReturnsToThePrimaryOwner_OnEveryNode(int local)
        {
            var (repo, e, sys) = Node(local, primary: Creator);
            var record = new DescriptorOwnership();
            record.SetOwner(Key(Kinematic), Muscle);
            record.SetOwner(Key(Kinematic, 4), Muscle);   // a part instance it held too
            repo.SetManagedComponent(e, record);
            repo.SetAuthority<NetworkTransform>(e, false);
            repo.SetAuthority<NetworkVelocity>(e, false);

            Depart(repo, sys, Muscle);

            Assert.Equal(Creator, Recorded(repo, e, Key(Kinematic)));
            Assert.Equal(Creator, Recorded(repo, e, Key(Kinematic, 4)));
            Assert.Equal(local == Creator, repo.HasAuthority<NetworkTransform>(e));
            Assert.Equal(local == Creator, ((ISimulationView)repo).HasAuthority(e, Key(Kinematic)));
        }

        [Fact]
        public void AFormerOwnersDeparture_TakesBackNothingItAlreadyHandedOn()
        {
            // P10: the Muscle once held Kinematic but handed it to the Bystander; its exit must not move it.
            var (repo, e, sys) = Node(Creator, primary: Creator);
            var record = new DescriptorOwnership();
            record.SetOwner(Key(Kinematic), Bystander);
            repo.SetManagedComponent(e, record);

            Depart(repo, sys, Muscle);

            Assert.Equal(Bystander, Recorded(repo, e, Key(Kinematic)));
            Assert.Equal(0, sys.KeysReclaimed);
        }

        [Fact]
        public void AnEntityWhoseMasterOwnerLeft_IsNotReclaimed()
        {
            var (repo, e, sys) = Node(Bystander, primary: Muscle);   // the departing node is the master owner
            var record = new DescriptorOwnership();
            record.SetOwner(Key(Kinematic), Muscle);
            repo.SetManagedComponent(e, record);

            Depart(repo, sys, Muscle);

            Assert.Equal(Muscle, Recorded(repo, e, Key(Kinematic)));   // the entity is deleted, not reclaimed
        }

        [Fact]
        public void AGrantWhoseTargetLeftBeforeTakingOver_IsTakenBackByTheCreator()
        {
            // Q79 P2: the creator yielded the claim at creation and keeps the record "mine" until the grantee confirms.
            var (repo, e, sys) = Node(Creator, primary: Creator);
            repo.SetAuthority<NetworkTransform>(e, false);
            repo.SetAuthority<NetworkVelocity>(e, false);
            var pending = new OutgoingGrantsPending();
            pending.Descriptors[Kinematic] = Muscle;
            repo.SetManagedComponent(e, pending);

            Depart(repo, sys, Muscle);

            Assert.True(repo.HasAuthority<NetworkTransform>(e));
            Assert.True(repo.HasAuthority<NetworkVelocity>(e));
            Assert.Equal(Creator, Recorded(repo, e, Key(Kinematic)));
            Assert.False(repo.HasManagedComponent<OutgoingGrantsPending>(e));
        }
    }
}
