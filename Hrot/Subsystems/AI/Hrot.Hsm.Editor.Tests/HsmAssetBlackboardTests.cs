using System;
using System.Collections.Generic;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

// Tests for IBlackboardManagedAsset.RemoveVariables on the real HsmAsset implementation.
public sealed class HsmAssetBlackboardTests
{
    private static HsmAsset MakeAsset(string name = "TestMachine")
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

    [Fact]
    public void RemoveVariables_RemovesNamedVars_OnHsmAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("a", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("b", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("c", typeof(bool), null));

        asset.RemoveVariables(new[] { "a", "c" });

        asset.BlackboardVariables.Should().HaveCount(1);
        asset.BlackboardVariables[0].Name.Should().Be("b");
    }

    [Fact]
    public void RemoveVariables_FiresChangedOnce_OnHsmAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("y", typeof(float), null));
        int count = 0;
        asset.Changed += () => count++;

        asset.RemoveVariables(new[] { "x", "y" });

        count.Should().Be(1);
    }

    [Fact]
    public void RemoveVariables_EmptyList_DoesNotFireChanged_OnHsmAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        int count = 0;
        asset.Changed += () => count++;

        asset.RemoveVariables(Array.Empty<string>());

        count.Should().Be(0);
        asset.BlackboardVariables.Should().HaveCount(1);
    }

    [Fact]
    public void BlackboardTypeName_DefaultsToSanitizedNamePlusBlackboard()
    {
        var asset = MakeAsset("GuardPatrol_HSM");

        asset.BlackboardTypeName.Should().Be("GuardPatrol_HSM_Blackboard");
    }

    public struct HsmParamsA { public float Radius; }
    public struct HsmParamsB { public int Rounds; }

    /// <summary>⭐ <c>CE-1061</c> U5a/U5b + <c>CE-1062</c> — the HSM's one params write (<c>SetParams</c>) and its undo through the HSM sink's
    /// <c>AssetEditCommand</c> arm: the DTO choice clears the overrides and rebuilds the reserved entry; undo restores the exact pair.</summary>
    [Fact]
    public void CE1062_TheHsmParamsWrite_IsUndoableThroughItsSink()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetParams(typeof(HsmParamsA).FullName, "{\"Radius\":3}");
        var undo = new NodeEditor.Core.Commands.UndoStack(new Hrot.Hsm.Editor.Host.HsmCommandSink(asset));

        Hrot.Editor.AiShared.Documents.AssetEdits.Run(undo, "Params DTO: HsmParamsB",
            () => asset.SetParams(typeof(HsmParamsB).FullName, null),
            () => asset.SetParams(typeof(HsmParamsA).FullName, "{\"Radius\":3}")).Should().BeTrue();
        asset.ParamsDtoTypeId.Should().Be(typeof(HsmParamsB).FullName);
        asset.CurrentParamsDefaults.Should().BeNull();
        ReservedParamsVariable.Find(asset.BlackboardVariables)!.FieldType.Should().Be(typeof(HsmParamsB));

        undo.Undo().Should().BeTrue();
        (asset.ParamsDtoTypeId, asset.CurrentParamsDefaults).Should().Be((typeof(HsmParamsA).FullName, "{\"Radius\":3}"));
        ReservedParamsVariable.Find(asset.BlackboardVariables)!.FieldType.Should().Be(typeof(HsmParamsA));
    }
}
