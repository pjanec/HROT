using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

/// <summary>
/// Tests that CompoundPredicateHelper.IsChildReadOnly correctly reflects
/// CompoundPredicateDto.ReadOnlyChildIndices (UBP-P11T7).
/// </summary>
public sealed class PredicateBuilderP11T7Tests
{
    [Fact]
    public void IsChildReadOnly_IndexInList_ReturnsTrue()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int> { 0 },
        };

        Assert.True(CompoundPredicateHelper.IsChildReadOnly(compound, 0));
    }

    [Fact]
    public void IsChildReadOnly_IndexNotInList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int> { 0 },
        };

        Assert.False(CompoundPredicateHelper.IsChildReadOnly(compound, 1));
    }

    [Fact]
    public void IsChildReadOnly_EmptyList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = new System.Collections.Generic.List<int>(),
        };

        Assert.False(CompoundPredicateHelper.IsChildReadOnly(compound, 0));
    }

    [Fact]
    public void IsChildReadOnly_NullList_ReturnsFalse()
    {
        var compound = new CompoundPredicateDto
        {
            ReadOnlyChildIndices = null!,
        };

        Assert.False(CompoundPredicateHelper.IsChildReadOnly(compound, 0));
    }
}
