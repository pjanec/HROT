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

    /// <summary>
    /// ⭐⭐⭐ <b><c>W7b</c> (§9.4) round-trips through the LAYOUT-METHOD channel.</b>
    ///
    /// <para>
    /// ⚠ <b>There are TWO persistence channels and both had to be wired.</b> The DTO/JSON mapper is
    /// one; this emitted <c>[BTreeLayout]</c> method is the other. ⛔ Wiring only the mapper would lose
    /// the designer's checkbox on every emit/project round trip — silently, since nothing would throw.
    /// </para>
    /// </summary>
    [Fact]
    public void Emit_Outputs_ConcurrentWritesAllowed()
    {
        var original = MakeAsset("TestSubtree");
        original.SetConcurrentWritesAllowed("speed", true);

        var code = new BTreeFluentEmitter().Emit(original);

        code.Should().Contain(".AllowConcurrentWrites(\"speed\")");
    }

    /// <summary>⭐ And comes back. ⛔ Asserted alongside the per-PAIR suppression so the two stay
    /// visibly separate: allowing a variable must NOT suppress a pair, and vice versa.</summary>
    [Fact]
    public void Projector_Loads_ConcurrentWritesAllowed_WithoutTouchingPairSuppression()
    {
        var layout = new BTreeEditorLayoutBuilder()
            .AllowConcurrentWrites("speed")
            .Build();

        var asset = BehaviorTreeAssetProjector.Project(
            EmptyBlob(), null, layout, Guid.NewGuid(), "Tree1", "p.cs", true, "BB", "Ctx");

        asset.IsConcurrentWritesAllowed("speed").Should().BeTrue();
        asset.IsConcurrentWritesAllowed("ammo").Should().BeFalse();
        // ⛔ Two mechanisms: allowing the variable did not create a pair suppression.
        asset.IsConflictSuppressed("speed", "anything").Should().BeFalse();
    }

    /// <summary>⚠ An asset with the flag unset emits NOTHING new — every existing asset stays
    /// byte-identical.</summary>
    [Fact]
    public void AnAssetWithoutTheFlag_EmitsNoAllowConcurrentWritesCall()
        => new BTreeFluentEmitter().Emit(MakeAsset("Plain"))
            .Should().NotContain("AllowConcurrentWrites");
}
