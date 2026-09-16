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

    /// <summary>
    /// The backing queue, so a host shell can drain and draw what was notified.
    ///
    /// <para>
    /// ⚠ BP-223 — this was private, and the Hrot editor consequently had <b>no way</b> to reach the
    /// queue it was notifying into: the only <c>TryDequeue</c> in the repo was the demo shell's,
    /// against a queue it had constructed itself. Every notification a real host raised was
    /// enqueued and discarded.
    /// </para>
    /// </summary>
    public ToastQueue Toasts => _toasts;

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
