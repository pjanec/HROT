using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class SanitizerTests
{
    [Theory]
    [InlineData("MoveToAndFire",    "MoveToAndFire")]
    [InlineData("Move To And Fire", "MoveToAndFire")]
    [InlineData("move-to-fire",     "MoveToFire")]
    [InlineData("hello world",      "HelloWorld")]
    [InlineData("abc123",           "Abc123")]
    [InlineData("123abc",           "123abc")]
    [InlineData("  spaces  ",       "Spaces")]
    [InlineData("",                 "UnknownBlueprint")]
    [InlineData("---",              "UnknownBlueprint")]
    public void SanitizeName_ProducesExpectedIdentifier(string input, string expected)
    {
        var result = Sanitizer.SanitizeName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("A", 0x12345678, false, "A_12345678_Bp.g.cs")]
    [InlineData("A", 0x12345678, true,  "BlueprintRegistrar_A_12345678_Bp.g.cs")]
    public void GeneratedFileName_ProducesExpectedFileName(
        string sanitizedName, int blueprintId, bool isRegistrar, string expected)
    {
        var result = Sanitizer.GeneratedFileName(sanitizedName, blueprintId, isRegistrar);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeName_IsDeterministic()
    {
        const string input = "Move To And Fire";
        Assert.Equal(Sanitizer.SanitizeName(input), Sanitizer.SanitizeName(input));
    }

    [Fact]
    public void SanitizeName_AllSpecialCharsStripped()
    {
        // Punctuation and symbols should be treated as word separators.
        var result = Sanitizer.SanitizeName("foo!bar@baz#qux");
        Assert.Equal("FooBarBazQux", result);
    }
}
