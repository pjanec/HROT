using System;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Abstracts the engine's time control surface for diagnostic debuggers.
/// Provides soft-pause semantics (returns immediately; halts on next frame).
/// </summary>
public interface IEngineDebugTimeController
{
    bool IsPausedByDebugger { get; }
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();
}

/// <summary>
/// Backward-compatible alias for <see cref="IEngineDebugTimeController"/>.
/// Retained for one batch so existing Slice 1 code compiles unchanged.
/// </summary>
[Obsolete("Use IEngineDebugTimeController. IBlueprintTimeController will be removed after one batch.")]
public interface IBlueprintTimeController : IEngineDebugTimeController
{
}
