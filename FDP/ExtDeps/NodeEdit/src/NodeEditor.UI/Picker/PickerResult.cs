namespace NodeEditor.UI.Picker;

/// <summary>
/// The result returned when a picker confirms or cancels.
/// An empty selection indicates cancellation.
/// </summary>
/// <param name="Selection">Entries chosen by the user. Empty when cancelled.</param>
public sealed record PickerResult(IReadOnlyList<PickerEntry> Selection)
{
    /// <summary>True when the user dismissed the picker without confirming.</summary>
    public bool Cancelled => Selection.Count == 0;

    /// <summary>First selected entry, or null when cancelled or multi-select produced no items.</summary>
    public PickerEntry? First => Selection.Count > 0 ? Selection[0] : null;
}
