using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Literal inline editor: the value round-trips between the raw C# <c>ValueJson</c> and the plain,
/// designer-facing form. The C# syntax (float <c>f</c> suffix, string quotes) is added on commit and
/// stripped for editing, so the designer never types or sees it.
/// </summary>
public sealed class LiteralValueJsonTests
{
    [Theory]
    [InlineData("System.Int32", "5", "5")]
    [InlineData("System.Single", "1.5f", "1.5")]     // float 'f' suffix stripped for editing
    [InlineData("System.String", "\"hi\"", "hi")]     // string quotes stripped
    [InlineData("System.Boolean", "true", "true")]
    public void ToEditString_StripsCSharpSyntax(string typeId, string json, string expected)
        => Assert.Equal(expected, LiteralValueJson.ToEditString(typeId, json));

    [Theory]
    [InlineData("System.Int32", 5, "5")]
    [InlineData("System.Single", 1.5f, "1.5f")]       // float 'f' suffix added
    [InlineData("System.String", "hi", "\"hi\"")]     // string quotes added
    public void ToValueJson_AddsCSharpSyntax(string typeId, object value, string expected)
        => Assert.Equal(expected, LiteralValueJson.ToValueJson(typeId, value));

    [Fact]
    public void ToValueJson_Bool()
    {
        Assert.Equal("true",  LiteralValueJson.ToValueJson("System.Boolean", true));
        Assert.Equal("false", LiteralValueJson.ToValueJson("System.Boolean", false));
    }

    [Fact]
    public void HasInlineEditor_CommonTypesYes_UnknownNo()
    {
        Assert.True(LiteralValueJson.HasInlineEditor("System.Int32"));
        Assert.True(LiteralValueJson.HasInlineEditor("System.Single"));
        Assert.True(LiteralValueJson.HasInlineEditor("System.String"));
        Assert.False(LiteralValueJson.HasInlineEditor("System.UInt16")); // rare → stays in Details
        Assert.False(LiteralValueJson.HasInlineEditor(null));
    }
}
