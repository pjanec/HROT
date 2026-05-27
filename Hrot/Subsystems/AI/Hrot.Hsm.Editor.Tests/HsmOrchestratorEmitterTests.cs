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

    [Fact]
    public void Emit_ContainsOrchestratorMethod_ForAlias()
    {
        var asset = BuildAndProject("GuardAI");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        string result = HsmOrchestratorEmitter.Emit(asset)!;

        result.Should().NotBeNull();
        // HSM uses [HsmAction], not [BTreeAction].
        result.Should().Contain("[HsmAction(Name = \"Orchestrate_Shoot_BT\")]");
        result.Should().Contain("Orchestrate_Shoot_BT_Tick");
        result.Should().Contain("ref master.SharedFire");
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

    [Fact]
    public void Emit_StartsWithEditorGeneratedMarker()
    {
        var asset = BuildAndProject("GuardAI");
        asset.AddVariable(new BlackboardVariableEntry("SharedFire", typeof(ShootBtDto), null));
        asset.AddAlias("SharedFire", Binding("Shoot_BT", typeof(ShootBtDto)));

        string result = HsmOrchestratorEmitter.Emit(asset)!;

        result.Should().StartWith(FluentCSharpEmitterBase.EditorGeneratedMarker);
    }
}
