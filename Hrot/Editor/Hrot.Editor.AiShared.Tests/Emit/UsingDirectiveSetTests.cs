using Hrot.Editor.AiShared.Emit;

namespace Hrot.Editor.AiShared.Tests.Emit;

public sealed class UsingDirectiveSetTests
{
    [Fact]
    public void SortUsings_EmptyInput_ReturnsEmpty()
    {
        var result = FluentCSharpEmitterBase.SortUsings(Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void SortUsings_SystemFirst_ThenOthers_WithBlankLine()
    {
        var input = new[] { "Hrot.Foo", "System.IO", "Fbt", "System" };
        var result = FluentCSharpEmitterBase.SortUsings(input);

        Assert.Equal("System", result[0]);
        Assert.Equal("System.IO", result[1]);
        Assert.Equal(string.Empty, result[2]); // blank-line separator
        Assert.Equal("Fbt", result[3]);
        Assert.Equal("Hrot.Foo", result[4]);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void SortUsings_SystemOnly_NoBlankLine()
    {
        var input = new[] { "System.IO", "System" };
        var result = FluentCSharpEmitterBase.SortUsings(input);

        Assert.DoesNotContain(string.Empty, result);
        Assert.Equal(2, result.Count);
        Assert.Equal("System", result[0]);
        Assert.Equal("System.IO", result[1]);
    }

    [Fact]
    public void SortUsings_NonSystemOnly_NoLeadingBlankLine()
    {
        var input = new[] { "Zeta", "Alpha" };
        var result = FluentCSharpEmitterBase.SortUsings(input);

        Assert.DoesNotContain(string.Empty, result);
        Assert.Equal("Alpha", result[0]);
        Assert.Equal("Zeta", result[1]);
    }

    [Fact]
    public void SortUsings_AlphabeticallySorted()
    {
        var input = new[] { "System.Text", "System.Collections.Generic", "System", "System.IO" };
        var result = FluentCSharpEmitterBase.SortUsings(input);

        Assert.Equal("System", result[0]);
        Assert.Equal("System.Collections.Generic", result[1]);
        Assert.Equal("System.IO", result[2]);
        Assert.Equal("System.Text", result[3]);
    }

    [Fact]
    public void SortUsings_OtherGroupAlphabeticallySorted()
    {
        var input = new[] { "Zeta", "Alpha", "Mid" };
        var result = FluentCSharpEmitterBase.SortUsings(input);

        Assert.Equal("Alpha", result[0]);
        Assert.Equal("Mid", result[1]);
        Assert.Equal("Zeta", result[2]);
    }

    [Fact]
    public void UsingDirectiveSet_Add_ToSortedList_Works()
    {
        var set = new UsingDirectiveSet();
        set.Add("System.IO");
        set.Add("Hrot.Core");
        set.Add("System");

        var result = set.ToSortedList();
        Assert.Equal("System", result[0]);
        Assert.Equal("System.IO", result[1]);
        Assert.Equal(string.Empty, result[2]);
        Assert.Equal("Hrot.Core", result[3]);
    }

    [Fact]
    public void UsingDirectiveSet_AddRange_Works()
    {
        var set = new UsingDirectiveSet();
        set.AddRange(new[] { "System.Collections.Generic", "Fdp.Core" });

        var result = set.ToSortedList();
        Assert.Equal("System.Collections.Generic", result[0]);
        Assert.Equal(string.Empty, result[1]);
        Assert.Equal("Fdp.Core", result[2]);
    }
}
