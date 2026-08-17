using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Model;

/// <summary>
/// ⭐⭐⭐ <b><c>E7b</c> — <c>ExpressionTargetField</c> is an OUTPUT binding, and the usage count was
/// blind to it.</b>
///
/// <para>
/// 🔴 <b>What was wrong.</b> <c>HsmAsset.CountNodesReferencingVariable</c> returned a hardcoded
/// <c>0</c>, commented <i>"HSM does not use ExpressionTargetField in this phase"</i> — false when it
/// was written. The field is authored, persisted (<c>HsmAssetMapper</c>), maintained
/// (<c>HsmCommandSink</c>) and already read as a writer style by <c>HsmValidator</c>.
/// </para>
///
/// <para>
/// ⛔ <b>The consequence is trap #5:</b> <c>BlueprintLocalVariableSchemaSource</c> computes
/// <c>IsUnused: Count… == 0</c>, so a variable <b>written</b> through <c>ExpressionTargetField</c>
/// read as <b>UNUSED</b> and was offered for deletion. A member reporting success while doing
/// nothing.
/// </para>
///
/// <para>
/// ⚠⚠ <b>The runtime half is NOT built, and the reason is not the one the handoff guessed.</b> It is
/// not blocked on <c>E3</c>'s occurrence key: 📐 <c>ExpressionTargetField</c> is <b>emitted nowhere</b>
/// — zero occurrences in <c>HsmEmitCore</c> and <c>HsmBridgeEmitCore</c> — so it never reaches the
/// blob and there is no write to assert bytes against. ⭐ <see cref="TheRuntimeHalfDoesNotExistYet"/>
/// pins that, so the gap is a measurement rather than an omission.
/// </para>
/// </summary>
public sealed class ExpressionTargetFieldCountTests
{
    private static (HsmAsset Asset, TransitionNode T, GlobalTransitionNode G) MakeAsset()
    {
        var root = new StateNode("__root__");
        var a    = new StateNode("A") { IsInitial = true, Parent = root };
        var b    = new StateNode("B") { Parent = root };
        root.Children.Add(a);
        root.Children.Add(b);

        var t = new TransitionNode { VisualId = Guid.NewGuid(), Source = a, Target = b };
        var g = new GlobalTransitionNode { VisualId = Guid.NewGuid(), Target = b };

        var asset = new HsmAsset(
            Guid.NewGuid(), "CountFixture", "", false, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { a, b },
            new List<TransitionNode> { t },
            new List<GlobalTransitionNode> { g },
            new List<RegionNode>(),
            new List<EventDefinition>());

        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("Result", typeof(int), null, false, null,
                BlackboardVariableRole.State, WorkingStateScope.Behavior),
        });

        return (asset, t, g);
    }

    /// <summary>⛔ Nothing bound ⇒ zero. The shipped answer, kept true for the case it was true for.</summary>
    [Fact]
    public void AnUnboundVariable_CountsZero()
        => Assert.Equal(0, MakeAsset().Asset.CountNodesReferencingVariable("Result"));

    /// <summary>
    /// 🔴 <b>The rail the plan pre-wrote:</b> <i>"<c>CountNodesReferencingVariable</c> is non-zero for
    /// a field bound through <c>ExpressionTargetField</c>."</i>
    /// </summary>
    [Fact]
    public void ATransitionBoundThroughExpressionTargetField_IsCounted()
    {
        var (asset, t, _) = MakeAsset();
        t.ExpressionTargetField = "Result";

        Assert.Equal(1, asset.CountNodesReferencingVariable("Result"));
    }

    /// <summary>
    /// ⭐⭐ <b>A GLOBAL transition counts too.</b> It is excluded from the validator's cross-region
    /// conflict rule because it belongs to no region — ⛔ but it is still a writer, and excluding it
    /// here would resurrect the same wrong answer for a narrower case.
    /// </summary>
    [Fact]
    public void AGlobalTransitionBoundThroughExpressionTargetField_IsCounted()
    {
        var (asset, _, g) = MakeAsset();
        g.ExpressionTargetField = "Result";

        Assert.Equal(1, asset.CountNodesReferencingVariable("Result"));
    }

    /// <summary>⭐ Both kinds together sum, rather than one shadowing the other.</summary>
    [Fact]
    public void BothTransitionKinds_Sum()
    {
        var (asset, t, g) = MakeAsset();
        t.ExpressionTargetField = "Result";
        g.ExpressionTargetField = "Result";

        Assert.Equal(2, asset.CountNodesReferencingVariable("Result"));
    }

    /// <summary>⚠ Case-insensitive, matching what the validator's conflict rule already does.</summary>
    [Fact]
    public void TheComparisonIsCaseInsensitive_LikeTheConflictRule()
    {
        var (asset, t, _) = MakeAsset();
        t.ExpressionTargetField = "rESULT";

        Assert.Equal(1, asset.CountNodesReferencingVariable("Result"));
    }

    /// <summary>⛔ A binding to a DIFFERENT variable is not counted — the obvious negative control.</summary>
    [Fact]
    public void ABindingToAnotherVariable_IsNotCounted()
    {
        var (asset, t, _) = MakeAsset();
        t.ExpressionTargetField = "SomethingElse";

        Assert.Equal(0, asset.CountNodesReferencingVariable("Result"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The count and the conflict rule share ONE predicate.</b> Both ask <i>"is this
    /// <c>ExpressionTargetField</c> bound to that variable"</i>, and two spellings of one predicate is
    /// how they drift apart — <c>HsmValidator.IsLocallyBoundTo</c> now delegates to
    /// <see cref="HsmAsset.IsExpressionTargetOf"/>.
    /// </summary>
    [Theory]
    [InlineData("Result",  "Result", true)]
    [InlineData("rESULT",  "Result", true)]
    [InlineData("Other",   "Result", false)]
    [InlineData("",        "Result", false)]
    [InlineData(null,      "Result", false)]
    public void ThePredicateIsShared(string? bound, string variable, bool expected)
        => Assert.Equal(expected, HsmAsset.IsExpressionTargetOf(bound, variable));

    /// <summary>
    /// ⚠⚠ <b>The runtime half does not exist, and this says so as a measurement.</b>
    /// <c>ExpressionTargetField</c> appears <b>zero times</b> in either HSM emit core, so it never
    /// reaches the blob: there is no write whose bytes could be asserted. ⛔ Not blocked on <c>E3</c>
    /// — blocked on the field being emitted at all.
    ///
    /// <para>
    /// ⭐ <b>Invert this when the emitter carries it</b>, do not delete it — Batch 70's rule about
    /// tests that assert an absence.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRuntimeHalfDoesNotExistYet()
    {
        foreach (var relative in new[]
                 {
                     System.IO.Path.Combine("Hrot", "Subsystems", "AI", "Hrot.AiEditor.Persistence",
                                            "Emit", "HsmEmitCore.cs"),
                     System.IO.Path.Combine("Hrot", "Subsystems", "AI", "Hrot.AiEditor.Persistence",
                                            "Emit", "HsmBridgeEmitCore.cs"),
                 })
        {
            var source = System.IO.File.ReadAllText(FindUp(relative));
            Assert.DoesNotContain("ExpressionTargetField", source);
        }
    }

    private static string FindUp(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, relative);
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.FileNotFoundException($"Not found on any ancestor: {relative}");
    }
}
