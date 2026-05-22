namespace NodeEditor.Core.Action;

/// <summary>
/// Default implementation of <see cref="IEditorIndicators"/>.
/// Wraps a <see cref="ToastQueue"/> and stores the current <see cref="EditorStatusSnapshot"/>.
/// </summary>
public sealed class EditorIndicatorsImpl : IEditorIndicators
{
    private readonly ToastQueue _toasts;
    private EditorStatusSnapshot _snapshot;

    /// <summary>Create an indicators impl backed by the given toast queue.</summary>
    public EditorIndicatorsImpl(ToastQueue toasts)
    {
        _toasts = toasts;
    }

    /// <inheritdoc/>
    public EditorStatusSnapshot Snapshot => _snapshot;

    /// <inheritdoc/>
    public event System.Action? Changed;

    /// <inheritdoc/>
    public void Notify(EditorNotification notification) => _toasts.Enqueue(notification);

    /// <summary>
    /// Replace the current snapshot. Raises <see cref="Changed"/> only if any field differs.
    /// </summary>
    public void UpdateSnapshot(EditorStatusSnapshot newSnapshot)
    {
        if (_snapshot == newSnapshot) return;
        _snapshot = newSnapshot;
        Changed?.Invoke();
    }
}
