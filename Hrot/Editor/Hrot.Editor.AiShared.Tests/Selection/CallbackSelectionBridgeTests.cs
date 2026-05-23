using Fdp.Core;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Tests.Selection;

public sealed class CallbackSelectionBridgeTests
{
    // Helper: creates a subscription factory that captures the callback for manual invocation.
    private static (CallbackSelectionBridge Bridge, Action<Entity?> Fire, bool[] Disposed)
        MakeBridge()
    {
        Action<Entity?>? captured = null;
        bool[] disposed = { false };

        IDisposable Factory(Action<Entity?> callback)
        {
            captured = callback;
            return new DisposeSentinel(() => disposed[0] = true);
        }

        var bridge = new CallbackSelectionBridge(Factory);
        return (bridge, e => captured?.Invoke(e), disposed);
    }

    private sealed class DisposeSentinel : IDisposable
    {
        private readonly Action _onDispose;
        public DisposeSentinel(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }

    [Fact]
    public void IsConnected_IsFalse_BeforeConnect()
    {
        var (bridge, _, _) = MakeBridge();
        Assert.False(bridge.IsConnected);
    }

    [Fact]
    public void IsConnected_IsTrue_AfterConnect()
    {
        var (bridge, _, _) = MakeBridge();
        var store = new EditorSelectionStore();
        bridge.Connect(store);
        Assert.True(bridge.IsConnected);
    }

    [Fact]
    public void IsConnected_IsFalse_AfterDisconnect()
    {
        var (bridge, _, _) = MakeBridge();
        var store = new EditorSelectionStore();
        bridge.Connect(store);
        bridge.Disconnect();
        Assert.False(bridge.IsConnected);
    }

    [Fact]
    public void Connect_WhenExternalFiresEntity_StoreUpdated()
    {
        var (bridge, fire, _) = MakeBridge();
        var store = new EditorSelectionStore();
        bridge.Connect(store);

        var entity = new Entity(42, 1);
        fire(entity);

        Assert.Equal(entity, store.SelectedEntity);
    }

    [Fact]
    public void Connect_WhenExternalFiresNull_StoreEntitySetToNull()
    {
        var (bridge, fire, _) = MakeBridge();
        var store = new EditorSelectionStore();
        store.SelectedEntity = new Entity(1, 1);
        bridge.Connect(store);

        fire(null);

        Assert.Null(store.SelectedEntity);
    }

    [Fact]
    public void Disconnect_DisposesSubscription()
    {
        var (bridge, _, disposed) = MakeBridge();
        var store = new EditorSelectionStore();
        bridge.Connect(store);
        bridge.Disconnect();

        Assert.True(disposed[0]);
    }
}
