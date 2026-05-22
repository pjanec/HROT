using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-provided model for the "My Blueprint" panel: a hierarchical
/// outline of the asset's variables, functions, macros, events, and
/// dispatchers. The editor renders this purely as data; semantics are
/// entirely host-defined.
/// </summary>
public interface IMyBlueprintModel
{
    /// <summary>Top-level sections (Graphs, Functions, Variables, ...).</summary>
    IReadOnlyList<MyBlueprintSectionDescriptor> Sections { get; }

    /// <summary>Items in a given section.</summary>
    IReadOnlyList<MyBlueprintItem> GetItems(string sectionId);

    /// <summary>Raised when section content changes.</summary>
    event System.Action? Changed;
}

/// <summary>Descriptor for a top-level My Blueprint section.</summary>
public sealed record MyBlueprintSectionDescriptor(
    string Id,
    string DisplayName,
    int SortOrder,
    string? IconKey,
    bool CanCreateItems,
    bool CanHaveCategories,
    string? CreateCommandId);

/// <summary>An item appearing in a section. Can have children (nested categories or sub-items).</summary>
public sealed record MyBlueprintItem(
    string ItemId,
    string SectionId,
    string DisplayName,
    string? CategoryPath,
    string? IconKey,
    string? BadgeText,
    Vector4? AccentColor,
    IReadOnlyList<MyBlueprintItem>? Children,
    bool IsRenamable,
    bool IsDeletable,
    bool IsHostDefined,
    string? Tooltip);
