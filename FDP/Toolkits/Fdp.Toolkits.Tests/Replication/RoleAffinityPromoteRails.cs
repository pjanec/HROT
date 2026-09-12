using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>P3</c> step <c>3</c> — the PROMOTE leg: an arrived ghost claims exactly what this node's
    /// role covers.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1, §3.2, §6 step <c>3</c>.
    ///
    /// <para>⭐⭐ <b>Why the two legs must be railed as a PAIR.</b> The design's safety property is that the
    /// creator DECLINES exactly what the role-holder CLAIMS, both evaluating the same function. ⇒ a green
    /// create leg and a green promote leg are not independently sufficient — ⛔ if the two ever
    /// disagreed, an entity would end up owned twice or not at all. The last rail here is the one that
    /// asserts complementarity directly.</para>
    ///
    /// <para>⚠ Named <c>RoleAffinity*</c> to match <c>RoleAffinityPolicyTests</c>,
    /// <c>RoleShardSeamTests</c> and <c>RoleAffinitySpawnRails</c>, so the whole feature is findable by
    /// one search rather than split across suites by which system it happens to touch.</para>
    /// </summary>
    public class RoleAffinityPromoteRails
    {
        private const long TkbType = 8888L;

        private static int Id<T>() => ComponentTypeRegistry.GetOrRegisterManaged(typeof(T));

        // ⭐ Reuses this suite's own MockTkbDatabase (GhostProtocolTests.cs) rather than writing a
        //   parallel stub — R-142 ④ in miniature: the feature's suite already has the fixture.

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<ConstructionOrder>();
            return repo;
        }

        private static Dictionary<NodeRole, BitMask512> RoleTable()
        {
            var kinematics = default(BitMask512);
            kinematics.SetBit(Id<SimTransform>());
            kinematics.SetBit(Id<SimVelocity>());

            var brainish = default(BitMask512);
            brainish.SetBit(Id<NetworkIdentity>());   // stands in for a cognitive component

            return new Dictionary<NodeRole, BitMask512>
            {
                [NodeRole.MuscleGround] = kinematics,
                [NodeRole.Brain]        = brainish,
            };
        }

        private static IRoleAffinityPolicy PolicyFor(NodeRole declared)
            => new RoleAffinityPolicy(declared, RoleTable(), new SingleNodePerRoleShardProvider(declared));

        /// <summary>⭐ A ghost as <c>GhostCreationSystem</c> leaves it: identity, tracker, and the
        /// replicated components — owning NOTHING.</summary>
        private static Entity Ghost(EntityRepository repo)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new TkbIdentity { TkbType = TkbType });
            repo.AddComponent(e, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.AddComponent(e, new NetworkIdentity(4242));
            repo.AddComponent(e, new SimTransform());
            repo.AddComponent(e, new SimVelocity());
            repo.SetLifecycleState(e, EntityLifecycle.Ghost);
            return e;
        }

        private static Entity Promote(EntityRepository repo, IRoleAffinityPolicy? policy,
                                      bool birthCritical = true)
        {
            var template = new TkbTemplate("PromotionSubject", TkbType);
            if (birthCritical) template.AddBirthCriticalComponent<SimTransform>();

            var tkb = new MockTkbDatabase { TemplateToReturn = template };
            var sys = new GhostPromotionSystem(
                tkb, new EntityLifecycleModule(tkb, Array.Empty<int>()),
                translators: Array.Empty<ITkbEntityTranslator>(),
                roleAffinity: policy);

            var e = Ghost(repo);
            sys.Execute(repo, 0f);
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(e));
            return e;
        }

        private static bool Owns(EntityRepository repo, Entity e, int componentId)
            => repo.GetMetadata(e.Index).AuthorityMask.IsSet(componentId);

        /// <summary>
        /// ⭐⭐⭐ <b>Step 3's gate — with NO policy an arrived ghost claims NOTHING, exactly as today.</b>
        ///
        /// <para>⛔ The opt-in guarantee for the promote leg. A replica has always started unowned and
        /// waited for an explicit <c>OwnershipUpdate</c>; if this reddens, every node in the cluster has
        /// silently begun claiming components on entities other nodes own.</para>
        /// </summary>
        [Fact]
        public void WithNoPolicy_APromotedGhostClaimsNothing()
        {
            var repo = CreateWorld();
            var e    = Promote(repo, policy: null);

            Assert.False(Owns(repo, e, Id<SimTransform>()));
            Assert.False(Owns(repo, e, Id<SimVelocity>()));
            Assert.False(Owns(repo, e, Id<NetworkIdentity>()));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The claim: a Muscle node promoting an arrived ghost takes the kinematics and nothing
        /// else.</b> §6 step 3's gate — <i>"a promoted ghost owns exactly the role's descriptors"</i>.
        /// </summary>
        [Fact]
        public void AMuscleNode_ClaimsExactlyItsRolesComponents()
        {
            var repo = CreateWorld();
            var e    = Promote(repo, PolicyFor(NodeRole.MuscleGround));

            Assert.True(Owns(repo, e, Id<SimTransform>()));
            Assert.True(Owns(repo, e, Id<SimVelocity>()));
            Assert.False(Owns(repo, e, Id<NetworkIdentity>()),
                "the Muscle node claimed a component belonging to the Brain role — the promote leg is " +
                "not intersecting with the role's mask.");
        }

        /// <summary>
        /// 🔴🔴🔴 <b>A PROMOTER GETS NO BIRTHRIGHT — and this is the two-owner bug wearing the fix's
        /// clothes.</b>
        ///
        /// <para>📄 §3.1. The CREATOR keeps the template's birth-critical components whatever its role
        /// (step 2, railed in <c>RoleAffinitySpawnRails</c>). ⛔ If a PROMOTER claimed them too, both nodes
        /// would own the position and their egress translators would fight — the precise conflict this
        /// whole design exists to remove, reintroduced by its own mechanism.</para>
        ///
        /// <para>⭐ The template here DOES declare <c>SimTransform</c> birth-critical, so the rail is not
        /// vacuous: the exemption exists and must simply not apply on this leg. A Brain node claims
        /// neither it nor <c>SimVelocity</c>.</para>
        /// </summary>
        [Fact]
        public void APromotingNode_GetsNoBirthright_EvenForABirthCriticalComponent()
        {
            var repo = CreateWorld();
            var e    = Promote(repo, PolicyFor(NodeRole.Brain), birthCritical: true);

            Assert.False(Owns(repo, e, Id<SimTransform>()),
                "a PROMOTER claimed a birth-critical component. The creator already owns it (step 2), " +
                "so both nodes now own the position and their egress will fight — the two-owner conflict " +
                "this design removes, reintroduced by its own mechanism (§3.1).");

            Assert.True(Owns(repo, e, Id<NetworkIdentity>()),
                "anti-vacuity: the Brain node claimed nothing at all, so the assertion above would hold " +
                "for the wrong reason.");
        }

        /// <summary>
        /// ⭐⭐ <b>An explicit grant already on the ghost SURVIVES the claim.</b>
        ///
        /// <para>📄 §3.4 — this design does <b>not</b> retire <c>DeferredTakeOwnership</c>; explicit grants
        /// still win, and it only makes the DEFAULT declarative and local. ⇒ the claim must be ADDITIVE.
        /// ⛔ An assignment here would silently revoke authority a node was explicitly granted over the
        /// wire, which is a regression no existing rail would catch.</para>
        /// </summary>
        [Fact]
        public void AnExplicitGrantAlreadyOnTheGhost_IsNotRevokedByTheClaim()
        {
            var repo = CreateWorld();

            var template = new TkbTemplate("PromotionSubject", TkbType);
            var tkb      = new MockTkbDatabase { TemplateToReturn = template };
            var sys      = new GhostPromotionSystem(
                tkb, new EntityLifecycleModule(tkb, Array.Empty<int>()),
                translators: Array.Empty<ITkbEntityTranslator>(),
                roleAffinity: PolicyFor(NodeRole.MuscleGround));

            var e = Ghost(repo);

            // ⭐ As if OwnershipIngressSystem had already granted this node the Brain-role component.
            repo.SetAuthority(e, Id<NetworkIdentity>(), true);

            sys.Execute(repo, 0f);

            Assert.True(Owns(repo, e, Id<NetworkIdentity>()),
                "the role claim REVOKED an explicit grant. §3.4: explicit grants still win — the claim " +
                "must be additive (BitwiseOr), never an assignment.");
            Assert.True(Owns(repo, e, Id<SimTransform>()),
                "anti-vacuity: the role's own claim did not land either, so the assertion above would " +
                "hold for the wrong reason.");
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE COMPLEMENTARITY RAIL — the create leg and the promote leg PARTITION the components,
        /// with no overlap and no gap.</b>
        ///
        /// <para>⛔⛔ This is the property the whole design rests on, and neither leg's own rails can see
        /// it: each is green in isolation while the pair double-owns or orphans a component. ⭐ Asserted
        /// over the SAME policy pair a real Brain+Muscle cluster would run — a Brain creator and a Muscle
        /// promoter — by checking the two masks directly.</para>
        ///
        /// <para>⚠ The birth-critical component is the deliberate EXCEPTION and is asserted as such: the
        /// creator keeps it and the promoter does not, so it is owned exactly once — by the creator, until
        /// an explicit hand-off moves it.</para>
        /// </summary>
        [Fact]
        public void TheCreateAndPromoteLegs_PartitionTheComponents()
        {
            var template = new TkbTemplate("PartitionSubject", TkbType);
            template.AddBirthCriticalComponent<SimTransform>();

            var brainCreates  = PolicyFor(NodeRole.Brain)
                .OwnableMask(template, isCreator: true,  default);
            var musclePromotes = PolicyFor(NodeRole.MuscleGround)
                .OwnableMask(template, isCreator: false, default);

            // ⭐ The cognitive component: the Brain creator owns it, the Muscle promoter does not.
            Assert.True(brainCreates.IsSet(Id<NetworkIdentity>()));
            Assert.False(musclePromotes.IsSet(Id<NetworkIdentity>()));

            // ⭐ SimVelocity: kinematic and NOT birth-critical ⇒ the Muscle promoter takes it, the Brain
            //   creator declines it. Exactly one owner.
            Assert.False(brainCreates.IsSet(Id<SimVelocity>()));
            Assert.True(musclePromotes.IsSet(Id<SimVelocity>()));

            // ⚠ SimTransform is the EXCEPTION, and deliberately so: the creator's birthright means it is
            //   owned by the Brain at birth even though kinematics are the Muscle's role. The hand-off is
            //   explicit (DeferredTakeOwnership), which is why §3.1 keeps that path alive.
            Assert.True(brainCreates.IsSet(Id<SimTransform>()));
            Assert.True(musclePromotes.IsSet(Id<SimTransform>()),
                "the Muscle node does not claim SimTransform on promotion, so after the creator hands it " +
                "off nobody would own the position.");
        }
    }
}
