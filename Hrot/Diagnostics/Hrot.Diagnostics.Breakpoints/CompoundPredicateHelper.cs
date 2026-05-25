using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>Helpers for working with CompoundPredicateDto structure.</summary>
public static class CompoundPredicateHelper
{
    /// <summary>
    /// Returns true if child at <paramref name="childIndex"/> is marked read-only
    /// by a menu populator via <see cref="CompoundPredicateDto.ReadOnlyChildIndices"/>.
    /// </summary>
    public static bool IsChildReadOnly(CompoundPredicateDto dto, int childIndex)
        => dto.ReadOnlyChildIndices?.Contains(childIndex) == true;
}
