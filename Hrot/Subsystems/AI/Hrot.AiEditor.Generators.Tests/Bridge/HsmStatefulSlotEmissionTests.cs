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
    /// 🔴 <b>RED before <c>E1</c>:</b> the emitted registrar carried no manifest at all, so an authored state variable was
    /// provisioned nowhere. ⭐⭐ <b><c>CE-416</c> (<c>2026-10-01</c>) — its home moved, the guarantee did not:</b> it now lives in
    /// the behaviour's BLOCK (<c>{Asset}_Block.St</c>, the BTree's <c>CE-437</c> home), which the registrar names as its layout
    /// so the shared ingress allocates and bakes it. ⛔ SUPERSEDED: "emits a StatefulWorkingSlots entry".
    /// </summary>
    [Fact]
    public void AnAuthoredStateVariable_ReachesTheRuntime_InTheBlock()
    {
        var dto    = MakeHsmDto(("Cursor", BlackboardVariableRole.State, WorkingStateScope.Behavior));
        var bridge = HsmBridgeEmitCore.EmitBridge(dto);
        var structs = BTreeEmitCore.EmitBlackboardStructSource(HsmBridgeEmitCore.BlackboardOwner(dto), out _);

        bridge.Should().Contain("BlackboardLayoutType = typeof(global::Hrot.AI.Behaviors.Machines.StatefulHsm_Block)",
            "the block is the layout the shared ingress sizes the root slot from");
        bridge.Should().Contain("ParseParams  = __parseParams,", "a State-only block must still be allocated (CE-429)");
        structs.Should().Contain("public struct StatefulHsm_BlockState").And.Contain(" Cursor;");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>One home (<c>CE-437</c>'s rail, HSM half):</b> a variable in <c>St</c> has NO side slot. ⚠ The key is still
    /// computed by the BTree's algorithm — that is how <c>TryGetBlockStateVariable</c> recognises it — so a second algorithm
    /// would show up here as a slot that should not exist.
    /// </summary>
    [Fact]
    public void ABlockStateVariable_HasNoSideSlot()
    {
        int key = BTreeBridgeEmitCore.ComputeStatefulSlotKey(AssetId, WorkingStateScope.Behavior, Guid.Empty, "Cursor");

        var bridge = HsmBridgeEmitCore.EmitBridge(
            MakeHsmDto(("Cursor", BlackboardVariableRole.State, WorkingStateScope.Behavior)));

        bridge.Should().NotContain($"StatefulSlotInfo({key},");
    }

    /// <summary>⭐ N state variables ⇒ N distinct <c>St</c> fields. ⚠ Distinctness matters: a shared field would silently
    /// alias two variables.</summary>
    [Fact]
    public void NStateVariables_ProduceNDistinctBlockFields()
    {
        var dto = MakeHsmDto(
            ("Alpha", BlackboardVariableRole.State, WorkingStateScope.Behavior),
            ("Bravo", BlackboardVariableRole.State, WorkingStateScope.Behavior));

        var structs = BTreeEmitCore.EmitBlackboardStructSource(HsmBridgeEmitCore.BlackboardOwner(dto), out _);

        structs.Should().Contain(" Alpha;").And.Contain(" Bravo;");
        HsmBridgeEmitCore.EmitBridge(dto).Should().NotContain("StatefulSlotInfo(");
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
