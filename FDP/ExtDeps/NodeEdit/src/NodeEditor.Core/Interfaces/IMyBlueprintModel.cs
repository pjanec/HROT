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
/// <remarks>
/// <b><c>CreateDisabledReason</c></b> — ⭐⭐ Non-null when this section's "+" is currently unusable, and it is the reason WHY — shown as
/// the button's tooltip while the button is greyed.
///
/// <para>📌 <b>User ruling, <c>2026-08-17</c>, verbatim:</b> <i>"Disabling/graying a [+] on variable
/// section but showing explanatory tooltip … would be better than allowing user to click the button
/// and then saying that it is not possible — same information value, no false expectations."</i></para>
///
/// <para>⭐ <b>A REFINEMENT of <c>Q26-B2</c>, not a reversal.</b> That ruling forbids the "+"
/// <b>VANISHING</b> — <i>"the '+' stays and REFUSES OUT LOUD, naming the reason, rather than
/// vanishing and teaching nothing."</i> ⛔ Greying is not vanishing: the button stays visible and the
/// reason is still taught, ⭐ only now BEFORE the designer does the work instead of after.</para>
///
/// <para>⚠ <b>General, not macro-specific.</b> Any section that cannot currently create says why,
/// through this one field — ⛔ so the next refusal does not need a second mechanism.</para>
/// </remarks>
public sealed record MyBlueprintSectionDescriptor(
    string Id,
    string DisplayName,
    int SortOrder,
    string? IconKey,
    bool CanCreateItems,
    bool CanHaveCategories,
    string? CreateCommandId,
    string? CreateDisabledReason = null);

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
