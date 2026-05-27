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
}
