namespace NodeEditor.UI.Picker;

/// <summary>
/// The result returned when a picker confirms or cancels.
/// An empty selection indicates cancellation.
/// </summary>
/// <param name="Selection">Entries chosen by the user. Empty when cancelled.</param>
public sealed record PickerResult(IReadOnlyList<PickerEntry> Selection)
{
    /// <summary>⭐ CE-4168 (AQ108 D26 P3) — the <see cref="PickerAction.Id"/> of the footer action that confirmed; null for the classic OK / no actions.</summary>
    public string? ActionId { get; init; }

    /// <summary>⭐ CE-4168 (AQ108 D26 P2) — what the request's <see cref="PickerRequest.CapturePaneState"/> returned at confirm (the interactive pane's
    /// host-held state); null when the request has none.</summary>
    public object? PaneState { get; init; }

    /// <summary>True when the user dismissed the picker without confirming.</summary>
    public bool Cancelled => Selection.Count == 0;

    /// <summary>First selected entry, or null when cancelled or multi-select produced no items.</summary>
    public PickerEntry? First => Selection.Count > 0 ? Selection[0] : null;
}
