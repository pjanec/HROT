using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Editor punch-list #1/#5/#8: a node's canvas body must surface its own data (literal value,
/// parameter name, compare/arith/bool operator) instead of the generic "Value" pin label. The title
/// is <see cref="BlueprintNodeModel"/>'s single body string, so these assert it directly.
/// </summary>
public sealed class BlueprintNodeTitleTests
{
    private static string Title(Node node, BlueprintAsset? asset = null)
        => new BlueprintNodeModel(node, System.Array.Empty<IPinModel>(), asset).Title;

    [Theory]
    [InlineData("System.Int32", "5", "5")]
    [InlineData("System.Boolean", "true", "true")]
    [InlineData("System.Single", "1.5f", "1.5")]
    [InlineData("System.String", "\"hello\"", "hello")]
    public void Literal_ShowsValue(string typeId, string valueJson, string expected)
        => Assert.Equal(expected, Title(new LiteralNode { TypeId = typeId, ValueJson = valueJson }));

    [Fact]
    public void Literal_Empty_FallsBackToType()
        => Assert.Equal("Literal (Single)", Title(new LiteralNode { TypeId = "System.Single", ValueJson = "" }));

    [Theory]
    [InlineData(ComparisonOperator.Equal, "Compare ==")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, "Compare >=")]
    [InlineData(ComparisonOperator.LessThan, "Compare <")]
    public void Compare_ShowsOperator(ComparisonOperator op, string expected)
        => Assert.Equal(expected, Title(new CompareNode { Operator = op }));

    [Theory]
    [InlineData(ArithmeticOperator.Add, "Math +")]
    [InlineData(ArithmeticOperator.Modulo, "Math %")]
    public void BinaryOp_ShowsOperator(ArithmeticOperator op, string expected)
        => Assert.Equal(expected, Title(new BinaryOpNode { Operator = op }));

    [Theory]
    [InlineData(BooleanOperator.And, "Logic &&")]
    [InlineData(BooleanOperator.Or, "Logic ||")]
    public void BooleanOp_ShowsOperator(BooleanOperator op, string expected)
        => Assert.Equal(expected, Title(new BooleanOpNode { Operator = op }));

    [Fact]
    public void GetParameter_TitleIsClean_NameShownOnPinInstead()
    {
        // The parameter NAME now labels the output pin (render-only, in BlueprintGraphModel), so the
        // node title stays generic and uncluttered.
        var pid = System.Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            AssetId = System.Guid.NewGuid(),
            Name = "T",
            Parameters = new() { new ParameterDecl { Id = pid, Name = "FiringLineStart" } },
        };
        Assert.Equal("Get Parameter", Title(new GetParameterNode { ParameterId = pid.ToString() }, asset));
    }
}
