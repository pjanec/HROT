using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>P3</c> step <c>1</c> — <c>RoleAffinityPolicy</c>.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.3, §3.8, §6 step <c>1</c>.
    ///
    /// <para>🔒 <b>The rule these enforce</b> (user, <c>2026-09-01</c>): <i>"i do not have brain role -&gt;
    /// i will not own brain components, the brain will… No authority conflict."</i></para>
    ///
    /// <para>⚠ <b>These rails build their OWN role→component tables.</b> The production tables are step 4
    /// (the composition roots); step 1 is the mechanism, so the rails supply representative masks and
    /// assert how they COMPOSE. ⇒ ⛔ a green here does not mean CGF and SimHost are configured correctly —
    /// that is step 4's acceptance test.</para>
    /// </summary>
    public class RoleAffinityPolicyTests
    {
        // Representative component ids. ⭐ Deliberately plain ints: this assembly must not know that
        //   "component 10" is a blackboard, which is the same discipline §2.3 imposes on the policy.
        private const int BrainComponentA  = 10;
        private const int BrainComponentB  = 11;
        private const int KinematicA       = 20;   // stands for SimTransform
        private const int KinematicB       = 21;   // stands for SimVelocity

        /// ⭐⭐ Stands for <c>NavigationIntent</c> — the measured case that forced §3.9: <b>Brain-OWNED and
        /// Muscle-READ</b>. ⛔ It is the ONE id that appears in two roles' sets, in two different
        /// relationships, and every §3.9 rail below turns on it.
        private const int IntentComponent  = 12;

        private static BitMask512 Mask(params int[] bits)
        {
            var m = default(BitMask512);
            foreach (var b in bits) m.SetBit(b);
            return m;
        }

        private static bool Has(in BitMask512 mask, int bit) => BitMask512.HasAll(in mask, Mask(bit));

        /// ⭐ The <c>ownedComponentSet</c> table — what each role has AUTHORITY over (§3.9).
        private static Dictionary<NodeRole, BitMask512> StandardTable() => new()
        {
            [NodeRole.Brain]        = Mask(BrainComponentA, BrainComponentB, IntentComponent),
            [NodeRole.MuscleGround] = Mask(KinematicA, KinematicB),
        };

        /// 🔴 The <c>readComponentSet</c> table — owned elsewhere, replicated in, consumed here (§3.9).
        /// ⚠ The Muscle role READS the intent the Brain role OWNS; that is the whole measured case.
        private static Dictionary<NodeRole, BitMask512> StandardReadTable() => new()
        {
            [NodeRole.MuscleGround] = Mask(IntentComponent),
        };

        private static TkbTemplate TemplateWithBirthCritical(params int[] ids)
        {
            var t = new TkbTemplate("RailTemplate", 4242);
            foreach (var id in ids) t.BirthCriticalComponents.Add(id);
            return t;
        }

        private static RoleAffinityPolicy Policy(NodeRole declared, IRoleShardProvider? shard = null)
            => new(declared, StandardTable(), shard ?? new SingleNodePerRoleShardProvider(declared));

        /// ⭐ The same node, additionally told what its roles READ (§3.9's fourth constructor argument).
        private static RoleAffinityPolicy PolicyWithReads(NodeRole declared, IRoleShardProvider? shard = null)
            => new(declared, StandardTable(), shard ?? new SingleNodePerRoleShardProvider(declared),
                   StandardReadTable());

        /// <summary>
        /// ⭐⭐⭐ <b>Step 1's gate, first half — Brain and Muscle masks are DISJOINT.</b>
        ///
        /// <para>⛔ This is the whole point of the design: the Muscle node declines exactly what the Brain
        /// node claims, so no handshake is needed for the cognitive half and two nodes cannot both own a
        /// component. ⚠ It is asserted in BOTH directions — a rule that only ever stripped brain
        /// components from Muscle would leave the Brain node owning kinematics it must not.</para>
        /// </summary>
        [Fact]
        public void BrainAndMuscle_OwnDisjointSets()
        {
            var brain  = Policy(NodeRole.Brain).OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);
            var muscle = Policy(NodeRole.MuscleGround).OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);

            Assert.True(Has(brain, BrainComponentA));
            Assert.True(Has(brain, BrainComponentB));
            Assert.False(Has(brain, KinematicA));
            Assert.False(Has(brain, KinematicB));

            Assert.True(Has(muscle, KinematicA));
            Assert.True(Has(muscle, KinematicB));
            Assert.False(Has(muscle, BrainComponentA));
            Assert.False(Has(muscle, BrainComponentB));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Step 1's gate, second half — the CREATOR'S BIRTHRIGHT is in BOTH, whatever the role.</b>
        ///
        /// <para>⛔⛔ This is the architect's correction, and the rail exists because getting it wrong is
        /// SILENT: applied symmetrically, role affinity would make a Brain-role creator produce
        /// <c>SimTransform</c> unowned. Every egress translator gates on <c>HasAuthority</c>, so the spawn
        /// coordinate would be written correctly and <b>never published</b> — every peer's ghost at the
        /// origin, with no error anywhere.</para>
        ///
        /// <para>⚠ Note <c>isCreator: false</c> must NOT grant it: a node promoting someone else's ghost
        /// has no birthright, or the two nodes would both own the position.</para>
        /// </summary>
        [Fact]
        public void TheCreatorKeepsBirthCriticalComponents_WhateverItsRole_AndOnlyAsCreator()
        {
            var template = TemplateWithBirthCritical(KinematicA);

            foreach (var role in new[] { NodeRole.Brain, NodeRole.MuscleGround, NodeRole.None })
            {
                var asCreator  = Policy(role).OwnableMask(template, isCreator: true,  default);
                var asPromoter = Policy(role).OwnableMask(template, isCreator: false, default);

                Assert.True(Has(asCreator, KinematicA));

                // ⛔ The Brain node must NOT keep it when it is merely promoting an arrived ghost.
                if (role != NodeRole.MuscleGround)
                    Assert.False(Has(asPromoter, KinematicA));
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE SHARD RAIL — the one that proves the seam is REAL rather than decorative.</b>
        ///
        /// <para>📄 §6 step 1 names it: <i>"with a stub provider answering false for Brain, a
        /// Brain-declaring node's mask contains NO brain components"</i>. ⛔ Without this, a future
        /// sharding implementation could be wired in and the policy would ignore it — the seam would
        /// compile, be injected, and change nothing, which is the "written-never-read" family this
        /// codebase keeps producing.</para>
        /// </summary>
        [Fact]
        public void AShardProviderThatDeclinesTheRole_StripsThatRolesComponents()
        {
            var declineBrain = new StubShard(r => r != NodeRole.Brain);

            var mask = new RoleAffinityPolicy(
                NodeRole.Brain | NodeRole.MuscleGround, StandardTable(), declineBrain)
                .OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);

            // ⛔ Declared Brain, but this shard says another node serves Brain for this entity.
            Assert.False(Has(mask, BrainComponentA));
            Assert.False(Has(mask, BrainComponentB));

            // ⛔ Anti-vacuity: the OTHER declared role must still come through, or this rail would pass
            //    on a policy that simply returned an empty mask.
            Assert.True(Has(mask, KinematicA));
            Assert.True(Has(mask, KinematicB));
        }

        /// <summary>
        /// ⭐⭐ A node owns the union of the roles it declares — the all-in-one deployment, which is a
        /// first-class case rather than a degenerate one (<c>NodeRole</c> is <c>[Flags]</c> and
        /// <c>SimHostApp.DefaultRole</c> is itself three roles).
        /// </summary>
        [Fact]
        public void AMultiRoleNode_OwnsTheUnion()
        {
            var mask = Policy(NodeRole.Brain | NodeRole.MuscleGround)
                .OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);

            Assert.True(Has(mask, BrainComponentA));
            Assert.True(Has(mask, KinematicA));
        }

        /// <summary>
        /// ⭐⭐ Roles this node does NOT declare are never consulted, even when the table carries them and
        /// the shard provider would happily say yes.
        /// ⛔ This is what keeps <i>"I only answer for roles I claim"</i> structural rather than a
        /// property of whichever table the host happened to be handed.
        /// </summary>
        [Fact]
        public void AnUndeclaredRole_IsNeverConsulted_EvenIfTheShardSaysYes()
        {
            var alwaysYes = new StubShard(_ => true);

            var mask = new RoleAffinityPolicy(NodeRole.MuscleGround, StandardTable(), alwaysYes)
                .OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);

            Assert.False(Has(mask, BrainComponentA));
            Assert.True(Has(mask, KinematicA));
        }

        /// <summary>
        /// ⭐ A node that declares nothing owns nothing by role — and still keeps its birthright when it
        /// creates. ⚠ The empty case matters: it is §5 ②'s "nobody holds the role" outcome, which the
        /// design rules must LOG rather than fall back, so it must be reachable and harmless here.
        /// </summary>
        [Fact]
        public void ARoleLessNode_OwnsNothingByRole()
        {
            var mask = Policy(NodeRole.None)
                .OwnableMask(TemplateWithBirthCritical(), isCreator: false, default);

            Assert.False(Has(mask, BrainComponentA));
            Assert.False(Has(mask, KinematicA));
        }

        /// <summary>
        /// ⭐⭐ <b>Determinism — the safety property, asserted directly.</b> The same inputs give the same
        /// answer, and the answer does not drift across calls or across keys the default shard ignores.
        /// ⛔ If anyone ever adds a counter, a clock or a cached "last answer" to the policy, this reddens:
        /// two nodes that disagree is precisely the conflict the whole design removes.
        /// </summary>
        [Fact]
        public void TheSameInputsAlwaysGiveTheSameAnswer()
        {
            var policy   = Policy(NodeRole.Brain);
            var template = TemplateWithBirthCritical(KinematicA);

            var first = policy.OwnableMask(template, isCreator: true, default);

            foreach (var key in new[]
                     {
                         default(RoleShardKey),
                         new RoleShardKey(1, 1001, default),
                         new RoleShardKey(long.MaxValue, long.MaxValue, new DISEntityType { Value = 7 }),
                     })
            {
                Assert.Equal(first, policy.OwnableMask(template, isCreator: true, key));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════════════
        // §3.9 — THE TWO-SET MODEL.  REGISTER = ownedComponentSet ∪ readComponentSet,  AUTHORITY = owned.
        // ═══════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 🔴🔴🔴 <b>THE §3.9 RAIL — a Muscle node REGISTERS the intent it must never OWN.</b>
        ///
        /// <para>🔒 User, <c>2026-09-12</c>: <i>"intents are brain owned components that must be replicated
        /// to muscle so musle can read and act on them. so muscle cant simply stop registwring them because
        /// they are brain ones."</i> 📐 Measured: <c>NavigationIntent</c> 16 wire references,
        /// <c>MissionPlanQueue</c> 9 — both Brain-owned and Muscle-read.</para>
        ///
        /// <para>⛔⛔ <b>If this reddens on the REGISTER half, a Muscle node has stopped receiving its own
        /// orders</b> — the failure that killed the "split the cognitive bundle along the role line"
        /// proposal before it was built. ⛔⛔ <b>If it reddens on the AUTHORITY half, the Brain and the
        /// Muscle both own the intent</b> and their egress translators fight — the two-owner conflict this
        /// whole design exists to remove. ⇒ the two halves fail in OPPOSITE directions, which is exactly
        /// why one set could never have expressed both.</para>
        /// </summary>
        [Fact]
        public void AReadComponent_IsRegistered_AndNeverOwned()
        {
            var muscle = PolicyWithReads(NodeRole.MuscleGround);

            Assert.True(Has(muscle.RegisterComponentSet, IntentComponent),
                "the Muscle node does not REGISTER the Brain-owned intent, so the ingress translator has " +
                "no component to write and the node stops receiving its own orders (§3.9).");

            Assert.False(Has(muscle.OwnedComponentSet, IntentComponent),
                "the Muscle node claims AUTHORITY over a component it merely reads — the Brain owns it, " +
                "so both nodes would publish it (§3.9).");

            Assert.False(Has(muscle.OwnableMask(TemplateWithBirthCritical(), isCreator: false, default),
                             IntentComponent),
                "the per-entity authority answer leaked the READ set. readComponentSet must contribute to " +
                "registration and to nothing else.");
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The headline equation, asserted as an equation: <c>REGISTER = owned ∪ read</c>.</b>
        ///
        /// <para>⭐ Both contributors are checked present and the union is checked to add nothing else, so
        /// the rail fails whichever way the implementation drifts — dropping a half, or quietly widening
        /// the set.</para>
        /// </summary>
        [Fact]
        public void RegisterComponentSet_IsExactlyTheUnionOfTheTwoSets()
        {
            var muscle = PolicyWithReads(NodeRole.MuscleGround);

            var expected = muscle.OwnedComponentSet;
            var read     = muscle.ReadComponentSet;
            expected.BitwiseOr(in read);

            Assert.Equal(expected, muscle.RegisterComponentSet);

            // ⛔ Anti-vacuity: an implementation returning empty masks everywhere would satisfy the
            //    equation above and nothing else. Both halves must actually carry something, and they
            //    must carry DIFFERENT things — or the two sets are not being kept apart at all.
            Assert.True(Has(muscle.OwnedComponentSet, KinematicA));
            Assert.True(Has(muscle.ReadComponentSet,  IntentComponent));
            Assert.False(Has(muscle.ReadComponentSet, KinematicA));
        }

        /// <summary>
        /// ⭐⭐ <b>The opt-in guarantee — with NO read table, <c>REGISTER == OWNED</c> and every existing
        /// three-argument call site behaves exactly as it did.</b>
        ///
        /// <para>⛔ The fourth constructor argument is optional, and this is what makes that safe to ship
        /// ahead of the composition roots that will fill it (step 4). ⚠ It is also the rail that would
        /// catch a "helpful" default — a read table inferred from the owned table, say — which would hand
        /// every node a wider registry than its host asked for.</para>
        /// </summary>
        [Fact]
        public void WithNoReadTable_RegisterEqualsOwned()
        {
            var muscle = Policy(NodeRole.MuscleGround);

            Assert.Equal(default(BitMask512), muscle.ReadComponentSet);
            Assert.Equal(muscle.OwnedComponentSet, muscle.RegisterComponentSet);
            Assert.True(Has(muscle.RegisterComponentSet, KinematicA));   // anti-vacuity
        }

        /// <summary>
        /// 🔴🔴 <b>REGISTRATION IS PER NODE; THE SHARD IS PER ENTITY — so the shard must NOT narrow these
        /// sets.</b>
        ///
        /// <para>⛔⛔ The trap this rail exists for: <see cref="RoleAffinityPolicy.OwnableMask"/> consults
        /// <see cref="IRoleShardProvider"/>, so the obvious implementation of "which components can I own?"
        /// is to call it with a default key. ⚠ That is WRONG — the shard answers <i>"does another node
        /// serve Brain for THIS entity?"</i>, and a node that deregistered a component because it does not
        /// serve that role for ONE entity could not handle the next one. ⇒ these sets are deliberately
        /// shard-free, and a declining shard changes nothing.</para>
        /// </summary>
        [Fact]
        public void TheRegistrationSets_AreNotNarrowedByTheShard()
        {
            var declineEverything = new StubShard(_ => false);

            var node = new RoleAffinityPolicy(
                NodeRole.Brain | NodeRole.MuscleGround, StandardTable(), declineEverything,
                StandardReadTable());

            Assert.True(Has(node.OwnedComponentSet,    BrainComponentA));
            Assert.True(Has(node.OwnedComponentSet,    KinematicA));
            Assert.True(Has(node.RegisterComponentSet, IntentComponent));

            // ⛔ Anti-vacuity, and the other half of the contract: the shard DOES still strip the
            //    per-entity AUTHORITY answer. If this ever goes true, the shard seam has gone decorative.
            Assert.False(Has(node.OwnableMask(TemplateWithBirthCritical(), isCreator: false, default),
                             BrainComponentA));
        }

        /// <summary>
        /// ⭐⭐ Roles this node does not declare contribute nothing to EITHER set — the same structural rule
        /// the authority path already keeps (<see cref="AnUndeclaredRole_IsNeverConsulted_EvenIfTheShardSaysYes"/>),
        /// now asserted for registration.
        ///
        /// <para>⭐ This is what lets ONE shared pair of tables be handed to every host in the cluster: a
        /// host's registry follows from the roles IT declares, not from the size of the table it was
        /// given.</para>
        /// </summary>
        [Fact]
        public void AnUndeclaredRolesComponents_AreNeitherOwnedNorRegistered()
        {
            var brainOnly = PolicyWithReads(NodeRole.Brain);

            Assert.True(Has(brainOnly.OwnedComponentSet, IntentComponent));       // Brain OWNS the intent
            Assert.Equal(default(BitMask512), brainOnly.ReadComponentSet);        // ⛔ and reads nothing
            Assert.False(Has(brainOnly.RegisterComponentSet, KinematicA),
                "a Brain-only node registered a component only the Muscle role declares.");
        }

        /// <summary>
        /// ⚠⚠ <b>The birthright is NOT in <c>OwnedComponentSet</c>, and that is deliberate — so a
        /// diagnostic must never read "can never own" off it alone.</b>
        ///
        /// <para>📄 §3.9. <c>BirthCriticalComponents</c> are per TEMPLATE, and this property has no
        /// template — so a node genuinely CAN own a component absent from it (every entity it creates).
        /// ⛔ A boot check that flagged such a component as a configuration error would be wrong for
        /// exactly the case where being wrong is loudest: the origin-flash failure §3.1 exists to
        /// prevent.</para>
        ///
        /// <para>⭐ Railed rather than only commented because the tempting "fix" — folding the birthright
        /// in — silently turns a per-template fact into a node-wide claim.</para>
        /// </summary>
        [Fact]
        public void OwnedComponentSet_ExcludesTheCreatorsBirthright()
        {
            var brain    = PolicyWithReads(NodeRole.Brain);
            var template = TemplateWithBirthCritical(KinematicA);

            Assert.False(Has(brain.OwnedComponentSet, KinematicA),
                "the role-derived owned set absorbed a per-TEMPLATE birth-critical component, turning a " +
                "fact about one template into a node-wide claim (§3.9).");

            // ⭐ And the creator really does own it — so the exclusion above is a genuine gap between the
            //   static set and the per-entity answer, not an artefact of the template being empty.
            Assert.True(Has(brain.OwnableMask(template, isCreator: true, default), KinematicA));
        }

        private sealed class StubShard : IRoleShardProvider
        {
            private readonly System.Func<NodeRole, bool> _answer;
            public StubShard(System.Func<NodeRole, bool> answer) => _answer = answer;
            public bool ServesRole(NodeRole role, in RoleShardKey key) => _answer(role);
        }
    }
}
