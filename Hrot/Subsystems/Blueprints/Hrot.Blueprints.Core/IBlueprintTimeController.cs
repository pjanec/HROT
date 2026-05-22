namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Abstracts the engine's time control for the Blueprint debugger.
/// Provides soft-pause semantics (returns immediately; halts on next frame).
/// </summary>
public interface IBlueprintTimeController
{
    bool IsPausedByDebugger { get; }
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();
}
