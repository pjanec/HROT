namespace NodeEditor.Core.Action;

/// <summary>
/// A queued notification for the host shell to display (toast).
/// </summary>
public sealed class ToastQueue
{
    private readonly Queue<EditorNotification> _pending = new();

    /// <summary>All pending notifications (not yet dequeued by the host).</summary>
    public IReadOnlyCollection<EditorNotification> Pending => _pending;

    /// <summary>Number of pending notifications.</summary>
    public int Count => _pending.Count;

    /// <summary>Enqueue a notification for display.</summary>
    public void Enqueue(EditorNotification notification) => _pending.Enqueue(notification);

    /// <summary>Dequeue the next notification, or return false if empty.</summary>
    public bool TryDequeue(out EditorNotification notification) =>
        _pending.TryDequeue(out notification!);
}
