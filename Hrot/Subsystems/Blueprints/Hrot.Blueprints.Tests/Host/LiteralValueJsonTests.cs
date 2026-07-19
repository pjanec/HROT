using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Literal inline editor: the value round-trips between the raw C# <c>ValueJson</c> and the plain,
/// designer-facing value. The C# syntax (suffixes, casts, quotes) is added on commit and stripped for
/// editing, so the designer never types or sees it. The integer family edits through the Int32 proxy.
/// </summary>
public sealed class LiteralValueJsonTests
{
    [Theory]
    [InlineData("System.Int32",   "5",          "5")]
    [InlineData("System.Int64",   "-1L",        "-1")]      // long suffix stripped
    [InlineData("System.UInt16",  "(ushort)3",  "3")]       // cast stripped
    [InlineData("System.Byte",    "(byte)7",    "7")]
    [InlineData("System.UInt32",  "9u",         "9")]
    [InlineData("System.Single",  "1.5f",       "1.5")]     // float 'f' stripped
    [InlineData("System.String",  "\"hi\"",     "hi")]      // quotes stripped
    [InlineData("System.Boolean", "true",       "true")]
    public void ToEditString_StripsCSharpSyntax(string typeId, string json, string expected)
        => Assert.Equal(expected, LiteralValueJson.ToEditString(typeId, json));

    [Theory]
    [InlineData("System.Int32",  5,   "5")]
    [InlineData("System.Int64",  -1,  "-1L")]      // long suffix added
    [InlineData("System.UInt16", 3,   "(ushort)3")]// cast added
    [InlineData("System.Byte",   7,   "(byte)7")]
    [InlineData("System.UInt32", 9,   "9u")]
    [InlineData("System.Single", 1.5f,"1.5f")]     // float 'f' added
    [InlineData("System.String", "hi","\"hi\"")]   // quotes added
    public void ToValueJson_AddsCSharpSyntax(string typeId, object value, string expected)
        => Assert.Equal(expected, LiteralValueJson.ToValueJson(typeId, value));

    [Fact]
    public void ToValueJson_Bool()
    {
        Assert.Equal("true",  LiteralValueJson.ToValueJson("System.Boolean", true));
        Assert.Equal("false", LiteralValueJson.ToValueJson("System.Boolean", false));
    }

    [Theory]
    [InlineData("System.Int32",   "System.Int32")]   // integer family → Int32 proxy editor
    [InlineData("System.Int64",   "System.Int32")]
    [InlineData("System.UInt16",  "System.Int32")]
    [InlineData("System.Byte",    "System.Int32")]
    [InlineData("System.Single",  "System.Single")]
    [InlineData("System.Boolean", "System.Boolean")]
    [InlineData("System.String",  "System.String")]
    public void EditorTypeId_MapsToProxyEditor(string typeId, string expectedEditorType)
        => Assert.Equal(expectedEditorType, LiteralValueJson.EditorTypeId(typeId));

    [Fact]
    public void EditorTypeId_UnknownTypes_HaveNoInlineEditor()
    {
        Assert.Null(LiteralValueJson.EditorTypeId("System.Double")); // no double editor → title + Details
        Assert.Null(LiteralValueJson.EditorTypeId("Some.Struct"));
        Assert.Null(LiteralValueJson.EditorTypeId(null));
        Assert.False(LiteralValueJson.HasInlineEditor("System.Double"));
        Assert.True(LiteralValueJson.HasInlineEditor("System.Int64"));
    }
}
