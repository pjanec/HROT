using System.Numerics;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Category tree node used by the Wide and Tree layouts to display a sidebar
/// hierarchy. Build from <see cref="PickerEntry.Category"/> strings if the
/// host does not supply an explicit tree.
/// </summary>
/// <param name="Name">Display label for this category node.</param>
/// <param name="Children">Child nodes, may be empty.</param>
public sealed record CategoryNode(string Name, IReadOnlyList<CategoryNode> Children);

/// <summary>
/// Caller-supplied input to open a picker via <see cref="PickerRegistry.OpenPicker"/>.
/// Fully data-driven — the picker window renders items and emits the choice.
/// </summary>
public sealed class PickerRequest
{
    /// <summary>Stable key identifying this picker context (favorites + recent persistence).</summary>
    public required string ContextKey { get; init; }

    /// <summary>Window title shown to the user.</summary>
    public required string Title { get; init; }

    /// <summary>Layout to use for rendering items.</summary>
    public PickerLayout Layout { get; init; } = PickerLayout.Standard;

    /// <summary>Selection mode (single / multi / multi-ordered).</summary>
    public PickerSelectionMode SelectionMode { get; init; } = PickerSelectionMode.Single;

    /// <summary>Source of items. Called once and cached on first open.</summary>
    public required Func<IEnumerable<PickerEntry>> ItemsProvider { get; init; }

    /// <summary>Initial search text (e.g. pre-filled from the dropped wire's type filter).</summary>
    public string InitialQuery { get; init; } = "";

    /// <summary>Screen position to anchor the window to. Null = centered in main viewport.</summary>
    public Vector2? AnchorScreen { get; init; }

    /// <summary>Optional category tree for Wide/Tree layouts. Null = built from Category strings.</summary>
    public CategoryNode? CategoryRoot { get; init; }
}
