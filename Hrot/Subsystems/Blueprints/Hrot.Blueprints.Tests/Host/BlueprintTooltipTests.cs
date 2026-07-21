using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Editor punch-list #4: function nodes and every data pin surface their data type on hover
/// ("data type mandatory"). These assert the model-supplied tooltip strings directly (game-free —
/// no reflection / no game assemblies, so they run in the standard headless suite). The XML-doc
/// <c>&lt;summary&gt;</c> enrichment is disk-artifact-driven and exercised live in the editor.
/// </summary>
public sealed class BlueprintTooltipTests
{
    private static readonly NodeId Owner = new(System.Guid.NewGuid());

    private static BlueprintPinModel DataPin(string name, string direction, string typeId, bool isArray = false)
        => new(new Pin
        {
            Id = System.Guid.NewGuid(),
            Name = name,
            Direction = direction,
            IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray },
        }, Owner);

    private static BlueprintPinModel ExecPin(string direction)
        => new(new Pin
        {
            Id = System.Guid.NewGuid(),
            Name = direction,
            Direction = direction,
            IsExec = true,
            TypeRef = new BlueprintTypeRef(),
        }, Owner);

    // ── TooltipText.ShortTypeName ───────────────────────────────────────────────

    [Theory]
    [InlineData("System.Numerics.Vector3", "Vector3")]
    [InlineData("System.Single", "Single")]
    [InlineData("global::Hrot.AI.Behaviors.Brains.WaveState", "WaveState")]
    [InlineData("Entity", "Entity")]
    public void ShortTypeName_StripsNamespaceAndGlobalSentinel(string typeId, string expected)
        => Assert.Equal(expected, TooltipText.ShortTypeName(typeId));

    // ── pin tooltips ────────────────────────────────────────────────────────────

    [Fact]
    public void DataPin_Tooltip_LeadsWithNameAndShortType()
    {
        var tip = DataPin("Destination", "In", "System.Numerics.Vector3").Tooltip;
        Assert.NotNull(tip);
        Assert.StartsWith("Destination : Vector3", tip);
        // namespaced type also surfaces the full FQN so struct/class returns are unambiguous.
        Assert.Contains("System.Numerics.Vector3", tip);
    }

    [Fact]
    public void DataPin_Tooltip_ArrayIsMarked()
        => Assert.Contains("[]", DataPin("Items", "In", "System.Int32", isArray: true).Tooltip!);

    [Fact]
    public void ExecPin_HasNoTooltip()
        => Assert.Null(ExecPin("In").Tooltip);

    // ── function-node tooltip ───────────────────────────────────────────────────

    [Fact]
    public void FunctionCall_Tooltip_BuildsSignatureFromPins()
    {
        var fc = new FunctionCallNode { MethodName = "TotalSlots", TargetTypeId = "Hrot.AI.Behaviors.Brains.SegmentMath", IsPure = true };
        var pins = new IPinModel[]
        {
            DataPin("width",  "In",  "System.Single"),
            DataPin("stride", "In",  "System.Single"),
            DataPin("Return", "Out", "System.Int32"),
        };
        var tip = FunctionCallTooltip.Build(fc, pins);
        Assert.NotNull(tip);
        Assert.Contains("Int32 TotalSlots(Single width, Single stride)", tip);
        // the CLR target FQN is surfaced for "which method is this?" clarity.
        Assert.Contains("CLR method — Hrot.AI.Behaviors.Brains.SegmentMath", tip);
    }

    [Fact]
    public void FunctionCall_Tooltip_GraphCall_ShowsBlueprintFunctionKind()
    {
        var fc = new FunctionCallNode { MethodName = "", TargetGraphId = System.Guid.NewGuid().ToString() };
        var tip = FunctionCallTooltip.Build(fc, System.Array.Empty<IPinModel>());
        Assert.NotNull(tip);
        Assert.Contains("Blueprint function", tip);
    }

    [Fact]
    public void NodeModel_HeaderGlyph_IsFunctionMark_OnlyForFunctionCall()
    {
        var fn = new BlueprintNodeModel(new FunctionCallNode { MethodName = "X", IsPure = true }, System.Array.Empty<IPinModel>());
        Assert.Equal("ƒ", fn.HeaderGlyph);

        var other = new BlueprintNodeModel(new CompareNode { Operator = ComparisonOperator.Equal }, System.Array.Empty<IPinModel>());
        Assert.Null(other.HeaderGlyph);
    }

    [Fact]
    public void NodeModel_StatusTooltip_OnlyForFunctionCall()
    {
        var plain = new BlueprintNodeModel(new CompareNode { Operator = ComparisonOperator.Equal }, System.Array.Empty<IPinModel>());
        Assert.Null(plain.StatusTooltip);

        var fnPins = new IPinModel[] { DataPin("Return", "Out", "System.Boolean") };
        var fn = new BlueprintNodeModel(new FunctionCallNode { MethodName = "IsArrived", IsPure = true }, fnPins);
        Assert.NotNull(fn.StatusTooltip);
        Assert.Contains("IsArrived", fn.StatusTooltip);
    }
}
