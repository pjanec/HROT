using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Map.Common;

/// <summary>
/// ⭐⭐⭐ <b><c>P3</c> step <c>4</c> — THE CLUSTER'S role→component tables, authored ONCE.</b>
///
/// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9 (the two-set model), §3.9a (the measured
/// per-component classification), §6 step <c>4</c>, §6i (the as-built).</para>
///
/// <para>⛔⛔ <b>THE ENGINE NEVER LEARNS WHAT A ROLE MEANS.</b> 🔒 User, <c>2026-09-12</c>: <i>"the bitmask
/// for components is correct approach, fdp should not understand what a brain and muscle really
/// mean."</i> ⇒ <c>Fdp.Toolkits</c> holds the MECHANISM (<see cref="RoleAffinityPolicy"/>); THIS file —
/// in the application layer — holds the MEANING. Nothing below <c>Hrot</c> knows that
/// <c>BehaviorState</c> is a brain component.</para>
///
/// <para>⭐⭐⭐ <b>ONE table, handed to every host.</b> The safety property of the whole design is that two
/// nodes evaluating the same function over the same entity cannot disagree — ⛔ which is false the moment
/// two hosts author their own tables. <see cref="CreatePolicy"/> exists so a host supplies only its
/// DECLARED ROLE and can never supply a different table.</para>
/// </summary>
public static class HrotRoleComponentSets
{
    /// <summary>
    /// ⛔⛔ <b>The components a node WITHOUT the Brain role must not own</b> — §3.9a's classification,
    /// measured <c>2026-09-12</c>.
    ///
    /// <para>⚠ <b>Two different relationships live in here, deliberately.</b> Most rows are ABSENT on a
    /// Muscle node (no system touches them, zero wire references); <c>NavigationIntent</c> and
    /// <c>MissionPlanQueue</c> are READ (Brain-owned, replicated IN, consumed by Muscle systems). ⭐ Both
    /// are excluded from the Muscle OWNED set for the same reason — the Brain owns them — and the READ pair
    /// is added back to <see cref="Read"/>, which drives REGISTRATION and never authority (§3.9).</para>
    ///
    /// <para>⛔ <b>Nothing UNCLASSIFIED is in here.</b> §3.9a classified ~20 components; the codebase has
    /// hundreds. A component nobody has classified stays owned by whoever creates it, exactly as today —
    /// see <see cref="Owned"/> for why that is expressed as a COMPLEMENT rather than an enumeration.</para>
    /// </summary>
    public static BitMask512 BrainOnlyComponents { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>Excluded from EVERY role's owned set</b> — the creator keeps these by BIRTHRIGHT and hands
    /// them off through the existing <c>DeferredTakeOwnership</c> path (§3.1).
    ///
    /// <para>🔴🔴 <b>This exclusion is load-bearing, and the failure it prevents is measured.</b>
    /// <c>GeoSpatialIngressTranslator.cs:90</c> asks <c>repo.HasAuthority&lt;SimTransform&gt;(entity)</c>
    /// and SKIPS applying the incoming position when the answer is true. ⇒ if a PROMOTING node claimed
    /// <c>SimTransform</c> by role, it would declare itself the owner of a position it does not simulate
    /// and stop accepting the owner's updates — <b>every ghost on that node would freeze</b>. ⛔ The
    /// handover is explicit, not role-derived, and that is the whole point of the birthright category.</para>
    ///
    /// <para>⚠ It mirrors what step <c>0</c> seeds into <c>TkbTemplate.BirthCriticalComponents</c>. The two
    /// are asserted to agree by a rail rather than shared, because the template's list is per-TEMPLATE and
    /// this one is per-CLUSTER.</para>
    /// </summary>
    public static BitMask512 BirthCriticalComponents { get; }

    /// <summary>
    /// 🔴 <b>The Muscle role's <c>readComponentSet</c></b> — owned by the Brain, replicated IN, consumed
    /// here. 🔒 User, <c>2026-09-12</c>: <i>"intents are brain owned components that must be replicated to
    /// muscle so musle can read and act on them. so muscle cant simply stop registwring them because they
    /// are brain ones."</i>
    ///
    /// <para>📐 Measured: <c>NavigationIntent</c> has 16 wire references and
    /// <c>NavigationIntentBridgeSystem</c> consumes it in <c>SimHostCoreLogicPack</c>;
    /// <c>MissionPlanQueue</c> has 9 and <c>MissionPlanTranslator</c> genuinely persists it.</para>
    /// </summary>
    public static BitMask512 MuscleReadComponents { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>The <c>ownedComponentSet</c> per role — expressed as a COMPLEMENT, and that is a decision,
    /// not a shortcut.</b>
    ///
    /// <para>📄 <b>The owning section is <c>DESIGN_Role_Affinity_Ownership.md</c> §3.9c</b>, and
    /// <c>DESIGN_Entity_Creation_Unification.md</c> §4.1 asks the same question from the creation side
    /// (<i>"any node may create an entity — so who owns its <c>EntityInfo</c>?"</i>). ⛔ If this table ever
    /// becomes a positive enumeration, BOTH sections' answers change.</para>
    ///
    /// <para>⛔⛔ <b>An ENUMERATED muscle set would silently un-own everything nobody has classified yet.</b>
    /// <c>NetworkSpawningSystem.cs:237</c> does <c>AuthorityMask &amp;= OwnableMask(...)</c> — it REPLACES
    /// the blanket "I own everything I materialised" grant. ⇒ a positive list containing the twenty
    /// components §3.9a names would leave a SimHost-created tank owning twenty components and nothing else:
    /// no <c>EntityInfo</c>, no health, no map display. 🔴 That is <c>CE-256</c> — <i>"owns nothing, so
    /// nothing it is responsible for ever moves"</i> — reproduced by this design's own step 4.</para>
    ///
    /// <para>⭐ The complement makes the change's blast radius EXACTLY the ruling and nothing else: a Muscle
    /// node's authority differs from today's by precisely <see cref="BrainOnlyComponents"/>. ⚠ The positive
    /// enumeration is the upgrade path once the classification is complete — it is strictly more precise
    /// and strictly more dangerous, and it must not be taken before every component has a row.</para>
    ///
    /// <para>⭐⭐ <b>Brain and Muscle are still DISJOINT over the classified set</b>, which is the property
    /// step 1's rail asserts: the Muscle mask is the Brain mask minus <see cref="BrainOnlyComponents"/>, so
    /// no Muscle node can ever claim a component the Brain node claims. ⛔ They deliberately OVERLAP over
    /// the unclassified remainder, because that remainder is what both hosts legitimately own for the
    /// entities they each create.</para>
    ///
    /// <para>⚠ <b><c>Perception</c> and <c>NavigationSolver</c> have NO entry, deliberately.</b> A declared
    /// role absent from the table contributes nothing (<see cref="RoleAffinityPolicy"/> documents this), and
    /// every host that declares them also declares <c>MuscleGround</c>, whose complement already covers the
    /// EQS and navigation components. ⛔ Giving them positive sets would be the enumeration trap above, one
    /// role at a time.</para>
    /// </summary>
    public static IReadOnlyDictionary<NodeRole, BitMask512> Owned { get; }

    /// <summary>
    /// 🔴 <b>The <c>readComponentSet</c> per role.</b> Contributes to
    /// <see cref="IRoleAffinityPolicy.RegisterComponentSet"/> and to nothing else — ⛔ it can never grant
    /// authority (§3.9).
    /// </summary>
    public static IReadOnlyDictionary<NodeRole, BitMask512> Read { get; }

    static HrotRoleComponentSets()
    {
        // ⭐ Ids come from [ComponentId] attributes, so touching ComponentType<T>.ID here is
        //   order-independent and cannot drift between nodes. ⛔ Auto-assignment no longer exists —
        //   ComponentType.cs:138-147 THROWS for a type with no [ComponentId] — which is what makes a
        //   composition-time mask of component ids safe to build before any world is registered.
        var brainOnly = default(BitMask512);

        // ── ABSENT on a Muscle node: no SimHost system touches them, zero wire references ──────────
        brainOnly.SetBit(ComponentType<BehaviorState>.ID);
        // ⛔ P4 §2 ② (2026-09-22): BrainBlackboard retired — nothing had filled it since P3-C
        //    (CE-312), so replicating it shipped a permanently-zero region to every brain node.
        //    ⚠ Its id 23 stays RESERVED, for the same reason 74 is.
        // ⛔ P4-① (2026-09-22): Blackboard1024 retired — §30.13. ⚠ Its id 74 stays RESERVED in
        //    GlobalComponentIds rather than being reused, so a stale recording cannot bind it to a
        //    different component.
        // ⛔ O7c-② (2026-09-22): BrainBTreeState retired — the cursor rides in the occurrence store,
        //    whose tier components this mask already covers.
        // ⛔ O7c-① (2026-09-22): BrainHsm64 retired — nothing ever attached it, so this bit
        //    declined a component that was never present. ⚠ Its id 35 stays RESERVED, like 23 and 74.
        brainOnly.SetBit(ComponentType<BrainHsm128>.ID);
        // ⭐ The three channels: their only consumers are ActionDispatchModule and
        //   ChannelArbitrationSystem, both registered by CgfLogicPack alone (§3.9a).
        brainOnly.SetBit(ComponentType<LocomotionChannel>.ID);
        brainOnly.SetBit(ComponentType<WeaponChannel>.ID);
        brainOnly.SetBit(ComponentType<InteractionChannel>.ID);
        // ⭐ Read only by CognitiveInterruptSystem (Brain) and the Stride animation reactor.
        brainOnly.SetBit(ComponentType<PreviousCapabilities>.ID);
        // ⭐ Written only by TraceBufferLifecycleSystem, which runs in the Brain's pack. SimHost's
        //   readers are extract-only diagnostic translators, and they read the COMPONENT, not its
        //   authority bit.
        brainOnly.SetBit(ComponentType<BTreeTraceWorkingMemory1024>.ID);
        brainOnly.SetBit(ComponentType<HsmTraceWorkingMemory1024>.ID);

        // ── READ on a Muscle node: Brain-OWNED, replicated IN, consumed here (§3.9) ────────────────
        var muscleRead = default(BitMask512);
        muscleRead.SetBit(ComponentType<NavigationIntent>.ID);
        muscleRead.SetBit(ComponentType<MissionPlanQueue>.ID);
        // ⛔ They are Brain-owned, so they are excluded from the Muscle OWNED set too — the READ table
        //   is what puts them back into REGISTER without ever granting authority.
        brainOnly.BitwiseOr(in muscleRead);

        // ⭐⭐ DERIVED from [BirthCritical] on the component type (2026-09-13) — this used to hand-list
        //   SimTransform, making it a THIRD producer of one fact alongside the TKB templates and the
        //   app-layer convention. 📄 docs/designs/tkb-1/DESIGN.md §6.6a.
        var birthCritical = default(BitMask512);
        foreach (int id in ComponentAttributeSets.BirthCritical)
            birthCritical.SetBit(id);

        var all = default(BitMask512);
        all.SetAll();

        var brainOwned = all;
        brainOwned.BitwiseAndNot(in birthCritical);

        var muscleOwned = brainOwned;
        muscleOwned.BitwiseAndNot(in brainOnly);

        // ── ⭐⭐⭐ Map2D OWNED — CE-271 seam ③ (2026-09-13) ───────────────────────────────────────────
        // 🔒 R-138 (canon): every ECS node creates entities it owns and distributes the rest. A Map2D
        //   node (IG) is a real creating owner, so it needs an OWNED set — WITHOUT one it runs a null
        //   policy, keeps every component it materialises, and a Muscle promoting a Map2D-created tank
        //   would fight it for authority (two owners). WITH this set, a Map2D-created TANK owns only its
        //   SimTransform birthright (kept whatever the role) and DECLINES combat/muscle/brain, which the
        //   Brain/Muscle then claim on promotion — exactly the equal-creation contract.
        //
        // ⭐ The set is the overlay/route geometry a Map2D node AUTHORS and replicates OUT — precisely the
        //   TargetComponentIds its own egress translators gate on:
        //     · EditablePolyline — MapVisualOverlayEgressTranslator (dtMapVisualOverlay). Overlay STYLE
        //       (MapOverlayStyle) rides on the same sample under this authority, so it needs no own bit.
        //     · RoutePlan        — MapRouteEgressTranslator (dtEntityMission/route).
        // ⛔ It is deliberately NON-EMPTY: an empty row (CE-256) would make a Map2D-created OVERLAY own
        //   only SimTransform, so MapVisualOverlayEgress's HasAuthority gate fails and overlays silently
        //   stop publishing. ⛔ It deliberately excludes everything combat/brain/muscle/kinematic so a
        //   Map2D-created tank declines those. 📄 DESIGN_Node_Roles_And_Policies.md §4.1.
        // ⚠ A PURE Map2D node does not yet run MapRouteEgress (it lives in KinematicTranslatorPack,
        //   gated _roleHasMuscle) — owning RoutePlan is correct-in-principle but inert until that
        //   translator reaches Map2D; that move is a separate seam, out of scope here.
        // ⚠ EditablePolyline and RoutePlan are MANAGED components (classes), so ComponentType<T>.ID —
        //   which is unmanaged-only — cannot be used; their ids come from the same [ComponentId] constants
        //   the egress translators use (GlobalComponentIds.EditablePolyline = 117, HrotComponentIds
        //   .RoutePlan = 168), exactly as the birth-critical loop above sets bits by raw id.
        var map2dOwned = default(BitMask512);
        map2dOwned.SetBit(GlobalComponentIds.EditablePolyline);
        map2dOwned.SetBit((int)Hrot.Map.Definitions.HrotComponentIds.RoutePlan);

        BrainOnlyComponents     = brainOnly;
        BirthCriticalComponents = birthCritical;
        MuscleReadComponents    = muscleRead;

        Owned = new Dictionary<NodeRole, BitMask512>
        {
            [NodeRole.Brain]        = brainOwned,
            [NodeRole.MuscleGround] = muscleOwned,
            [NodeRole.Map2D]        = map2dOwned,
        };

        Read = new Dictionary<NodeRole, BitMask512>
        {
            [NodeRole.MuscleGround] = muscleRead,
        };
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE way a host gets a policy.</b> A host supplies its declared role and nothing else, so
    /// two hosts cannot be handed two different tables — the safety property of the design stated as an
    /// API. 📄 §6 step <c>4</c>.
    /// </summary>
    /// <param name="declaredRoles">
    /// The host's own constant — <c>CgfSubsystem.DefaultRole</c>, <c>SimHostApp.DefaultRole</c>, … .
    /// ⭐ Roles with no entry in <see cref="Owned"/> contribute nothing, so passing the host's full
    /// declaration is always correct.
    /// </param>
    public static IRoleAffinityPolicy CreatePolicy(NodeRole declaredRoles)
        => new RoleAffinityPolicy(
            declaredRoles,
            Owned,
            new SingleNodePerRoleShardProvider(declaredRoles),
            Read);
}
