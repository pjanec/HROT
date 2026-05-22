using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Optional integration with a debugger. The host supplies this when a
/// debug session is attached.
/// </summary>
public interface IDebugSession
{
    bool IsAttached { get; }
    bool IsPaused { get; }
    NodeId? CurrentlyExecutingNode { get; }
    IReadOnlySet<NodeId> RecentlyExecutedNodes { get; }
    IReadOnlySet<NodeId> Breakpoints { get; }
    IReadOnlySet<PinId> WatchedPins { get; }

    void ToggleBreakpoint(NodeId node);
    void ToggleWatch(PinId pin);
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();

    /// <summary>Get the current value at a watched pin (only valid while paused).</summary>
    object? GetWatchValue(PinId pin);

    event System.Action? StateChanged;
}
