using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Verifies that the debug window DrawUI methods query the session for data (BPF-034).
/// </summary>
public sealed class DebugWindowDrawUITests
{
    // ---- Spy session that tracks method invocations ----

    private sealed class SpyDebugSession : IBlueprintDebugSession
    {
        public bool GetBreakpointsCalled  { get; private set; }
        public bool GetWatchesCalled      { get; private set; }
        public bool GetRecentHistoryCalled { get; private set; }

        // ---- IBlueprintProbeSink ----
        public void OnNodeEnter(Entity self, string nodeId) { }
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
        public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
        public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }

        // ---- Lifecycle ----
        public bool IsAttached => true;
        public void Attach()  { }
        public void Detach()  { }

        // ---- Breakpoints ----
        public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints()
        {
            GetBreakpointsCalled = true;
            return Array.Empty<Breakpoint>();
        }
        public bool IsAnyBreakpointActive => false;

        // ---- Watches ----
        public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => default;
        public void RemoveWatch(WatchId id) { }
        public void ClearAllWatches() { }
        public IReadOnlyList<Watch> GetWatches()
        {
            GetWatchesCalled = true;
            return Array.Empty<Watch>();
        }
        public bool IsAnyWatchActive => false;

        // ---- Entity filter ----
        public void SetEntityFilter(Entity? entity) { }
        public Entity? GetEntityFilter() => null;

        // ---- Active entities ----
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

        // ---- Pause state ----
        public bool IsPaused => false;
        public Breakpoint? PausedAt => null;
        public Entity? PausedOnEntity => null;

        // ---- Pause control ----
        public void Continue()  { }
        public void StepOver()  { }
        public void StepInto()  { }
        public void StepOut()   { }
        public void Pause()     { }

        // ---- Inspection ----
        public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
        public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
        {
            GetRecentHistoryCalled = true;
            return Array.Empty<NodeExecuted>();
        }

        // ---- Map registration ----
        public void RegisterDebugMap(DebugMap map) { }
        public void UnregisterDebugMap(Guid assetId) { }

        // ---- PDB locator ----
        public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }

        // ---- Hot reload ----
        public void OnHotReloadBegin() { }
        public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }
        public void OnNewTick() { }

        // ---- Events ----
        public event Action<BreakpointHit>?   OnBreakpointHit;
        public event Action<NodeExecuted>?     OnNodeExecuted;
        public event Action?                   OnSessionStateChanged;
        public event Action<Guid>?             OnBreakpointListChanged;

        private Action<PinValueChanged>? _pinValueChangedHandlers;
        event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
        {
            add    => _pinValueChangedHandlers += value;
            remove => _pinValueChangedHandlers -= value;
        }

        // Suppress unused event warnings.
        private void RaiseEvents()
        {
            OnBreakpointHit?.Invoke(null!);
            OnNodeExecuted?.Invoke(null!);
            OnSessionStateChanged?.Invoke();
            OnBreakpointListChanged?.Invoke(Guid.Empty);
        }
    }

    [Fact]
    public void DebugPanelWindow_DrawUI_Queries_Breakpoints_From_Session()
    {
        var spy    = new SpyDebugSession();
        var window = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.True(spy.GetBreakpointsCalled);
    }

    [Fact]
    public void WatchPanelWindow_DrawUI_Queries_Watches_From_Session()
    {
        var spy    = new SpyDebugSession();
        var window = new WatchPanelWindow(spy);

        window.DrawUI();

        Assert.True(spy.GetWatchesCalled);
    }

    [Fact]
    public void CallstackWindow_DrawUI_Queries_NodeHistory_From_Session()
    {
        var spy    = new SpyDebugSession();
        var store  = new EditorSelectionStore();
        var window = new CallstackWindow(spy, store);

        window.DrawUI();

        Assert.True(spy.GetRecentHistoryCalled);
    }
}
