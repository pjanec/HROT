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
    [InlineData("123abc",           "_123abc")]   // ⭐ CE-2039: "123abc_…_Bp" was never a legal class name
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

    /// <summary>
    /// ⭐⭐ <c>CE-2039</c> — the ONE sanitizer reproduces every shape it replaced, byte for byte, on the inputs that tell the
    /// shapes apart (the sanitizer sweep's table). ⛔ A changed cell here renames a persisted class, struct or variable.
    /// </summary>
    [Theory]
    //            input        replace("")   replace("_")  strip("Asset")  pascal("UnknownBlueprint")
    [InlineData("My-Tree 2",  "My_Tree_2",  "My_Tree_2",  "MyTree2",      "MyTree2")]
    [InlineData("my_tree",    "my_tree",    "my_tree",    "my_tree",      "MyTree")]
    [InlineData("2Fast",      "_2Fast",     "_2Fast",     "_2Fast",       "_2Fast")]   // pascal: CE-2039's new digit guard
    [InlineData("",           "",           "_",          "Asset",        "UnknownBlueprint")]
    [InlineData("!!!",        "___",        "___",        "Asset",        "UnknownBlueprint")]
    [InlineData("Shoot BT!",  "Shoot_BT_",  "Shoot_BT_",  "ShootBT",      "ShootBT")]
    [InlineData("class",      "class",      "class",      "class",        "Class")]    // not bare: a PART of a name stays
    public void CE2039_TheOneSanitizer_KeepsEveryShape(string input, string replaceEmpty, string replaceUnderscore,
                                                       string strip, string pascal)
    {
        Assert.Equal(replaceEmpty,      global::Hrot.AiEditor.Persistence.Emit.Identifiers.ReplaceInvalid(input, ""));
        Assert.Equal(replaceUnderscore, global::Hrot.AiEditor.Persistence.Emit.Identifiers.ReplaceInvalid(input, "_"));
        Assert.Equal(strip,             global::Hrot.AiEditor.Persistence.Emit.Identifiers.StripInvalid(input, "Asset"));
        Assert.Equal(pascal,            global::Hrot.AiEditor.Persistence.Emit.Identifiers.PascalJoin(input, "UnknownBlueprint"));
        // ⭐ the compiler's spelling and the AI side's mirror are now the same function (pair P1 of the sweep)
        Assert.Equal(Sanitizer.SanitizeName(input), global::Hrot.AiEditor.Persistence.Emit.BlueprintClassNaming.SanitizeName(input));
    }

    /// <summary>⭐ <c>CE-2039</c> — a name emitted BARE (a class) that is a reserved keyword gets <c>_</c>; contextual keywords are
    /// legal identifiers and pass. A BTree named <c>class</c> used to emit <c>public static class class</c> (CS1001).</summary>
    [Theory]
    [InlineData("class", "_class")]
    [InlineData("int",   "_int")]
    [InlineData("lock",  "_lock")]
    [InlineData("var",   "var")]
    [InlineData("Class", "Class")]
    public void CE2039_ABareKeyword_IsPrefixed(string input, string expected)
        => Assert.Equal(expected, global::Hrot.AiEditor.Persistence.Emit.Identifiers.StripInvalid(input, "Asset", bare: true));

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
