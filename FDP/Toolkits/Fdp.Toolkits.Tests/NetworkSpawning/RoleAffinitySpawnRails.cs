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
    /// ⭐⭐⭐ <b><c>P3</c> step <c>2</c> — the CREATE leg: a creator declines what its role excludes, and
    /// keeps its birthright regardless.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1, §3.2, §6 step <c>2</c>.
    ///
    /// <para>🔒 <b>The ruling</b> (user, <c>2026-09-01</c>): <i>"SimHost having a muscle role should not
    /// instantiate any brain related components… by applying 'auto-takeover' rules it can create the
    /// components as unowned while CGF creates them as owned. No authority conflict."</i></para>
    ///
    /// <para>⭐ <b>A separate rail class, matching this folder's own convention</b> — <c>D2</c>'s rails live
    /// in <c>TransientSpawnTagRails</c> beside <c>SpawnSystemTests</c> for the same reason: a cross-cutting
    /// feature on this system gets its own file, and folding these into the general suite would bury them.</para>
    ///
    /// <para>⚠⚠ <b>What step 2 does NOT do, stated so nobody over-trusts a green here:</b> it changes only
    /// which bits are SET. ⛔ Authority gates REPLICATION (every egress translator checks it) — it does
    /// <b>not</b> stop a node executing. The cognitive tick systems carry no authority filter, so a node
    /// that declines brain components still ticks the brain until <b>step 3b</b>. 📄 §3.5.</para>
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
            repo.RegisterComponent<NetworkOwnership>();
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

        /// <summary>⭐ A template that declares <see cref="SimTransform"/> birth-critical — what
        /// <c>P3</c> step 0 seeds onto every production template.</summary>
        private static TkbDatabase Tkb(bool birthCritical = true)
        {
            var tkb = new TkbDatabase();
            var t   = new TkbTemplate("RoleAffinitySubject", TkbType);
            if (birthCritical) t.AddBirthCriticalComponent<SimTransform>();
            tkb.Register(t);
            return tkb;
        }

        /// <summary>
        /// ⭐ The role table: MuscleGround owns the kinematics, Brain owns nothing the spawned entity has.
        /// ⚠ Deliberately NOT a production table — those are step 4. This pins COMPOSITION only.
        /// </summary>
        private static Dictionary<NodeRole, BitMask512> RoleTable()
        {
            var kinematics = default(BitMask512);
            kinematics.SetBit(Id<SimTransform>());
            kinematics.SetBit(Id<SimVelocity>());

            var brainish = default(BitMask512);
            brainish.SetBit(Id<GhostStateTracker>());

            return new Dictionary<NodeRole, BitMask512>
            {
                [NodeRole.MuscleGround] = kinematics,
                [NodeRole.Brain]        = brainish,
            };
        }

        private static IRoleAffinityPolicy PolicyFor(NodeRole declared)
            => new RoleAffinityPolicy(declared, RoleTable(), new SingleNodePerRoleShardProvider(declared));

        private static NetworkSpawningSystem Spawner(
            TkbDatabase tkb, NetworkEntityMap map, IRoleAffinityPolicy? policy)
            => new(tkb, new EntityLifecycleModule(tkb, System.Array.Empty<int>()),
                   map, new StubIdAllocator(startId: 700), LocalNodeId,
                   roleAffinity: policy);

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
        /// ⭐⭐⭐ <b>Step 2's gate — with NO policy the mask is unchanged, byte for byte.</b>
        ///
        /// <para>⛔⛔ This is the rail that makes step 2 safe to ship ahead of step 4. Every host runs with
        /// a null policy until its composition root hands one over, so if this ever reddens the change has
        /// stopped being opt-in and every node in the cluster has silently changed what it owns.</para>
        ///
        /// <para>📐 It asserts EQUALITY with the component mask, not merely "owns the interesting bits" —
        /// a weaker assertion would pass a policy that dropped something nobody thought to name.</para>
        /// </summary>
        [Fact]
        public void WithNoPolicy_TheCreatorStillOwnsEverythingItMaterialised()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map, policy: null), map, 701, LocalNodeId);

            var components = repo.GetComponentMask(e.Index);
            var authority  = repo.GetMetadata(e.Index).AuthorityMask;

            Assert.Equal(components, authority);

            // ⛔ Anti-vacuity: two empty masks are also equal.
            Assert.True(components.IsSet(Id<SimTransform>()),
                "the entity has no SimTransform, so this rail would pass on two empty masks.");
        }

        /// <summary>
        /// ⭐⭐⭐ <b>A policy DROPS the bits its role excludes — the design's own red-proof, as a rail.</b>
        ///
        /// <para>§6 step 2 words it as <i>"red-proof: inject a policy, assert the bits drop"</i>. ⭐ Asserted
        /// against the SAME spawn shape as the rail above, so the pair isolates exactly one variable: the
        /// presence of a policy.</para>
        /// </summary>
        [Fact]
        public void WithAMusclePolicy_TheCreatorDeclinesWhatItsRoleExcludes()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map, PolicyFor(NodeRole.MuscleGround)), map, 702, LocalNodeId);

            // ⭐ Owned: the kinematics its role covers.
            Assert.True(Owns(repo, e, Id<SimTransform>()));
            Assert.True(Owns(repo, e, Id<SimVelocity>()));

            // ⛔ Declined: everything its role does not cover — including the network identity it
            //    materialised itself. That is correct and is the point: ownership is now DERIVED, not
            //    "I made it, therefore it is mine".
            Assert.False(Owns(repo, e, Id<GhostStateTracker>()));
            Assert.False(Owns(repo, e, Id<NetworkIdentity>()),
                "the Muscle role's table does not name NetworkIdentity, so the blanket grant is still " +
                "in force — ProcessSpawn is not intersecting with the policy.");
        }

        /// <summary>
        /// 🔴🔴🔴 <b>THE BIRTHRIGHT RAIL — the one the architect's correction exists to protect.</b>
        ///
        /// <para>📄 §3.1. 🔒 Architect, <c>2026-09-01</c>: <i>"the position can not start empty (must always
        /// be valid — it is the key property of an entity)."</i></para>
        ///
        /// <para>⛔⛔ <b>Why getting this wrong would be SILENT.</b> Applied symmetrically, role affinity
        /// makes a Brain-role creator produce <see cref="SimTransform"/> UNOWNED. The creator still WRITES
        /// the spawn coordinate — <c>SetComponent</c> is not authority-gated — but <b>every egress
        /// translator gates on <c>HasAuthority</c></b>, so the position is never published and every peer's
        /// ghost sits at the origin, with no error anywhere.</para>
        ///
        /// <para>⭐ <b>The pairing is the assertion.</b> The Brain creator keeps <c>SimTransform</c>
        /// (birth-critical) and declines <c>SimVelocity</c> (kinematic, not birth-critical) — same role,
        /// same spawn, opposite outcomes. ⛔ A rail that only checked the kept bit would pass on an
        /// implementation that granted the whole kinematic set.</para>
        /// </summary>
        [Fact]
        public void ABrainRoleCreator_KeepsBirthCriticalTransform_ButNotTheRestOfTheKinematics()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map, PolicyFor(NodeRole.Brain)), map, 703, LocalNodeId);

            Assert.True(Owns(repo, e, Id<SimTransform>()),
                "the CREATOR declined its birth-critical SimTransform. It will still write the spawn " +
                "coordinate, but every egress translator gates on HasAuthority — so the position is " +
                "never published and every peer's ghost sits at the origin (§3.1).");

            Assert.False(Owns(repo, e, Id<SimVelocity>()),
                "the Brain creator kept SimVelocity, which is kinematic and NOT birth-critical — the " +
                "birthright is leaking into the whole role set.");
        }

        /// <summary>
        /// ⭐⭐ <b>The birthright is the TEMPLATE's, not a constant.</b> A template that does not declare
        /// <see cref="SimTransform"/> birth-critical gets no exemption — which is what makes step 0's
        /// per-template declaration load-bearing rather than decorative.
        /// </summary>
        [Fact]
        public void WithoutABirthCriticalDeclaration_TheBrainCreatorDeclinesTheTransformToo()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(birthCritical: false), map, PolicyFor(NodeRole.Brain)),
                             map, 704, LocalNodeId);

            Assert.False(Owns(repo, e, Id<SimTransform>()));
        }

        /// <summary>
        /// ⭐⭐ <b>A spawn this node does NOT own is untouched by the policy.</b>
        ///
        /// <para>📐 <c>ProcessSpawn</c> grants authority only when <c>cmd.OwnerNodeId == _localNodeId</c>;
        /// a replica of someone else's entity starts unowned and stays that way. ⛔ If the policy ever ran
        /// outside that branch, a node would claim components on an entity another node owns — the exact
        /// two-owner conflict this design removes, introduced by the fix for it.</para>
        /// </summary>
        [Fact]
        public void ARemotelyOwnedSpawn_OwnsNothing_PolicyOrNot()
        {
            var repo = CreateWorld();
            var map  = new NetworkEntityMap();
            var e    = Spawn(repo, Spawner(Tkb(), map, PolicyFor(NodeRole.MuscleGround)), map, 705, RemoteNode);

            Assert.False(Owns(repo, e, Id<SimTransform>()));
            Assert.False(Owns(repo, e, Id<SimVelocity>()));
        }
    }
}
