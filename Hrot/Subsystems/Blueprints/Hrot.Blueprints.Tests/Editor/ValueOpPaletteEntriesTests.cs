using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-04 — <c>Compare</c> / <c>BinaryOp</c> / <c>BooleanOp</c> / <c>Not</c> are fully lowered and
/// compile-tested, but had **no palette entry at all**, so they could not be placed in the editor —
/// only hand-authored into JSON. These tests pin the 14 baked entries that fix that.
///
/// <para>
/// Baking the operator into <c>CreateInstance</c> is safe because
/// <c>BlueprintCommandSink.CreateAssetNode</c> builds the node from the descriptor factory and only
/// *overlays* caller-supplied props afterwards — it does not round-trip through
/// <c>ApplyInitialProperties</c>, whose whitelist covers just 8 of ~50 node kinds and would
/// otherwise silently drop the operator.
/// </para>
/// </summary>
public sealed class ValueOpPaletteEntriesTests
{
    private static NodeKindDescriptor? Entry(string kind) =>
        BlueprintNodePaletteEntries.All().FirstOrDefault(d => d.Kind == kind);

    // ---- every operator value is reachable -------------------------------

    [Theory]
    [InlineData("Compare.Equal",              ComparisonOperator.Equal)]
    [InlineData("Compare.NotEqual",           ComparisonOperator.NotEqual)]
    [InlineData("Compare.LessThan",           ComparisonOperator.LessThan)]
    [InlineData("Compare.LessThanOrEqual",    ComparisonOperator.LessThanOrEqual)]
    [InlineData("Compare.GreaterThan",        ComparisonOperator.GreaterThan)]
    [InlineData("Compare.GreaterThanOrEqual", ComparisonOperator.GreaterThanOrEqual)]
    public void CompareEntries_BakeTheOperator(string kind, ComparisonOperator expected)
    {
        var entry = Entry(kind);
        Assert.NotNull(entry);
        var node = Assert.IsType<CompareNode>(entry!.CreateInstance());
        Assert.Equal(expected, node.Operator);
    }

    [Theory]
    [InlineData("BinaryOp.Add",      ArithmeticOperator.Add)]
    [InlineData("BinaryOp.Subtract", ArithmeticOperator.Subtract)]
    [InlineData("BinaryOp.Multiply", ArithmeticOperator.Multiply)]
    [InlineData("BinaryOp.Divide",   ArithmeticOperator.Divide)]
    [InlineData("BinaryOp.Modulo",   ArithmeticOperator.Modulo)]
    public void BinaryOpEntries_BakeTheOperator(string kind, ArithmeticOperator expected)
    {
        var entry = Entry(kind);
        Assert.NotNull(entry);
        var node = Assert.IsType<BinaryOpNode>(entry!.CreateInstance());
        Assert.Equal(expected, node.Operator);
    }

    [Theory]
    [InlineData("BooleanOp.And", BooleanOperator.And)]
    [InlineData("BooleanOp.Or",  BooleanOperator.Or)]
    public void BooleanOpEntries_BakeTheOperator(string kind, BooleanOperator expected)
    {
        var entry = Entry(kind);
        Assert.NotNull(entry);
        var node = Assert.IsType<BooleanOpNode>(entry!.CreateInstance());
        Assert.Equal(expected, node.Operator);
    }

    [Fact]
    public void NotEntry_Exists()
    {
        var entry = Entry("Not");
        Assert.NotNull(entry);
        Assert.IsType<NotNode>(entry!.CreateInstance());
    }

    /// <summary>
    /// Guards the round-out: if an operator is added to any of these enums, the palette must gain a
    /// row for it, otherwise the new value silently becomes unreachable from the editor — the exact
    /// shape of the original BP-04 defect.
    /// </summary>
    [Fact]
    public void EveryOperatorEnumValue_HasAPaletteEntry()
    {
        var kinds = BlueprintNodePaletteEntries.All().Select(d => d.Kind).ToHashSet(StringComparer.Ordinal);

        foreach (var op in Enum.GetValues<ComparisonOperator>())
            Assert.Contains($"Compare.{op}", kinds);
        foreach (var op in Enum.GetValues<ArithmeticOperator>())
            Assert.Contains($"BinaryOp.{op}", kinds);
        foreach (var op in Enum.GetValues<BooleanOperator>())
            Assert.Contains($"BooleanOp.{op}", kinds);
    }

    [Fact]
    public void ValueOpEntries_HaveUniqueKinds()
    {
        var all = BlueprintNodePaletteEntries.All().Select(d => d.Kind).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- retired entries stay retired ------------------------------------

    /// <summary>
    /// BP-09 — six node kinds had inviting palette descriptions but no Stage5 lowering, so they
    /// compiled to a silent no-op. BP-16 additionally made ArrayMake/ArrayGet a hard BP1420 error,
    /// so offering them would let a designer place a node that guarantees a broken build.
    /// </summary>
    [Theory]
    [InlineData("CallDispatcher")]
    [InlineData("BindDispatcher")]
    [InlineData("PartitionElements")]
    [InlineData("AssignRoles")]
    [InlineData("AdvancePhase")]
    [InlineData("AcquireSlot")]
    [InlineData("ArrayMake")]
    [InlineData("ArrayGet")]
    public void UnloweredKinds_AreNotOfferedInThePalette(string kind)
    {
        Assert.Null(Entry(kind));
    }
}
