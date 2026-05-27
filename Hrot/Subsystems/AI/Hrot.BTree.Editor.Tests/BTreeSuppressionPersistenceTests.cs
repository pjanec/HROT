using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Layout;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeSuppressionPersistenceTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset(string name = "MasterAI") =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), name, $"/trees/{name}.cs", true,
            "Hrot.Game.MasterBlackboard", "Hrot.Game.MasterContext",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

    [Fact]
    public void Emit_Outputs_Suppressions()
    {
        var original = MakeAsset("TestSubtree");
        original.SetConflictSuppressed("v1", "w1_w2", true);
        original.SetUnusedWarningSuppressed("v3", true);

        var emitter = new BTreeFluentEmitter();
        var code = emitter.Emit(original);

        code.Should().Contain(".SuppressBlackboardConflict(\"v1\", \"w1_w2\")");
        code.Should().Contain(".SuppressUnusedWarning(\"v3\")");
    }

    [Fact]
    public void Projector_Loads_Suppressions()
    {
        var layout = new BTreeEditorLayoutBuilder()
            .SuppressBlackboardConflict("vB", "k1_k2")
            .SuppressUnusedWarning("vA")
            .Build();

        var asset = BehaviorTreeAssetProjector.Project(
            EmptyBlob(),
            null,
            layout,
            Guid.NewGuid(),
            "Tree1",
            "p.cs",
            true,
            "BB",
            "Ctx");

        asset.IsConflictSuppressed("vB", "k1_k2").Should().BeTrue();
        asset.IsUnusedWarningSuppressed("vA").Should().BeTrue();
        asset.IsConflictSuppressed("vA", "k").Should().BeFalse();
    }
}
