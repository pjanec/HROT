using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// IGSelectionBridge implementation that accepts a subscription factory callback.
/// The factory receives an Action&lt;Entity?&gt; and returns an IDisposable token.
/// This keeps the shared library free of DDS or network dependencies.
/// </summary>
public sealed class CallbackSelectionBridge : IGSelectionBridge
{
    private readonly Func<Action<Entity?>, IDisposable> _subscribeFactory;
    private IDisposable? _subscription;
    private EditorSelectionStore? _store;

    public CallbackSelectionBridge(Func<Action<Entity?>, IDisposable> subscribeFactory)
    {
        _subscribeFactory = subscribeFactory ?? throw new ArgumentNullException(nameof(subscribeFactory));
    }

    public bool IsConnected => _subscription is not null;

    public void Connect(EditorSelectionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _subscription = _subscribeFactory(entity => store.SelectedEntity = entity);
    }

    public void Disconnect()
    {
        _subscription?.Dispose();
        _subscription = null;
        _store = null;
    }

    public void Dispose() => Disconnect();
}
