using Fdp.Core;
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

    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId)
    {
        _nodeEntries.Add(new NodeEnterRecord(self, nodeId, Time: 0f));
        if (_breakpoints.Contains(nodeId))
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId));
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        => _pinValues.Add(new PinValueRecord(self, pinId, value));

    // ---- IBlueprintDebugSession --------------------------------------------

    public void SetBreakpoint(string nodeId)   => _breakpoints.Add(nodeId);
    public void ClearBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public bool IsAnyBreakpointActive          => _breakpoints.Count > 0;
    public bool IsAnyWatchActive               => false;

    public void Continue()  { }
    public void StepOver()  { }
    public void StepInto()  { }
    public void StepOut()   { }

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>?  OnNodeExecuted;

    // Explicit interface impl: avoids conflict with generic method OnPinValueChanged<T>.
    private Action<PinValueChanged>? _pinValueChangedHandlers;

    event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
    {
        add    => _pinValueChangedHandlers += value;
        remove => _pinValueChangedHandlers -= value;
    }

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
