using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.NetworkSpawning.Tests.Helpers;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkit.NetworkSpawning.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The CREATE leg under push-only: the creator claims EVERYTHING it materialised</b> (D-7); the grants
    /// it publishes are what move role groups away (<c>LocalAuthorityYieldSystem</c>).
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.5 D-7, §5.6 S4; <c>Architect_Question_79</c> §0.7 (R-164).
    ///
    /// <para>⛔ <b>SUPERSEDED, <c>2026-10-02</c> (S4):</b> this suite used to rail the role-affinity create leg
    /// (<c>DESIGN_Role_Affinity_Ownership.md</c> P3 step 2) — a creator DECLINING what its role excludes, with a
    /// birthright exception for <c>SimTransform</c>. Push-only retired the decline; with the creator owning all,
    /// the birthright is no longer an exception. The policy rails were removed with the mechanism.</para>
    /// </summary>
    public class RoleAffinitySpawnRails
    {
        private const long TkbType     = 7777L;
        private const int  LocalNodeId = 1;
        private const int  RemoteNode  = 9;

        private static int Id<T>() => ComponentTypeRegistry.GetOrRegisterManaged(typeof(T));

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        /// <summary>⭐ Every template reports <see cref="SimTransform"/> birth-critical — it is DERIVED
        /// from <c>[BirthCritical]</c> on the component type, not declared per template
        /// (<c>2026-09-13</c>, <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a).</summary>
        private static TkbDatabase Tkb()
        {
            var tkb = new TkbDatabase();
            tkb.Register(new TkbTemplate("RoleAffinitySubject", TkbType));
            return tkb;
        }

        private static NetworkSpawningSystem Spawner(TkbDatabase tkb, NetworkEntityMap map)
            => new(tkb, new EntityLifecycleModule(tkb, System.Array.Empty<int>()),
                   map, new StubIdAllocator(startId: 700), LocalNodeId);

        private static SpawnEntityCommand Cmd(long networkId, int owner) => new()
        {
            RequestId        = System.Guid.NewGuid(),
            NetworkId        = networkId,
            TkbType          = TkbType,
            OwnerNodeId      = owner,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform(),
            InitialVelocity  = new SimVelocity(),
        };

        private static Entity Spawn(EntityRepository repo, NetworkSpawningSystem system,
                                    NetworkEntityMap map, long networkId, int owner)
        {
            repo.Bus.PublishManaged(Cmd(networkId, owner));
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);
            ((EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer()).Playback(repo);
            Assert.True(map.TryGetEntity(networkId, out var e), "the entity did not materialise.");
            return e;
        }

        private static bool Owns(EntityRepository repo, Entity e, int componentId)
            => repo.GetMetadata(e.Index).AuthorityMask.IsSet(componentId);

        /// <summary>
        /// ⭐⭐⭐ <b>The creator owns EVERYTHING it materialised — the authority mask equals the component mask.</b>
        /// <para>📐 Equality, not "owns the interesting bits": a weaker check would pass a creator that dropped
        /// something nobody named. The role groups leave only through grants (the yield), never at birth.</para>
        /// </summary>
        [Fact]
        public void TheCreatorOwnsEverythingItMaterialised()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map), map, 701, LocalNodeId);

            Assert.Equal(repo.GetComponentMask(e.Index), repo.GetMetadata(e.Index).AuthorityMask);
            Assert.True(repo.GetComponentMask(e.Index).IsSet(Id<SimTransform>()),
                "anti-vacuity: the entity has no SimTransform, so this rail would pass on two empty masks.");
        }

        /// <summary>
        /// ⭐ The TKB still derives <see cref="SimTransform"/> as birth-critical for every template (<c>tkb-1</c>
        /// §6.6a) — no template can lack it, and the creator owns it at birth.
        /// </summary>
        [Fact]
        public void NoTemplateCanOmitTheBirthright_AndTheCreatorOwnsIt()
        {
            var bare = new TkbTemplate("NoDeclarationsAtAll", TkbType);
            Assert.Contains(Id<SimTransform>(), bare.BirthCriticalComponents);

            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var tkb  = new TkbDatabase();
            tkb.Register(bare);
            var e = Spawn(repo, Spawner(tkb, map), map, 704, LocalNodeId);

            Assert.True(Owns(repo, e, Id<SimTransform>()));
        }

        /// <summary>
        /// ⭐⭐ <b>A spawn this node does NOT own claims nothing.</b> <c>ProcessSpawn</c> grants authority only when
        /// <c>cmd.OwnerNodeId == _localNodeId</c>; a replica starts unowned.
        /// </summary>
        [Fact]
        public void ARemotelyOwnedSpawn_OwnsNothing()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map), map, 705, RemoteNode);

            Assert.False(Owns(repo, e, Id<SimTransform>()));
            Assert.False(Owns(repo, e, Id<SimVelocity>()));
        }
    }
}
