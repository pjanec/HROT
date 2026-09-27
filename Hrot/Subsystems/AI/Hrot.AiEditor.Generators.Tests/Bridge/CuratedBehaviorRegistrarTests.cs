using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Hrot.AI.Behaviors.Generated;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-375</c> — the generated <c>CuratedBehaviorRegistrar</c> replaces the hand-written
/// one, and these rails are what says so.</b>
/// 📄 <c>DESIGN_Behavior_Self_Registration.md</c> §10.
///
/// <para>🔒 <b>User, <c>2026-09-27</c>:</b> *"can the `CgfCuratedBehaviorRegistrar` be replaced with
/// automatic boot-time reflection scan as almost everything else in the engine?"* — ⭐ it already WAS
/// reflection-discovered; what was hand-written was its BODY, and that body is now generated.</para>
///
/// <para>⛔⛔ <b><c>SR_R1</c> is the acceptance gate and it is a SET COMPARISON, not a count.</b>
/// 🔒 <c>CE-355</c>'s lesson, paid for once already: *"a matching NET count is not an explanation —
/// diff the SETS."*</para>
/// </summary>
public sealed class CuratedBehaviorRegistrarTests
{
    /// <summary>The registry facts that must survive the move, per behaviour.</summary>
    private readonly record struct Facts(
        string Name, byte BrainTier, bool HasBTree, bool HasHsm, string? ParamsType);

    private static ActionRegistry<byte, BTreeContext> NodeLogic() =>
        BTreeActionRegistryFactory.BuildFromAssembly(
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly);

    private static Dictionary<string, Facts> Snapshot(BehaviorRegistry beh)
    {
        var map = new Dictionary<string, Facts>(StringComparer.Ordinal);
        foreach (var name in beh.GetRegisteredNames())
        {
            if (!beh.TryGetId(name, out int id)) continue;
            if (!beh.TryGetDefinition(id, out var def)) continue;
            map[name] = new Facts(
                def.Name,
                def.BrainTier,
                def.BTreeInterpreter is not null,
                def.HsmDefinition is not null,
                def.BlackboardLayoutType?.FullName);
        }
        return map;
    }

    // ── SR_R1 — RETIRED WITH THE CLASS IT COMPARED AGAINST ────────────────────
    //
    // SR_R1 / SR_R1b registered through BOTH the hand-written CgfCuratedBehaviorRegistrar and the
    // generated CuratedBehaviorRegistrar and asserted the two BehaviorRegistry contents were equal
    // AS SETS -- not as counts (CE-355's lesson). They passed, and they were RED-PROVED: dropping
    // `Curated = true` from one topology reddened SR_R1 naming the missing behaviour.
    //
    // They are deleted with CE-374 because their subject is gone, exactly as the design said they
    // would be. Leaving a rail that references a deleted class is the failure this note prevents;
    // "delete a rail when its SUBJECT is gone, re-point it when only its SPELLING changed, never
    // leave it empty."
    //
    // The standing cover is everything below.

    // ── SR_R2 — the standing cover, and it closes a live hazard ───────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>SR_R2</c> — every <c>BehaviorNames</c> constant resolves to a registered behaviour.</b>
    ///
    /// <para>📐 <b>The real hazard, measured:</b> <c>BehaviorNames.MoveToLocation</c>, the
    /// <c>[BTreeDefinition("MoveToLocation")]</c> string and the generated
    /// <c>FbtTreeCatalog.GetMoveToLocation</c> are <b>three spellings and nothing checked they
    /// agree</b>. ⛔ Registering the right blob under a wrong name compiles perfectly.</para>
    /// </summary>
    [Fact]
    public void SR_R2_EveryBehaviorNamesConstant_ResolvesToARegisteredBehaviour()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        // ⚠ Only the names this registrar OWNS. HullDownAttackRun and PlatoonHillAttack are
        //   registered by their generated JSON registrars, so they are resolver-only here.
        string[] curated =
        {
            Hrot.Map.Definitions.Behavior.BehaviorNames.MoveToLocation,
            Hrot.Map.Definitions.Behavior.BehaviorNames.FollowRoute,
            Hrot.Map.Definitions.Behavior.BehaviorNames.JoinFormation,
            Hrot.Map.Definitions.Behavior.BehaviorNames.WanderMilitary,
            Hrot.Map.Definitions.Behavior.BehaviorNames.FireAtTarget,
            Hrot.Map.Definitions.Behavior.BehaviorNames.Idle,
        };

        foreach (var name in curated)
            beh.TryGetId(name, out _).Should().BeTrue(
                $"'{name}' is a BehaviorNames constant and must name a registered behaviour");
    }

    // ── SR_R3 — the Idle machine is REAL, not just catalogued ─────────────────

    /// <summary>
    /// ⭐⭐ <b><c>SR_R3</c> — the hand-built <c>Idle</c> machine survives the move to
    /// <c>[HsmDefinition]</c> with a usable blob.</b>
    ///
    /// <para>🔒 <b>Why this asserts the BLOB and not a catalog entry:</b> the retired BTree
    /// orchestrator passed its shape rails for months while emitting a call to a method defined
    /// nowhere, because nothing ever RAN what it emitted.</para>
    /// </summary>
    [Fact]
    public void SR_R3_TheIdleMachine_RegistersAUsableBlob()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        beh.TryGetId(Hrot.Map.Definitions.Behavior.BehaviorNames.Idle, out int id).Should().BeTrue();
        beh.TryGetDefinition(id, out var def).Should().BeTrue();

        def!.BrainTier.Should().Be(BehaviorConstants.BrainTierHsm);
        def.HsmDefinition.Should().NotBeNull("the graph-returning [HsmDefinition] must compile to a blob");
        def.BTreeInterpreter.Should().BeNull("Idle is an HSM, not a tree");
    }

    // ── SR_R4 — CE-370 ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>SR_R4</c> — <c>HsmMetadata</c> is present, which is <c>CE-370</c>.</b>
    ///
    /// <para>📐 Measured <c>2026-09-27</c>: <c>HsmBridgeEmitCore</c> never emits <c>HsmMetadata</c>,
    /// so before this change the ONLY definition in the repo carrying it was the hand-written Idle —
    /// while three consumers read it (<c>HsmTraceWorkingMemoryTranslator</c>,
    /// <c>HsmTraceWorkingMemoryRenderer</c>, <c>BrainTickSystem</c>). ⭐ The catalog is the one place
    /// that holds the graph, so it is the only place that can supply both.</para>
    ///
    /// <para>⚠ <b>Scope, stated honestly:</b> this pins the CURATED path. The JSON-authored half of
    /// <c>CE-370</c> is NOT closed by this change and its row stays open.</para>
    /// </summary>
    [Fact]
    public void SR_R4_TheCuratedHsm_CarriesItsMachineMetadata()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        beh.TryGetId(Hrot.Map.Definitions.Behavior.BehaviorNames.Idle, out int id).Should().BeTrue();
        beh.TryGetDefinition(id, out var def).Should().BeTrue();

        def!.HsmMetadata.Should().NotBeNull(
            "trace symbolication reads BehaviorDefinition.HsmMetadata; a null renders numeric ids");
    }

    // ── SR_R5 — both resolver arities ─────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>SR_R5</c> — the 6-param and the 3-param resolver shapes BOTH bind.</b>
    /// ⚠ <c>FollowRoute</c> is the 3-param case the generator wraps; <c>MoveToLocation</c> is the
    /// 6-param case bound directly. ⛔ A generator that silently dropped the short shape would leave
    /// two behaviours unparsed and every other rail green.
    /// </summary>
    [Fact]
    public void SR_R5_BothResolverArities_Bind()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        foreach (var name in new[]
                 {
                     Hrot.Map.Definitions.Behavior.BehaviorNames.FollowRoute,     // 3-param, wrapped
                     Hrot.Map.Definitions.Behavior.BehaviorNames.MoveToLocation,  // 6-param, direct
                 })
        {
            beh.TryGetId(name, out int id).Should().BeTrue();
            beh.TryGetDefinition(id, out var def).Should().BeTrue();
            def!.ParseParams.Should().NotBeNull($"'{name}' declares a [BehaviorResolver]");
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The overlay's params TYPE reaches a topology this registrar does NOT own.</b>
    /// <c>HullDownAttackRun</c>'s topology belongs to its generated JSON registrar and expresses the
    /// layout only via <c>ManagedBlackboardVariables</c>, so <c>RegisterResolver</c>'s third argument
    /// is the only carrier.
    ///
    /// <para>⚠ <b>An overlay for a name with no topology is NOT observable</b> — it waits in a
    /// pending map. ⛔ My first draft of this rail asserted <c>TryGetId</c> would succeed and it
    /// reddened, correctly. ⭐ Planting the topology afterwards is both the honest assertion and the
    /// production order: the JSON registrar and this one run in the same scan, in either order.</para>
    /// </summary>
    [Fact]
    public void SR_R5b_TheOverlayParamsType_ReachesATopologyItDoesNotOwn()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        const string name = "HullDownAttackRun";
        beh.TryGetId(name, out _).Should().BeFalse(
            "this registrar supplies only the RESOLVER for it — registering a topology would " +
            "hard-error against the generated JSON registrar's");

        // The generated JSON registrar's arrival, stood in for.
        beh.Register(name, new BehaviorDefinition
        {
            Name      = name,
            BrainTier = BehaviorConstants.BrainTierBTree,
        });

        beh.TryGetId(name, out int id).Should().BeTrue();
        beh.TryGetDefinition(id, out var def).Should().BeTrue();

        def!.ParseParams.Should().NotBeNull("the pending overlay must bind when the topology arrives");
        def.BlackboardLayoutType.Should()
            .Be(typeof(Hrot.AI.Behaviors.Brains.HullDownAttackParams));
    }

    // ── SR_R6 / SR_R7 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>SR_R6</c> — <c>R-149</c> survives the move to attributes:</b> two curated bindings for
    /// one params region THROW rather than racing. ⛔ A silent last-writer-wins would pick one by
    /// source order, which is the *"not a precedence rule, a race"* shape <c>R-132</c> names.
    /// </summary>
    [Fact]
    public unsafe void SR_R6_TwoCuratedResolversForOneName_StillThrow()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        var act = () => beh.RegisterResolver(
            Hrot.Map.Definitions.Behavior.BehaviorNames.MoveToLocation,
            (json, memory, capacity, world, self, host) => { });

        act.Should().Throw<Exception>("R-149: two explicit bindings for one params region must throw");
    }

    /// <summary>
    /// ⭐ <b><c>SR_R7</c> — registration is idempotent across a reload.</b> ⚠ Hot reload re-runs
    /// registrars into a FRESH staging registry, so the second pass must yield the same set.
    /// </summary>
    [Fact]
    public void SR_R7_ASecondRegistration_YieldsTheSameSet()
    {
        var first = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(first, NodeLogic());

        var second = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(second, NodeLogic());

        Snapshot(second).Should().BeEquivalentTo(Snapshot(first));
    }

    // ── the opt-in is what keeps the non-curated definitions out ──────────────

    /// <summary>
    /// ⛔⛔ <b>The <c>Curated</c> opt-in is load-bearing, and this rail is why it exists.</b>
    ///
    /// <para>📐 Measured <c>2026-09-27</c>: <c>Hrot.AI.Behaviors</c> carries <b>nine</b>
    /// <c>[BTreeDefinition]</c> methods and only <b>five</b> are curated topologies.
    /// <c>HideInCover_BT</c>/<c>_v2</c> are registered as behaviours nowhere, and
    /// <c>PlatoonHillAttack</c>/<c>HullDownAttackRun</c> have their topology owned by generated JSON
    /// registrars. ⇒ *"every [BTreeDefinition] is a behaviour"* is false four ways out of nine, and
    /// registering the last two here would <b>hard-error on a duplicate name</b>.</para>
    /// </summary>
    [Fact]
    public void TheOptIn_KeepsNonCuratedDefinitionsOut()
    {
        var beh = new BehaviorRegistry();
        CuratedBehaviorRegistrar.Register(beh, NodeLogic());

        var names = beh.GetRegisteredNames().ToHashSet(StringComparer.Ordinal);

        names.Should().NotContain("HideInCover_BT");
        names.Should().NotContain("HideInCover_BT_v2");

        // ⚠ These two ARE present as resolver overlays; what must be absent is a TOPOLOGY.
        foreach (var name in new[] { "HullDownAttackRun", "PlatoonHillAttack" })
        {
            if (!beh.TryGetId(name, out int id)) continue;
            beh.TryGetDefinition(id, out var def).Should().BeTrue();
            def!.BTreeInterpreter.Should().BeNull(
                $"'{name}' topology is owned by its GENERATED registrar — registering it here would " +
                "hard-error on a duplicate name");
        }
    }
}
