using System;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐⭐ <b><c>E3b-0</c> — the HSM registrar emits WHICH VARIABLE each state's occurrence seeds from.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6 / §28.6a.
///
/// <para>🔴🔴 <b>The gap these pin.</b> <c>E3a</c> gave every hosted occurrence its own params bytes,
/// but all of them seeded from <c>BehaviorParameters[0] + 0</c>, so two parallel regions running one
/// asset got their own COPY of the SAME authored value. ⛔ The BTree bridge avoids this by emitting one
/// adapter PER NODE at a per-site key; the HSM dispatcher takes ONE thunk per <c>ushort</c> action id,
/// so there is nowhere to bake a per-site offset — the binding has to travel as data.</para>
///
/// <para>⚠ <b>These rails ARE about the text</b>, which the sibling suite warns is usually the weaker
/// claim. ⭐ Here it is the right one: the runtime half is proved by <c>O7_R28</c>–<c>O7_R31</c> in
/// <c>Fdp.Toolkits.Tests</c> (including one driven through a real kernel tick), so what is left to
/// prove is exactly that the EMITTER produces the table — and the gating that keeps every unbound
/// asset byte-identical.</para>
/// </summary>
public sealed class HsmStateParamBindingEmissionTests
{
    private static readonly Guid AssetId  = new("bb3b0000-0000-0000-0000-0000000003b0");
    private static readonly Guid StateOne = new("bb3b0000-0000-0000-0000-00000000aaa1");
    private static readonly Guid StateTwo = new("bb3b0000-0000-0000-0000-00000000bbb2");

    /// <summary>Two packed input variables, and two states that may bind them.</summary>
    private static HsmAssetDto MakeDto(string? bindOne, string? bindTwo)
    {
        var dto = new HsmAssetDto { AssetId = AssetId, Name = "BindingHsm" };
        dto.Blackboard.Managed = true;

        foreach (var (name, typeId) in new[] { ("Alpha", "System.Int32"), ("Beta", "System.Single") })
        {
            dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
            {
                Name = name,
                Type = new HsmBlackboardTypeRefDto { TypeId = typeId },
                Role = BlackboardVariableRole.Input,
            });
        }

        dto.States.Add(new StateNodeDto
        {
            StableId = StateOne, Name = "One",
            OnEntry = new BehaviorActionBindingDto { MethodFqn = "Demo.Actions.Work", ExpressionTargetField = bindOne },
        });
        dto.States.Add(new StateNodeDto
        {
            StableId = StateTwo, Name = "Two",
            OnEntry = new BehaviorActionBindingDto { MethodFqn = "Demo.Actions.Work", ExpressionTargetField = bindTwo },
        });
        return dto;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Two states bound to two variables emit two DIFFERENT offsets, keyed by authoring id.</b>
    ///
    /// <para>⭐ <c>StableId</c>s rather than flat indices, because the emitter does not know the
    /// flattener's ordering — <c>MachineMetadata.StateStableIds</c> recovers it at runtime (<c>O7_R30</c>
    /// pins the other side of that).</para>
    /// </summary>
    [Fact]
    public void TwoBoundStates_EmitTwoDistinctSeedOffsets()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto("Alpha", "Beta"));

        bridge.Should().Contain("HsmParamBindings.Register(blob",
            "the registrar must hand the runtime its state->offset table");
        bridge.Should().Contain(StateOne.ToString());
        bridge.Should().Contain(StateTwo.ToString());

        // ⭐⭐ THE RAIL. Two states, two DIFFERENT offsets — 🔴 before E3b-0 every occurrence used 0.
        int alphaAt = bridge.IndexOf(StateOne.ToString(), StringComparison.Ordinal);
        int betaAt  = bridge.IndexOf(StateTwo.ToString(), StringComparison.Ordinal);
        string alphaLine = bridge.Substring(alphaAt, bridge.IndexOf('\n', alphaAt) - alphaAt);
        string betaLine  = bridge.Substring(betaAt,  bridge.IndexOf('\n', betaAt)  - betaAt);
        alphaLine.Should().NotBe(betaLine, "the two states must not resolve to the same offset");
        alphaLine.Should().Contain("), 0)");   // Alpha is the first packed variable
        betaLine.Should().Contain("), 4)");    // Beta follows a 4-byte int
    }

    /// <summary>
    /// ⛔⛔ <b>AN ASSET WITH NO BOUND STATE EMITS NOTHING — the gating rule the <c>AssetId</c> constant
    /// learned the hard way.</b>
    ///
    /// <para>📌 Emitting the <c>AssetId</c> const unconditionally once moved <b>11</b> golden baselines
    /// for assets that could not use it. 🔒 <b>An emitter addition is gated on the feature that needs
    /// it</b>, so every asset authored before <c>E3b-0</c> stays byte-identical and keeps seeding from
    /// offset 0.</para>
    /// </summary>
    [Fact]
    public void AnAssetWithNoBoundState_EmitsNoBindingTable()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto(null, null));

        bridge.Should().NotContain("HsmParamBindings",
            "an unbound asset must emit byte-identical output to before E3b-0");
    }

    /// <summary>
    /// ⚠ <b>A target naming a variable that is not PACKED is skipped, not guessed.</b>
    ///
    /// <para>⛔ A <c>State</c>-role variable lives in the partition tier, not the inline param region,
    /// and a renamed-away target names nothing at all. ⭐ Emitting a fallback offset for either would be
    /// a silent wrong answer — the failure mode §3.4's <i>"fails closed"</i> rule exists to prevent.</para>
    /// </summary>
    [Fact]
    public void ATargetNamingAnUnpackedVariable_IsSkipped()
    {
        string bridge = HsmBridgeEmitCore.EmitBridge(MakeDto("Alpha", "NoSuchVariable"));

        bridge.Should().Contain(StateOne.ToString(), "the bound state still registers");
        bridge.Should().NotContain(StateTwo.ToString(), "an unresolvable target must not be guessed");
    }

    // ── CE-413 / CE-414: a transition's guard binds its OWN variable ────────────────────

    private static readonly Guid GuardAssetId = new("bb3b0000-0000-0000-0000-0000000060a0");

    /// <summary>Adds a transition <c>One -&gt; Two</c> with the given guard and target field.</summary>
    private static HsmAssetDto WithGuardedTransition(
        HsmAssetDto dto, Guid guardAssetId, string? guardMethod, string? targetField)
    {
        dto.Transitions.Add(new TransitionNodeDto
        {
            VisualId              = new Guid("bb3b0000-0000-0000-0000-0000000000c1"),
            SourceStableId        = StateOne,
            TargetStableId        = StateTwo,
            IsPolled              = true,
            // CE-417: one guard binding carries the method/blueprint AND its own field.
            Guard                 = guardAssetId == Guid.Empty && guardMethod == null ? null : new BehaviorActionBindingDto
            {
                BlueprintAssetId      = guardAssetId,
                BlueprintName         = guardAssetId == Guid.Empty ? null : "GuardBp",
                MethodFqn             = guardMethod,
                ExpressionTargetField = targetField,
            },
        });
        return dto;
    }

    /// <summary>
    /// 🔴🔴🔴 <b><c>CE-413</c> — A TRANSITION'S <c>ExpressionTargetField</c> IS NO LONGER INERT.</b>
    ///
    /// <para>⛔⛔ <b>It was carried on <c>TransitionNodeDto</c> since <c>E7b</c> and nothing read it for
    /// the HSM seed.</b> The kernel stamps a polled guard with its SOURCE STATE, so a guard could only
    /// ever get the source state's binding — and it read those bytes through its OWN <c>Params</c> type.
    /// 🔴 A type-pun with no validator behind it.</para>
    ///
    /// <para>⭐ The fix needs no kernel change: the guard's ASSET GUID is the site, and it is the same
    /// Guid the guard thunk already passes to <c>HsmOccurrence.KeyFor</c> for its slot.</para>
    /// </summary>
    [Fact]
    public void AGuardBlueprintsOwnTargetField_EmitsASitedBinding()
    {
        var dto = WithGuardedTransition(MakeDto("Alpha", null), GuardAssetId, null, "Beta");

        string bridge = HsmBridgeEmitCore.EmitBridge(dto);

        // ⭐⭐ THE RAIL. The guard registers under (SOURCE STATE, its own asset id) at BETA's offset —
        //    while the state itself keeps the state-wide entry at ALPHA's offset.
        bridge.Should().Contain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{GuardAssetId}\"), 4)",
            "the guard must seed from its OWN variable, not the source state's");
        bridge.Should().Contain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{Guid.Empty}\"), 0)",
            "the state's own field stays the state-wide default every other site falls back to");
    }

    /// <summary>
    /// ⚠ <b>A transition guarded by a C# METHOD emits NOTHING, and that is deliberate.</b>
    ///
    /// <para>⛔ A method guard has no asset id, so it would have to register under
    /// <c>Guid.Empty</c> — which is the SOURCE STATE's own entry. ⇒ it would silently overwrite the
    /// state's binding and change what every other site at that state seeds from. ⭐ Its params come
    /// from the source state's field, which is exactly the pre-<c>CE-413</c> behaviour.</para>
    /// </summary>
    [Fact]
    public void AMethodGuardsTargetField_EmitsNoSitedBinding()
    {
        var dto = WithGuardedTransition(MakeDto("Alpha", null), Guid.Empty, "Demo.Guards.IsOpen", "Beta");

        string bridge = HsmBridgeEmitCore.EmitBridge(dto);

        // ⭐⭐ THE RAIL. Beta's offset (4) never appears — only the state's own entry at 0.
        bridge.Should().Contain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{Guid.Empty}\"), 0)");
        bridge.Should().NotContain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{Guid.Empty}\"), 4)",
            "a method guard must never overwrite its source state's binding");
    }

    /// <summary>
    /// ⛔ <b>The gating rule survives <c>CE-413</c>:</b> an asset whose ONLY binding would come from a
    /// transition still emits a table — but an asset with no binding anywhere still emits none.
    /// </summary>
    [Fact]
    public void ATransitionOnlyBinding_StillEmitsTheTable()
    {
        var dto = WithGuardedTransition(MakeDto(null, null), GuardAssetId, null, "Beta");

        string bridge = HsmBridgeEmitCore.EmitBridge(dto);

        bridge.Should().Contain("HsmParamBindings.Register(blob",
            "a guard-only binding is still a binding");
        bridge.Should().Contain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{GuardAssetId}\"), 4)");
        bridge.Should().NotContain(
            $"new global::System.Guid(\"{Guid.Empty}\")",
            "no state bound its own field, so there is no state-wide entry to emit");
    }

    // ── CE-417 §6 "transition split": one transition, two bindings, two variables ────────────────

    private const string PushFqn = "Demo.Actions.Push";

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-417</c> rail "transition split" — a transition whose guard BLUEPRINT and C# ACTION are bound to
    /// DIFFERENT variables addresses each through its OWN binding.</b> 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §6, B-2 (a′).
    ///
    /// <para>🔴 <b>What v1 could not express.</b> A transition carried ONE target field, so a guard and an action that
    /// needed two variables shared one (slice-2 as-built box: <i>"round-trips through the editor as one field"</i>). ⭐ Now
    /// each binding carries its own: the guard's goes to the sited seed table at <c>(source state, guard asset)</c>, the
    /// action's into its own <c>Fqn@hostOffset</c> call — and neither may borrow the other's, nor the source state's.</para>
    ///
    /// <para>✅ <b>Red-proof</b>: give <c>SharedAiBindings.Collect</c> the guard's binding for the action (or name the action
    /// by the guard's field) ⇒ the <c>@4</c> assertions redden.</para>
    /// </summary>
    [Fact]
    public void TransitionSplit_GuardBlueprintAndCSharpAction_EachUseTheirOwnVariable()
    {
        // Guard blueprint → Beta (offset 4); C# action → Alpha (offset 0); the source state binds nothing.
        var dto = WithGuardedTransition(MakeDto(null, null), GuardAssetId, null, "Beta");
        dto.Transitions[0].Action = new BehaviorActionBindingDto { MethodFqn = PushFqn, ExpressionTargetField = "Alpha" };

        static SharedAiMethodInfo? SharedAi(string fqn)
            => fqn == PushFqn ? new SharedAiMethodInfo("global::System.Int32", "System.Int32", false, false) : null;

        string bridge   = HsmBridgeEmitCore.EmitBridge(dto, null, SharedAi);
        string topology = HsmEmitCore.EmitTopologyCore(dto, null, null, null, null, SharedAi);

        // ⭐ the guard seeds from BETA, sited at (source state, its own asset) — and no state-wide entry exists.
        bridge.Should().Contain(
            $"(new global::System.Guid(\"{StateOne}\"), new global::System.Guid(\"{GuardAssetId}\"), 4)",
            "the guard blueprint must seed from its own variable");
        bridge.Should().NotContain($"new global::System.Guid(\"{Guid.Empty}\")",
            "the source state binds nothing, so the action must not have become a state-wide seed");

        // ⭐ the action is called at ALPHA's offset, under the name the blob addresses.
        bridge.Should().Contain($"// {PushFqn}@0", "the action's call is registered under its own Fqn@hostOffset");
        bridge.Should().Contain("(__root + 0)", "the action's call projects its own variable");
        bridge.Should().NotContain($"{PushFqn}@4", "the action must never take the guard's variable");
        bridge.Should().NotContain("(__root + 4)", "only the action is a C# call; Beta belongs to the blueprint guard");

        // ⚠ The fixture has no root, so no transition chain is emitted; the registration uses the SAME namer (BindingNamer).
        topology.Should().Contain($"builder.RegisterAction(\"{PushFqn}@0\")", "the blob names the action by its own binding");
        topology.Should().NotContain($"{PushFqn}@4");
    }
}
