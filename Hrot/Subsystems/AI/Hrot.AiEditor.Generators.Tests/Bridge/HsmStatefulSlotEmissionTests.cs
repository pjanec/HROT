using System;
using FluentAssertions;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐⭐ <b><c>E1</c> — an HSM asset's authored <c>Role = State</c> variables reach the runtime.</b>
///
/// <para>
/// 🔴🔴 <b>Measured before the change: <c>HsmEmitCore</c> + <c>HsmBridgeEmitCore</c> contained ZERO
/// <c>Role</c>/<c>Scope</c> references</b>, while <c>BTreeBridgeEmitCore</c> contained 45 — and
/// <c>HsmBlackboardVariableDto</c> persists both faithfully. ⇒ ⛔ <b>a designer could author working
/// state on an HSM asset, save it, reload it, and have it exist nowhere at runtime.</b>
/// ⭐ User ruling: <i>"if something is not present in HSM, it is not because it is not needed, just
/// not implemented yet."</i>
/// </para>
///
/// <para>
/// ⭐⭐ <b><c>E2</c> is satisfied by the manifest existing, not by a second provisioner</b> —
/// <c>BehaviorIngressSystem:142-154</c> reads <c>def.StatefulWorkingSlots</c> and provisions
/// <b>without consulting <c>BrainTier</c></b>. ⛔ Emitting the manifest without provisioning would
/// have been dead data, which is why the two ship together.
/// </para>
/// </summary>
public sealed class HsmStatefulSlotEmissionTests
{
    private static readonly Guid AssetId = new("11111111-2222-3333-4444-555555555555");

    private static HsmAssetDto MakeHsmDto(params (string Name, BlackboardVariableRole Role, WorkingStateScope Scope)[] vars)
    {
        var dto = new HsmAssetDto { AssetId = AssetId, Name = "StatefulHsm" };
        dto.Blackboard.Managed = true;
        foreach (var (name, role, scope) in vars)
        {
            dto.Blackboard.Variables.Add(new HsmBlackboardVariableDto
            {
                Name  = name,
                Type  = new HsmBlackboardTypeRefDto { TypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState" },
                Role  = role,
                Scope = scope,
            });
        }
        return dto;
    }

    /// <summary>
    /// 🔴 <b>RED before <c>E1</c>:</b> the emitted registrar carried no manifest at all, so an authored
    /// state variable was provisioned nowhere.
    /// </summary>
    [Fact]
    public void AnAuthoredStateVariable_EmitsASlotManifestEntry()
    {
        var bridge = HsmBridgeEmitCore.EmitBridge(
            MakeHsmDto(("Cursor", BlackboardVariableRole.State, WorkingStateScope.Behavior)));

        bridge.Should().Contain("StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]",
            "an HSM asset with an authored Role=State variable must emit the manifest the shared "
            + "ingress provisions from");
        bridge.Should().Contain("\"Cursor\"");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The rail that matters: the key is BTREE'S ALGORITHM for the same inputs.</b>
    ///
    /// <para>
    /// ⛔ A second key algorithm is the one thing that fails this item — two tiers would hash the same
    /// variable to two slots and the shared allocator would hand out two regions for one concept. ⚠ The
    /// expected value is <b>computed by calling <c>ComputeStatefulSlotKey</c></b>, not pasted as a
    /// literal: a literal would still pass if BOTH sides changed together, which is exactly the drift
    /// this guards.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(WorkingStateScope.Behavior)]
    [InlineData(WorkingStateScope.Entity)]
    public void TheSlotKeyMatchesTheBTreeAlgorithmForTheSameInputs(WorkingStateScope scope)
    {
        int expected = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, scope, Guid.Empty, "Cursor");

        var bridge = HsmBridgeEmitCore.EmitBridge(
            MakeHsmDto(("Cursor", BlackboardVariableRole.State, scope)));

        bridge.Should().Contain($"StatefulSlotInfo({expected},",
            "the HSM emitter must CALL the BTree key algorithm, not reimplement it");
    }

    /// <summary>⭐ N state variables ⇒ N distinct slots. ⚠ Distinctness matters: a shared key would
    /// silently alias two variables onto one region.</summary>
    [Fact]
    public void NStateVariables_ProduceNDistinctSlots()
    {
        var bridge = HsmBridgeEmitCore.EmitBridge(MakeHsmDto(
            ("Alpha", BlackboardVariableRole.State, WorkingStateScope.Behavior),
            ("Bravo", BlackboardVariableRole.State, WorkingStateScope.Behavior)));

        int keyA = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, WorkingStateScope.Behavior, Guid.Empty, "Alpha");
        int keyB = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, WorkingStateScope.Behavior, Guid.Empty, "Bravo");

        keyA.Should().NotBe(keyB);
        bridge.Should().Contain($"StatefulSlotInfo({keyA},");
        bridge.Should().Contain($"StatefulSlotInfo({keyB},");
    }

    /// <summary>
    /// ⛔ <b><c>Role = Input</c> is NOT working state</b> and must not take a slot — inputs live in the
    /// params region (<c>DESIGN_Parameter_Model.md</c> §1: <i>"there is NO 'Param' role; Input IS the
    /// parameter role"</i>).
    /// </summary>
    [Fact]
    public void AnInputVariable_ProducesNoSlot()
    {
        var bridge = HsmBridgeEmitCore.EmitBridge(
            MakeHsmDto(("Speed", BlackboardVariableRole.Input, WorkingStateScope.Node)));

        bridge.Should().NotContain("StatefulWorkingSlots");
    }

    /// <summary>
    /// ⚠ <b><c>Node</c> scope is skipped deliberately</b>, mirroring the BTree standalone pass: the
    /// <c>Node</c> key collapses to <c>FNV(assetId ++ nodeVisualId)</c> and ignores the variable name,
    /// so a variable with no node to key off has no meaningful <c>Node</c>-scoped slot. ⭐ Asserted so
    /// the omission reads as a decision rather than a gap.
    /// </summary>
    [Fact]
    public void ANodeScopedStateVariable_IsSkipped_WithNoNodeToKeyOff()
    {
        var bridge = HsmBridgeEmitCore.EmitBridge(
            MakeHsmDto(("Scratch", BlackboardVariableRole.State, WorkingStateScope.Node)));

        bridge.Should().NotContain("StatefulWorkingSlots");
    }

    /// <summary>⭐ An asset with no blackboard variables emits byte-identically to before — the
    /// existing HSM corpus must not move.</summary>
    [Fact]
    public void AnAssetWithNoVariables_EmitsNoManifest()
    {
        HsmBridgeEmitCore.EmitBridge(MakeHsmDto()).Should().NotContain("StatefulWorkingSlots");
    }
}
