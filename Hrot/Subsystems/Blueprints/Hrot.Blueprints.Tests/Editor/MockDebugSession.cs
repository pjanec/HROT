using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Editor;

internal sealed class MockDebugSession : IBlueprintDebugSession
{
    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;
    public void Detach() { }

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId) => default;
    public void ClearBreakpoint(BreakpointId id) { }
    public void ClearAllBreakpoints() { }
    public IReadOnlyList<Breakpoint> GetBreakpoints() => Array.Empty<Breakpoint>();
    public bool IsAnyBreakpointActive => false;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => default;
    public void RemoveWatch(WatchId id) { }
    public void ClearAllWatches() { }
    public IReadOnlyList<Watch> GetWatches() => Array.Empty<Watch>();
    public bool IsAnyWatchActive => false;

    // ---- IBlueprintDebugSession -- entity filter ----------------------------

    public void SetEntityFilter(Entity? entity) { }
    public Entity? GetEntityFilter() => null;

    // ---- IBlueprintDebugSession -- active entity tracking ------------------

    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    // ---- IBlueprintDebugSession -- pause state ------------------------------

    public bool IsPaused { get; set; }
    public Breakpoint? PausedAt => null;
    public Entity? PausedOnEntity => null;

    // ---- IBlueprintDebugSession -- pause control ----------------------------

    public void Continue()  { }
    public void StepOver()  { }
    public void StepInto()  { }
    public void StepOut()   { }
    public void Pause()     { }

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100) => Array.Empty<NodeExecuted>();

    // ---- IBlueprintDebugSession -- map registration ------------------------

    public void RegisterDebugMap(DebugMap map) { }
    public void UnregisterDebugMap(Guid assetId) { }

    // ---- IBlueprintDebugSession -- PDB locator ------------------------------

    public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }

    // ---- IBlueprintDebugSession -- hot reload --------------------------------

    public void OnHotReloadBegin() { }
    public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }
    public void OnNewTick() { }

    // ---- IBlueprintDebugSession -- events -----------------------------------

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>?  OnNodeExecuted;
    public event Action?                OnSessionStateChanged;
    public event Action<Guid>?          OnBreakpointListChanged;

    // Explicit interface impl to avoid C# conflict with generic method OnPinValueChanged<T>.
    private Action<PinValueChanged>? _pinValueChangedHandlers;
    public int PinValueChangedSubscriberCount => _pinValueChangedHandlers?.GetInvocationList().Length ?? 0;

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
