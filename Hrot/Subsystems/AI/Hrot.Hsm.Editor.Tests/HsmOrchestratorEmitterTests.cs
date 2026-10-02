using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Emit;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmOrchestratorEmitterTests
{
    // ---- Helpers ----

    private struct ShootBtDto  { public float Aim   { get; set; } }
    private struct PatrolBtDto { public float Range { get; set; } }

    private static HsmAsset BuildAndProject(string name = "TestMachine")
    {
        var builder = new HsmBuilder(name);
        builder.State("Idle");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(
            blob, metadata, null, Guid.NewGuid(), name, "", false, "Hrot.AI.Machines");
    }

    private static BlackboardAliasBinding Binding(
        string requiringAssetName,
        Type dtoType,
        Guid? assetId = null,
        Guid? elementId = null) =>
        new BlackboardAliasBinding(
            assetId ?? Guid.NewGuid(),
            elementId ?? Guid.NewGuid(),
            requiringAssetName,
            $"/{requiringAssetName}.cs",
            dtoType);

    // ---- Tests ----

    [Fact]
    public void Emit_ReturnsNull_WhenNoAliases()
    {
        var asset = BuildAndProject("GuardAI");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));

        string? result = HsmOrchestratorEmitter.Emit(asset);

        result.Should().BeNull();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>RE-HOMED <c>2026-09-23</c> (<c>CE-333</c> / <c>E5</c>) — the claim INVERTED, and the
    /// inversion is the point.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.11.2.
    ///
    /// <para>🔴 <b>What this used to assert:</b> that an alias produced
    /// <c>[HsmAction(Name = "Orchestrate_Shoot_BT")] … Orchestrate_Shoot_BT_Tick(ref master.SharedFire …)</c>.
    /// 📐 Measured <c>2026-09-23</c>: that emission <b>could never compile</b> — it carried the
    /// FastBTree ACTION signature under an <c>[HsmAction]</c> attribute, whose ABI is a thunk
    /// <c>(void*, void*, HsmCommandWriter*)</c>. ⛔ <b>This rail asserted its TEXT, which is exactly
    /// why nobody found out</b> — a text-asserting golden cannot tell you the code it pins is not
    /// valid C#.</para>
    ///
    /// <para>⛔⛔ <b>And making it ABI-correct would not have made it WORK:</b> an <c>[HsmAction]</c> is
    /// dispatched at most once per event-driven round (<c>CE-334</c>), and an HSM thunk has no
    /// <c>deltaTime</c> — a hosted BTree would have ticked once, at dt 0.</para>
    ///
    /// <para>⭐⭐ <b>So the arm was ROUTED, not patched.</b> HSM sub-tree hosting is declared per STATE
    /// (<c>StateNode.SubtreeName</c> + <c>SubtreeAssetId</c>) and ticked every frame by
    /// <c>BrainTickSystem.TickHostedChildren</c>. ⇒ this rail now pins the SUPERSESSION, so a future
    /// author who re-adds an alias-driven emitter fails here and reads why.</para>
    /// </summary>
    [Fact]
    public void Emit_ReturnsNull_EvenForAnAlias_BecauseHsmHostingIsPerState_CE333()
    {
        var asset = BuildAndProject("GuardAI");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        string? result = HsmOrchestratorEmitter.Emit(asset);

        result.Should().BeNull(
            "an HSM alias no longer emits a hosting orchestrator — the [HsmAction] shape it used to "
            + "emit could not compile (CE-333) and could not have ticked a child every frame "
            + "(CE-334). Hosting moved to StateNode.SubtreeName + BrainTickSystem (E5, DESIGN §32).");
    }

    [Fact]
    public void Emit_OutputIsDeterministic()
    {
        var asset = BuildAndProject("GuardAI");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        var bindingId = new Guid("c1c2c3c4-0001-0000-0000-000000000001");
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto), bindingId, bindingId));

        string first  = HsmOrchestratorEmitter.Emit(asset)!;
        string second = HsmOrchestratorEmitter.Emit(asset)!;

        first.Should().Be(second, "emitter output must be deterministic for the same input");
    }

    [Fact]
    public void BlackboardTypeName_DefaultsToAssetNamePlusBlackboard()
    {
        var asset = BuildAndProject("GuardPatrol_HSM");

        asset.BlackboardTypeName.Should().Be(
            "GuardPatrol_HSM_Blackboard",
            "default BlackboardTypeName is SanitizeIdentifier(name) + '_Blackboard'");
    }

    // ⛔ REMOVED 2026-09-23 (CE-333): Emit_StartsWithEditorGeneratedMarker.
    //
    // ⭐ The claim — "an emitted orchestrator carries the editor-generated marker" — is NOT lost: it is
    //   RE-HOMED to the arm that still emits one, BTreeOrchestratorEmitterTests.Emit_StartsWithEditor-
    //   GeneratedMarker:128. ⛔ Keeping a copy here would have asserted the marker on a string the HSM
    //   arm never produces again, which is a vacuous rail, not coverage.
    // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.11.2; the re-home rule is §31.19.4.
}
