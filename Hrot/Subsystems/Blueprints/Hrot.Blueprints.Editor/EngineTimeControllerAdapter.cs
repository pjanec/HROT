using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Adapter from the engine's time-control mechanism to IBlueprintTimeController.
/// The concrete engine type is discovered during M13 implementation (Q-16.1).
/// This stub satisfies the interface contract for testing and dependency injection.
/// </summary>
public sealed class EngineTimeControllerAdapter : IBlueprintTimeController
{
    // Actual engine reference stored as object until the concrete type is known.
    private readonly object _engineController;

    public EngineTimeControllerAdapter(object engineController)
    {
        _engineController = engineController ?? throw new ArgumentNullException(nameof(engineController));
    }

    public void RequestPause()
    {
        // TODO M13: invoke engine pause via _engineController when type is known.
    }

    public void RequestResume()
    {
        // TODO M13: invoke engine resume via _engineController when type is known.
    }

    public void RequestStepOneTick()
    {
        // TODO M13: invoke engine step via _engineController when type is known.
    }

    public bool IsPausedByDebugger => false;  // TODO M13: read from engine.
}
