using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Test double: records every IBlueprintProbeSink notification for assertion.
/// Also implements IBlueprintDebugSession with a simple in-memory breakpoint set.
/// </summary>
public sealed class CapturingDebugSession : IBlueprintProbeSink, IBlueprintDebugSession
{
    private readonly List<NodeEnterRecord> _nodeEntries = new();
    private readonly List<PinValueRecord>  _pinValues   = new();
    private readonly HashSet<string>       _breakpoints = new();
    private readonly Dictionary<Guid, DebugMap> _maps   = new();

    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId)
    {
        _nodeEntries.Add(new NodeEnterRecord(self, nodeId, Time: 0f));
        if (_breakpoints.Contains(nodeId))
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId, Guid.Empty, 0f, 0u));
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
        => _pinValues.Add(new PinValueRecord(self, pinId, value));

    public void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName) { }
    public void OnPeerCallExit(Entity entity) { }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;
    public void Detach() { }

    // ---- IBlueprintDebugSession -- breakpoints (string overloads for tests) --

    public void SetBreakpoint(string nodeId)   => _breakpoints.Add(nodeId);
    public void ClearBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public bool IsAnyBreakpointActive          => _breakpoints.Count > 0;

    // IBlueprintDebugSession GUID-based breakpoint methods (stubs for now)
    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
        => throw new NotImplementedException();
    public void ClearBreakpoint(BreakpointId id) => throw new NotImplementedException();
    public void ClearAllBreakpoints() => _breakpoints.Clear();
    public IReadOnlyList<Breakpoint> GetBreakpoints() => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public bool IsAnyWatchActive => false;
    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => throw new NotImplementedException();
    public void RemoveWatch(WatchId id) => throw new NotImplementedException();
    public void ClearAllWatches() => throw new NotImplementedException();
    public IReadOnlyList<Watch> GetWatches() => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- pause state ------------------------------

    public bool IsPaused => false;
    public Breakpoint? PausedAt => null;
    public Entity? PausedOnEntity => null;

    // ---- IBlueprintDebugSession -- entity filter ----------------------------

    public void SetEntityFilter(Entity? entity) { }
    public Entity? GetEntityFilter() => null;

    // ---- IBlueprintDebugSession -- active entity tracking ------------------

    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    // ---- IBlueprintDebugSession -- pause control ----------------------------

    public void Continue()  { }
    public void StepOver()  { }
    public void StepInto()  { }
    public void StepOut()   { }
    public void Pause()     { }

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
        => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- events -----------------------------------

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>?  OnNodeExecuted;
    public event Action? OnSessionStateChanged;
    public event Action<Guid>? OnBreakpointListChanged;

    // Explicit interface impl: avoids conflict with generic method OnPinValueChanged<T>.
    private Action<PinValueChanged>? _pinValueChangedHandlers;

    event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
    {
        add    => _pinValueChangedHandlers += value;
        remove => _pinValueChangedHandlers -= value;
    }

    public void RegisterDebugMap(DebugMap map)   => _maps[map.AssetId] = map;
    public void UnregisterDebugMap(Guid assetId) => _maps.Remove(assetId);

    // ---- IBlueprintDebugSession -- PDB locator ------------------------------

    public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }

    // ---- IBlueprintDebugSession -- hot reload --------------------------------

    public void OnHotReloadBegin() { }
    public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }

    // ---- Inspection helpers -------------------------------------------------

    public IReadOnlyList<NodeEnterRecord> NodeEntries => _nodeEntries;
    public IReadOnlyList<PinValueRecord>  PinValues   => _pinValues;

    public bool Hit(string nodeId)     => _nodeEntries.Any(r => r.NodeId == nodeId);
    public int  HitCount(string nodeId) => _nodeEntries.Count(r => r.NodeId == nodeId);

    public IReadOnlyList<NodeEnterRecord> HitsFor(Entity self)
        => _nodeEntries.Where(r => r.Self == self).ToList();

    public void Clear()
    {
        _nodeEntries.Clear();
        _pinValues.Clear();
    }
}

public sealed record NodeEnterRecord(Entity Self, string NodeId, float Time);
public sealed record PinValueRecord(Entity Self, string PinId, object? Value);
