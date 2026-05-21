using Fdp.Core;

namespace Hrot.Blueprints.Core.Debug;

// Minimal record types for debug session events.
public sealed record BreakpointHit(Entity Self, string NodeId);
public sealed record NodeExecuted(Entity Self, string NodeId, float Time);
public sealed record PinValueChanged(Entity Self, string PinId, object? Value);

/// <summary>
/// Minimal debug session interface for test use (Slice 1).
/// Full surface in Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md.
/// </summary>
public interface IBlueprintDebugSession : IBlueprintProbeSink
{
    // Breakpoint management
    void SetBreakpoint(string nodeId);
    void ClearBreakpoint(string nodeId);
    bool IsAnyBreakpointActive { get; }

    // Watch management
    bool IsAnyWatchActive { get; }

    // Pause control stubs
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();

    // Events
    event Action<BreakpointHit>? OnBreakpointHit;
    event Action<NodeExecuted>? OnNodeExecuted;
    event Action<PinValueChanged>? OnPinValueChangedEvent;
}
