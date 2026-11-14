using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Abstractions;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>P3</c> step <c>4</c> — the PRODUCTION role tables.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9, §3.9a, §6 step <c>4</c>, §6i.
    ///
    /// <para>⚠ <c>RoleAffinityPolicyTests</c> (in <c>Fdp.Toolkits.Tests</c>) proves the MECHANISM over its
    /// own representative masks and says in its own header that <i>"a green here does not mean CGF and
    /// SimHost are configured correctly — that is step 4's acceptance test."</i> ⭐ <b>This file is that
    /// acceptance test</b>, over the real tables the two hosts actually receive.</para>
    /// </summary>
    public class HrotRoleComponentSetsTests
    {
        private static readonly NodeRole SimHostRoles =
            NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver;

        private static IRoleAffinityPolicy SimHost() => HrotRoleComponentSets.CreatePolicy(SimHostRoles);
        private static IRoleAffinityPolicy Cgf()     => HrotRoleComponentSets.CreatePolicy(NodeRole.Brain);

        /// <summary>⭐ Any template — birth-criticality is DERIVED from <c>[BirthCritical]</c> on the
        /// component type, so every template reports <c>SimTransform</c> without declaring it
        /// (<c>2026-09-13</c>, <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a).</summary>
        private static TkbTemplate BirthCriticalTemplate() => new TkbTemplate("RoleTableRail", 9901);

        /// <summary>Every component id this rail file names, so the "unclassified" sweep can skip them.</summary>
        private static BitMask512 ClassifiedOrBirthCritical()
        {
            var m = HrotRoleComponentSets.BrainOnlyComponents;
            var birth = HrotRoleComponentSets.BirthCriticalComponents;
            m.BitwiseOr(in birth);
            return m;
        }

        // ── THE RULING ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔒 <b>User ruling, <c>2026-09-12</c>: <i>"Simhost has no ai(brain). So it does not need"</i>.</b>
        /// ⭐ Stated as authority rather than as registration: a Muscle node may not OWN a brain component,
        /// whatever order the cluster happened to spawn the entity in.
        /// </summary>
        [Fact]
        public void TheMuscleRolesOwnNoBrainComponent_AndTheBrainRoleOwnsThemAll()
        {
            var muscle = SimHost().OwnedComponentSet;
            var brain  = Cgf().OwnedComponentSet;
            var brainOnly = HrotRoleComponentSets.BrainOnlyComponents;

            for (int id = 0; id < FdpConfig.MAX_COMPONENT_TYPES; id++)
            {
                if (!brainOnly.IsSet(id)) continue;

                Assert.False(muscle.IsSet(id), $"a Muscle node must not own brain component id {id}");
                Assert.True(brain.IsSet(id),  $"the Brain node must own brain component id {id}");
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>§6 step 4's ACCEPTANCE TEST, expressed over the real tables.</b> A brain-enabled entity
        /// CREATED on SimHost leaves SimHost without authority over <c>BehaviorState</c>, and the Brain
        /// node CLAIMS it when it promotes the ghost. ⇒ exactly one node owns it, and
        /// <c>TacticalIntentResolutionSystem</c>'s gate (<c>:95</c>) passes on that one.
        /// </summary>
        [Fact]
        public void ASimHostCreatedBrainEntity_LeavesBehaviorStateForTheBrainNode()
        {
            int behaviorState = ComponentType<BehaviorState>.ID;
            var template = BirthCriticalTemplate();

            var creatorKeeps = SimHost().OwnableMask(template, isCreator: true,  default);
            var brainClaims  = Cgf()    .OwnableMask(template, isCreator: false, default);

            Assert.False(creatorKeeps.IsSet(behaviorState));
            Assert.True(brainClaims.IsSet(behaviorState));
        }

        // ── MAP2D — CE-271 seam ③ (§4.1) ────────────────────────────────────────────────────────────

        private static IRoleAffinityPolicy Map2D() => HrotRoleComponentSets.CreatePolicy(NodeRole.Map2D);

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-271</c> — a Map2D node OWNS its overlay/route authorship, and NOTHING dynamic.</b>
        /// 🔒 <c>R-138</c>: a Map2D node is a real creating owner, so its owned set must be NON-EMPTY
        /// (an empty row is <c>CE-256</c> — a Map2D-created overlay would own only <c>SimTransform</c> and
        /// its egress <c>HasAuthority</c> gate would fail, so overlays silently stop publishing). ⛔ And it
        /// must exclude everything combat/muscle/brain so a Map2D-created TANK declines those. This is the
        /// precise set the Map2D egress translators gate on. 📄 <c>DESIGN_Node_Roles_And_Policies.md</c> §4.1.
        /// </summary>
        [Fact]
        public void Map2DOwns_ItsOverlayAndRouteAuthorship_AndNothingDynamic()
        {
            var owned = Map2D().OwnedComponentSet;

            // ⭐ Non-empty, and owns exactly the overlay/route authorship components.
            //   ⚠ EditablePolyline/RoutePlan are managed → identified by their [ComponentId] constants,
            //   not ComponentType<T>.ID (unmanaged-only).
            Assert.True(owned.IsSet(GlobalComponentIds.EditablePolyline),
                "a Map2D node must own EditablePolyline so MapVisualOverlayEgress can publish its overlays");
            Assert.True(owned.IsSet((int)Hrot.Map.Definitions.HrotComponentIds.RoutePlan),
                "a Map2D node must own RoutePlan (route authorship)");

            // ⛔ Owns NOTHING a tank needs distributed — those go to the Brain/Muscle on promotion.
            foreach (int id in new[] { ComponentType<BehaviorState>.ID,
                                       ComponentType<SimVelocity>.ID })
            {
                Assert.False(owned.IsSet(id),
                    $"a Map2D node must NOT own dynamic component id {id} — a Map2D-created tank declines it");
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The equal-creation acceptance, Map2D leg.</b> A brain-enabled entity CREATED on a Map2D
        /// node keeps ONLY its <c>SimTransform</c> birthright — it declines <c>BehaviorState</c> (Brain
        /// claims it on promotion) and <c>SimVelocity</c> (Muscle claims it), and its kinematics
        /// <c>SimTransform</c> is then handed to a Muscle by the auto-takeover grant (<c>CE-271</c> seam ①).
        /// </summary>
        [Fact]
        public void AMap2DCreatedBrainEntity_KeepsOnlyItsSimTransformBirthright()
        {
            var template = BirthCriticalTemplate();
            var creatorKeeps = Map2D().OwnableMask(template, isCreator: true, default);

            Assert.True(creatorKeeps.IsSet(ComponentType<SimTransform>.ID),
                "the creator keeps SimTransform (birthright) whatever its role, else the spawn position is never published");
            Assert.False(creatorKeeps.IsSet(ComponentType<BehaviorState>.ID),
                "a Map2D creator must decline BehaviorState — the Brain owns it");
            Assert.False(creatorKeeps.IsSet(ComponentType<SimVelocity>.ID),
                "a Map2D creator must decline SimVelocity — the Muscle owns it");
        }

        // ── THE TWO-SET MODEL (§3.9) ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>The measured case that forced §3.9.</b> 🔒 User: <i>"intents are brain owned components
        /// that must be replicated to muscle so musle can read and act on them. so muscle cant simply stop
        /// registwring them because they are brain ones."</i>
        ///
        /// <para>⇒ <c>NavigationIntent</c> and <c>MissionPlanQueue</c> must be OUT of the Muscle node's
        /// owned set and IN its register set. ⛔ A table that satisfied only the first half would make a
        /// Muscle node stop receiving its own orders.</para>
        /// </summary>
        [Fact]
        public void TheMuscleRolesREGISTERWhatTheyRead_WithoutEverOwningIt()
        {
            var policy = SimHost();

            foreach (int id in new[] { ComponentType<NavigationIntent>.ID,
                                       ComponentType<MissionPlanQueue>.ID })
            {
                Assert.False(policy.OwnedComponentSet.IsSet(id));
                Assert.True(policy.ReadComponentSet.IsSet(id));
                Assert.True(policy.RegisterComponentSet.IsSet(id));
            }
        }

        /// <summary>
        /// ⛔ <b>READ never grants authority.</b> The read pair must not reappear through
        /// <see cref="IRoleAffinityPolicy.OwnableMask"/>, on either leg — that would hand the Muscle node
        /// authority over the intent the Brain owns, and their egress would fight.
        /// </summary>
        [Fact]
        public void TheReadSetNeverBecomesAuthority_OnEitherLeg()
        {
            var template = BirthCriticalTemplate();
            var asCreator  = SimHost().OwnableMask(template, isCreator: true,  default);
            var asPromoter = SimHost().OwnableMask(template, isCreator: false, default);

            foreach (int id in new[] { ComponentType<NavigationIntent>.ID,
                                       ComponentType<MissionPlanQueue>.ID })
            {
                Assert.False(asCreator.IsSet(id));
                Assert.False(asPromoter.IsSet(id));
            }
        }

        // ── THE BIRTHRIGHT (§3.1) ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>No role may own a birth-critical component, and the failure is measured.</b>
        /// <c>GeoSpatialIngressTranslator.cs:90</c> skips applying an incoming position when
        /// <c>HasAuthority&lt;SimTransform&gt;</c> is true. ⇒ a promoting node that claimed
        /// <c>SimTransform</c> BY ROLE would declare itself the owner of a position it does not simulate,
        /// and every ghost on that node would stop accepting the owner's updates.
        /// </summary>
        [Fact]
        public void NoRoleOwnsABirthCriticalComponent()
        {
            int simTransform = ComponentType<SimTransform>.ID;

            Assert.False(SimHost().OwnedComponentSet.IsSet(simTransform));
            Assert.False(Cgf()    .OwnedComponentSet.IsSet(simTransform));
        }

        /// <summary>
        /// ⭐⭐ <b>…and the creator keeps it anyway, whatever its role</b> — the architect's correction.
        /// ⛔ A promoter gets no birthright, or both nodes would own the position.
        /// </summary>
        [Fact]
        public void TheCreatorKeepsSimTransform_WhateverItsRole_AndOnlyAsCreator()
        {
            int simTransform = ComponentType<SimTransform>.ID;
            var template = BirthCriticalTemplate();

            foreach (var policy in new[] { SimHost(), Cgf() })
            {
                Assert.True(policy.OwnableMask(template, isCreator: true,  default).IsSet(simTransform));
                Assert.False(policy.OwnableMask(template, isCreator: false, default).IsSet(simTransform));
            }
        }

        // ── THE COMPLEMENT, AND WHY IT IS NOT AN ENUMERATION ──────────────────────────────────────

        /// <summary>
        /// ⛔⛔⛔ <b>THE <c>CE-256</c> GUARD, and it is the reason the tables are complements.</b>
        /// <c>NetworkSpawningSystem.cs:237</c> REPLACES the blanket grant with
        /// <c>AuthorityMask &amp;= OwnableMask(...)</c>. ⇒ an ENUMERATED muscle set would leave a
        /// SimHost-created tank owning the twenty components §3.9a happens to name and <b>nothing else</b>
        /// — no <c>EntityInfo</c>, no health, no map display — which is <c>CE-256</c> verbatim:
        /// <i>"owns nothing, so nothing it is responsible for ever moves."</i>
        ///
        /// <para>⭐ So: every component id that is NEITHER brain-only NOR birth-critical stays owned by
        /// BOTH roles, exactly as it is today. 🔒 This rail is what a future positive enumeration has to
        /// argue with.</para>
        /// </summary>
        [Fact]
        public void EveryUnclassifiedComponentStaysOwnedByBothRoles()
        {
            var muscle    = SimHost().OwnedComponentSet;
            var brain     = Cgf().OwnedComponentSet;
            var excluded  = ClassifiedOrBirthCritical();

            for (int id = 0; id < FdpConfig.MAX_COMPONENT_TYPES; id++)
            {
                if (excluded.IsSet(id)) continue;

                Assert.True(muscle.IsSet(id), $"unclassified component id {id} must stay Muscle-ownable");
                Assert.True(brain.IsSet(id),  $"unclassified component id {id} must stay Brain-ownable");
            }
        }

        /// <summary>
        /// ⭐ <b>The disjointness the design promises, stated where it is actually true:</b> over the
        /// CLASSIFIED set. ⛔ Brain and Muscle deliberately OVERLAP over the unclassified remainder (the
        /// rail above) — that remainder is what each host legitimately owns for the entities it creates.
        /// </summary>
        [Fact]
        public void BrainAndMuscleAreDisjointOverTheClassifiedSet()
        {
            var muscle    = SimHost().OwnedComponentSet;
            var brainOnly = HrotRoleComponentSets.BrainOnlyComponents;

            Assert.False(BitMask512.HasAny(in muscle, in brainOnly));
        }

        // ── ONE TABLE, NOT TWO ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>The safety property as an API rail.</b> Two nodes running the same function over the
        /// same entity cannot disagree — ⛔ which is false the moment two hosts author their own tables.
        /// <see cref="HrotRoleComponentSets.CreatePolicy"/> takes a ROLE and nothing else, so the table is
        /// not a parameter any host can vary.
        /// </summary>
        [Fact]
        public void BothHostsDeriveTheirPolicyFromTheSameTable()
        {
            // The Brain-only bits are the ONLY difference between the two hosts' owned sets.
            var muscle = SimHost().OwnedComponentSet;
            var brain  = Cgf().OwnedComponentSet;

            var difference = brain;
            difference.BitwiseAndNot(in muscle);

            Assert.Equal(HrotRoleComponentSets.BrainOnlyComponents, difference);
        }

        /// <summary>
        /// ⚠ <b><c>Perception</c> and <c>NavigationSolver</c> contribute nothing today, deliberately</b> —
        /// every host that declares them also declares <c>MuscleGround</c>. ⭐ The rail exists so that
        /// giving them positive sets later is a visible decision rather than a silent narrowing.
        /// </summary>
        [Fact]
        public void ThePerceptionAndNavigationRolesAddNothingBeyondMuscleGround()
        {
            var muscleOnly = HrotRoleComponentSets.CreatePolicy(NodeRole.MuscleGround).OwnedComponentSet;
            var allThree   = SimHost().OwnedComponentSet;

            Assert.Equal(muscleOnly, allThree);
        }
    }
}
