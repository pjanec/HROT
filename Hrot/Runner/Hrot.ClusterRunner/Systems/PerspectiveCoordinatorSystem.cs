using System.Collections.Concurrent;
using Fdp.Toolkit.Runner;
using Hrot.Common;

namespace Hrot.ClusterRunner.Systems;

/// <summary>
/// Bridges <see cref="Fdp.Presentation.WindowManager.WindowManager.OnPerspectiveChanged"/>
/// UI events to the subsystem layer by queuing them thread-safely and draining the queue
/// each frame via <see cref="ProcessPendingEvents"/>.
///
/// <para>This is a plain coordinator class — not an ECS ComponentSystem — because it
/// bridges a UI-level C# event to the orchestration layer without requiring a shared world.
/// Wrap it in a <see cref="PerspectiveUpdateSubsystem"/> to integrate with the frame loop.</para>
/// </summary>
public sealed class PerspectiveCoordinatorSystem
{
    private readonly SubsystemOrchestrator _orchestrator;
    private readonly IReadOnlyDictionary<string, string> _perspectiveToSubsystemName;
    private readonly ConcurrentQueue<TogglePerspectiveEvent> _queue = new();
    private string _currentPerspective = string.Empty;

    /// <summary>
    /// Initialises the coordinator.
    /// </summary>
    /// <param name="orchestrator">
    /// The orchestrator whose <see cref="SubsystemOrchestrator.SwitchMapOwner"/> is called
    /// when a perspective transition is processed.
    /// </param>
    /// <param name="perspectiveToSubsystemName">
    /// Maps perspective names (e.g. "IG") to subsystem names (e.g. "IG") used by
    /// <see cref="SubsystemOrchestrator.SwitchMapOwner"/>.
    /// Unknown perspective names are silently ignored by the orchestrator.
    /// </param>
    public PerspectiveCoordinatorSystem(
        SubsystemOrchestrator orchestrator,
        IReadOnlyDictionary<string, string> perspectiveToSubsystemName)
    {
        _orchestrator              = orchestrator;
        _perspectiveToSubsystemName = perspectiveToSubsystemName;
    }

    /// <summary>The last perspective that was successfully processed, or empty string before the first event.</summary>
    public string CurrentPerspective => _currentPerspective;

    /// <summary>
    /// Enqueues a perspective-change event for processing on the next frame.
    /// Thread-safe; may be called from the UI thread.
    /// </summary>
    public void Enqueue(TogglePerspectiveEvent evt) => _queue.Enqueue(evt);

    /// <summary>
    /// Drains the pending event queue and applies each perspective transition in order.
    /// Must be called from the frame-update thread.
    /// </summary>
    public void ProcessPendingEvents()
    {
        while (_queue.TryDequeue(out var evt))
        {
            if (_perspectiveToSubsystemName.TryGetValue(evt.NewPerspective, out var subsystemName))
                _orchestrator.SwitchMapOwner(subsystemName);

            _currentPerspective = evt.NewPerspective;
        }
    }
}
