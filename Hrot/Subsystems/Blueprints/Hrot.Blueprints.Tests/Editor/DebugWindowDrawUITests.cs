using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Verifies that the debug window DrawUI methods query the session for data (BPF-034 / FIX2-006).
/// </summary>
public sealed class DebugWindowDrawUITests
{
    // ---- Spy session that tracks method invocations and supplies configurable data ----

    private sealed class SpyDebugSession : IBlueprintDebugSession
    {
        public bool GetBreakpointsCalled       { get; private set; }
        public bool GetWatchesCalled           { get; private set; }
        public bool GetCurrentCallStackCalled  { get; private set; }

        // Configurable return values.
        public List<Breakpoint>  BreakpointsToReturn  { get; } = new();
        public List<Watch>       WatchesToReturn       { get; } = new();
        public List<CallFrame>   CallFramesToReturn    { get; } = new();
        public bool              PausedValue           { get; set; }

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
            return BreakpointsToReturn.AsReadOnly();
        }
        public bool IsAnyBreakpointActive => false;

        // ---- Watches ----
        public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => default;
        public void RemoveWatch(WatchId id) { }
        public void ClearAllWatches() { }
        public IReadOnlyList<Watch> GetWatches()
        {
            GetWatchesCalled = true;
            return WatchesToReturn.AsReadOnly();
        }
        public bool IsAnyWatchActive => false;

        // ---- Entity filter ----
        public void SetEntityFilter(Entity? entity) { }
        public Entity? GetEntityFilter() => null;

        // ---- Active entities ----
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

        // ---- Pause state ----
        public bool IsPaused => PausedValue;
        public Breakpoint? PausedAt => null;
        public Entity? PausedOnEntity => null;

        // ---- Pause control ----
        public string? LastStepAction { get; private set; }
        public void Continue()  { LastStepAction = "Continue"; }
        public void StepOver()  { LastStepAction = "StepOver"; }
        public void StepInto()  { LastStepAction = "StepInto"; }
        public void StepOut()   { LastStepAction = "StepOut"; }
        public void Pause()     { LastStepAction = "Pause"; }

        // ---- Inspection ----
        public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
        public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId) => null;
        public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
            => Array.Empty<NodeExecuted>();
        public IReadOnlyList<CallFrame> GetCurrentCallStack()
        {
            GetCurrentCallStackCalled = true;
            return CallFramesToReturn.AsReadOnly();
        }
        public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
            => Array.Empty<NodeHistoryEntry>();

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

    // FIX2-006: DebugPanelWindow calls GetBreakpoints() and stores result.
    [Fact]
    public void DebugPanelWindow_DrawUI_Queries_Breakpoints_From_Session()
    {
        var spy    = new SpyDebugSession();
        var window = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.True(spy.GetBreakpointsCalled);
    }

    // FIX2-006: DebugPanelWindow populates LastRenderedBreakpoints with session data.
    [Fact]
    public void DebugPanelWindow_DrawUI_LastRenderedBreakpoints_Reflects_Session_Data()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var bp      = new Breakpoint(new BreakpointId(1), assetId, Guid.Empty,
                          nodeId.ToString("D"), 3, true);
        var spy     = new SpyDebugSession { PausedValue = true };
        spy.BreakpointsToReturn.Add(bp);
        var window  = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.NotNull(window.LastRenderedBreakpoints);
        Assert.Single(window.LastRenderedBreakpoints!);
        Assert.Equal(bp.NodeId, window.LastRenderedBreakpoints![0].NodeId);
        Assert.Equal(3, window.LastRenderedBreakpoints![0].HitCount);
        Assert.True(window.LastRenderedPausedState);
    }

    // FIX2-006: WatchPanelWindow calls GetWatches() and stores result.
    [Fact]
    public void WatchPanelWindow_DrawUI_Queries_Watches_From_Session()
    {
        var spy    = new SpyDebugSession();
        var window = new WatchPanelWindow(spy);

        window.DrawUI();

        Assert.True(spy.GetWatchesCalled);
    }

    // FIX2-006: WatchPanelWindow populates LastRenderedWatches with session data.
    [Fact]
    public void WatchPanelWindow_DrawUI_LastRenderedWatches_Reflects_Session_Data()
    {
        var watch = new Watch(new WatchId(1), Guid.NewGuid(), Guid.Empty,
                        Guid.NewGuid(), "MyPin", typeof(float));
        var spy   = new SpyDebugSession();
        spy.WatchesToReturn.Add(watch);
        var window = new WatchPanelWindow(spy);

        window.DrawUI();

        Assert.NotNull(window.LastRenderedWatches);
        Assert.Single(window.LastRenderedWatches!);
        Assert.Equal("MyPin", window.LastRenderedWatches![0].DisplayName);
    }

    // FIX2-006: CallstackWindow must call GetCurrentCallStack(), NOT GetRecentNodeHistory().
    [Fact]
    public void CallstackWindow_DrawUI_Queries_CurrentCallStack_From_Session()
    {
        var spy    = new SpyDebugSession();
        var store  = new EditorSelectionStore();
        var window = new CallstackWindow(spy, store);

        window.DrawUI();

        Assert.True(spy.GetCurrentCallStackCalled,
            "CallstackWindow.DrawUI() must call GetCurrentCallStack().");
    }

    // FIX2-006: CallstackWindow populates LastRenderedFrames with call stack data.
    [Fact]
    public void CallstackWindow_DrawUI_LastRenderedFrames_Reflects_Session_CallStack()
    {
        var frame  = new CallFrame("asset-001", "Execute", 0);
        var spy    = new SpyDebugSession();
        spy.CallFramesToReturn.Add(frame);
        var store  = new EditorSelectionStore();
        var window = new CallstackWindow(spy, store);

        window.DrawUI();

        Assert.NotNull(window.LastRenderedFrames);
        Assert.Single(window.LastRenderedFrames!);
        Assert.Equal("Execute",   window.LastRenderedFrames![0].MethodName);
        Assert.Equal("asset-001", window.LastRenderedFrames![0].PeerAssetIdString);
    }

    // ── Step control tests (BATCH-C) ────────────────────────────────────────

    [Fact]
    public void DebugPanelWindow_DrawUI_LastStepActionInvoked_ResetsToNull_OnEachDraw()
    {
        var spy    = new SpyDebugSession { PausedValue = true };
        var window = new DebugPanelWindow(spy);

        window.DrawUI();

        // LastStepActionInvoked should be null after DrawUI (reset at start of each call).
        Assert.Null(window.LastStepActionInvoked);
    }

    [Fact]
    public void DebugPanelWindow_DrawUI_LastRenderedPausedState_True_WhenPaused()
    {
        var spy    = new SpyDebugSession { PausedValue = true };
        var window = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.True(window.LastRenderedPausedState);
    }

    [Fact]
    public void DebugPanelWindow_DrawUI_LastRenderedPausedState_False_WhenNotPaused()
    {
        var spy    = new SpyDebugSession { PausedValue = false };
        var window = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.False(window.LastRenderedPausedState);
    }

    [Fact]
    public void DebugPanelWindow_DrawUI_LastRenderedBreakpoints_ReflectsData_WhenPaused()
    {
        var assetId = Guid.NewGuid();
        var nodeId  = Guid.NewGuid();
        var bp      = new Breakpoint(new BreakpointId(42), assetId, Guid.Empty,
                          nodeId.ToString("D"), 5, true);
        var spy     = new SpyDebugSession { PausedValue = true };
        spy.BreakpointsToReturn.Add(bp);
        var window  = new DebugPanelWindow(spy);

        window.DrawUI();

        Assert.NotNull(window.LastRenderedBreakpoints);
        Assert.Single(window.LastRenderedBreakpoints!);
        Assert.Equal(bp.NodeId, window.LastRenderedBreakpoints![0].NodeId);
        Assert.Equal(5, window.LastRenderedBreakpoints![0].HitCount);
        Assert.True(window.LastRenderedPausedState);
    }
}
