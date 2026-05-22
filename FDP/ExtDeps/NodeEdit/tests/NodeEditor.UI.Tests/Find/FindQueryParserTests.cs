using FluentAssertions;
using NodeEditor.UI.Find;
using Xunit;

namespace NodeEditor.UI.Tests.Find;

public sealed class FindQueryParserTests
{
    [Fact]
    public void Parse_FreeTextOnly_SetsFreeTextNoPrefix()
    {
        var result = FindQueryParser.Parse("Vector3");

        result.FreeText.Should().Be("Vector3");
        result.Prefixes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TypePrefix_ExtractsPrefixAndFreeText()
    {
        var result = FindQueryParser.Parse("type:Vector3 foo");

        result.Prefixes.Should().ContainKey("type").WhoseValue.Should().Be("Vector3");
        result.FreeText.Should().Be("foo");
    }

    [Fact]
    public void Parse_MultiplePrefix_ExtractsBothPrefixes()
    {
        var result = FindQueryParser.Parse("kind:branch error: foo");

        result.Prefixes.Should().ContainKey("kind").WhoseValue.Should().Be("branch");
        result.Prefixes.Should().ContainKey("error").WhoseValue.Should().Be(string.Empty);
        result.FreeText.Should().Be("foo");
    }

    [Fact]
    public void Parse_Empty_ReturnsEmptyFreeTextAndNoPrefixes()
    {
        var result = FindQueryParser.Parse(string.Empty);

        result.FreeText.Should().BeEmpty();
        result.Prefixes.Should().BeEmpty();
    }
}
