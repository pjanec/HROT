using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;
using Hrot.Network.Systems;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// CE-276 — the INITIATION (push) side: a node hands an entity (or a subset of its descriptors) away.
    /// Proves the three scopes and that save ownership (NetworkAuthority.PrimaryOwnerId) moves iff EntityMaster
    /// is in the transferred set. 📄 docs/DESIGN_Entity_Ownership_Transfer.md §2.2/§4.
    /// </summary>
    public class OwnershipTransferInitiationTests
    {
        // A real registered component stands in for a non-master descriptor's component set.
        private const int  Local     = 1;
        private const int  MasterOrd = 7;            // stand-in for dtEntityMaster
        private const int  OtherOrd  = 8;
        private const long NetId     = 5000;

        private static (EntityRepository repo, Entity e, OwnershipTransferInitiationSystem sys) Setup()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();
            repo.RegisterManagedEvent<TransferEntityOwnershipRequest>();

            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity(NetId));
            repo.AddComponent(e, new NetworkAuthority(Local, Local));   // this node owns it
            repo.AddComponent(e, new TkbIdentity { TkbType = 1 });

            var map = new NetworkEntityMap();
            map.Register(NetId, e);

            var dmap = new DescriptorOwnershipMap { PrimaryOwnerDescriptorOrdinal = MasterOrd };
            dmap.RegisterMapping((long)MasterOrd, ComponentType<NetworkIdentity>.ID);
            dmap.RegisterMapping((long)OtherOrd,  ComponentType<TkbIdentity>.ID);

            return (repo, e, new OwnershipTransferInitiationSystem(map, Local, dmap));
        }

        private static void Fire(EntityRepository repo, OwnershipTransferInitiationSystem sys, TransferEntityOwnershipRequest req)
        {
            repo.Bus.PublishManaged(req);
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();   // make the published OwnershipUpdate(s) readable
        }

        private static List<OwnershipUpdate> Updates(EntityRepository repo)
            => ((ISimulationView)repo).ReadEvents<OwnershipUpdate>().ToArray().ToList();

        [Fact]
        public void MasterOnly_MirrorsPrimaryOwner_AndPublishesOnlyMaster()
        {
            var (repo, e, sys) = Setup();
            Fire(repo, sys, new TransferEntityOwnershipRequest
            {
                NetworkId = NetId, NewOwnerNodeId = 5, Scope = TransferScope.MasterOnly,
            });

            Assert.Equal(5, repo.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId);   // save ownership moved
            var ups = Updates(repo);
            Assert.Single(ups);
            Assert.Equal(OwnershipExtensions.PackKey(MasterOrd, 0), ups[0].PackedKey);
            Assert.Equal(5, ups[0].NewOwnerNodeId);
            Assert.Equal(Local, ups[0].OriginNodeId);
        }

        [Fact]
        public void AllOwnedByThisNode_MovesAllDescriptors_IncludingMaster()
        {
            var (repo, e, sys) = Setup();
            Fire(repo, sys, new TransferEntityOwnershipRequest
            {
                NetworkId = NetId, NewOwnerNodeId = 5, Scope = TransferScope.AllOwnedByThisNode,
            });

            Assert.Equal(5, repo.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId);
            var keys = Updates(repo).Select(u => u.PackedKey).ToHashSet();
            Assert.Contains(OwnershipExtensions.PackKey(MasterOrd, 0), keys);
            Assert.Contains(OwnershipExtensions.PackKey(OtherOrd, 0), keys);
        }

        [Fact]
        public void SpecificDescriptors_ExcludingMaster_LeavesPrimaryOwner()
        {
            var (repo, e, sys) = Setup();
            Fire(repo, sys, new TransferEntityOwnershipRequest
            {
                NetworkId = NetId, NewOwnerNodeId = 5, Scope = TransferScope.SpecificDescriptors,
                DescriptorTypeIds = new long[] { OtherOrd },
            });

            // master NOT in the set → save ownership stays with us (spec-legal partial owner)
            Assert.Equal(Local, repo.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId);
            var ups = Updates(repo);
            Assert.Single(ups);
            Assert.Equal(OwnershipExtensions.PackKey(OtherOrd, 0), ups[0].PackedKey);
        }

        [Fact]
        public void TransferToOurselves_IsNoOp()
        {
            var (repo, e, sys) = Setup();
            Fire(repo, sys, new TransferEntityOwnershipRequest
            {
                NetworkId = NetId, NewOwnerNodeId = Local, Scope = TransferScope.AllOwnedByThisNode,
            });

            Assert.Equal(Local, repo.GetComponentRO<NetworkAuthority>(e).PrimaryOwnerId);
            Assert.Empty(Updates(repo));
        }
    }
}
