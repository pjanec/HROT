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

        private static BitMask512 Mask(params int[] bits)
        {
            var m = default(BitMask512);
            foreach (var b in bits) m.SetBit(b);
            return m;
        }

        private static bool Has(in BitMask512 mask, int bit) => BitMask512.HasAll(in mask, Mask(bit));

        private static Dictionary<NodeRole, BitMask512> StandardTable() => new()
        {
            [NodeRole.Brain]        = Mask(BrainComponentA, BrainComponentB),
            [NodeRole.MuscleGround] = Mask(KinematicA, KinematicB),
        };

        private static TkbTemplate TemplateWithBirthCritical(params int[] ids)
        {
            var t = new TkbTemplate("RailTemplate", 4242);
            foreach (var id in ids) t.BirthCriticalComponents.Add(id);
            return t;
        }

        private static RoleAffinityPolicy Policy(NodeRole declared, IRoleShardProvider? shard = null)
            => new(declared, StandardTable(), shard ?? new SingleNodePerRoleShardProvider(declared));

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

        private sealed class StubShard : IRoleShardProvider
        {
            private readonly System.Func<NodeRole, bool> _answer;
            public StubShard(System.Func<NodeRole, bool> answer) => _answer = answer;
            public bool ServesRole(NodeRole role, in RoleShardKey key) => _answer(role);
        }
    }
}
