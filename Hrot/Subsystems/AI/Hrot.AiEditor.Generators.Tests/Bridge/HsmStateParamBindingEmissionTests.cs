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
            OnEntryAction = "Demo.Actions.Work", ExpressionTargetField = bindOne,
        });
        dto.States.Add(new StateNodeDto
        {
            StableId = StateTwo, Name = "Two",
            OnEntryAction = "Demo.Actions.Work", ExpressionTargetField = bindTwo,
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
}
