using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Adapts the engine's native MasterSyncController to the Blueprint debug protocol.
/// </summary>
#pragma warning disable CS0618 // IBlueprintTimeController is obsolete; retained for one-batch backward compatibility
public sealed class MasterSyncTimeControllerAdapter : IEngineDebugTimeController, IBlueprintTimeController
#pragma warning restore CS0618
{
    private readonly MasterSyncController _masterSync;

    // 60 Hz fixed delta for stepping
    private const float StepDeltaSeconds = 1.0f / 60.0f;

    public MasterSyncTimeControllerAdapter(MasterSyncController masterSync)
    {
        _masterSync = masterSync ?? throw new ArgumentNullException(nameof(masterSync));
    }

    /// <summary>
    /// True if the engine is currently in lockstep/paused mode.
    /// </summary>
    public bool IsPausedByDebugger => _masterSync.GetMode() == TimeMode.Deterministic;

    /// <summary>
    /// Requests a soft pause. The current tick will finish, and time advancement
    /// will halt on the next frame.
    /// </summary>
    public void RequestPause()
    {
        // Transitioning to deterministic mode with an empty slave roster
        // effectively pauses the local simulation clock without waiting for network ACKs.
        _masterSync.SwitchToDeterministic(new HashSet<int>());
    }

    /// <summary>
    /// Resumes continuous time advancement.
    /// </summary>
    public void RequestResume()
    {
        _masterSync.SwitchToContinuous();
    }

    /// <summary>
    /// Advances the simulation clock by exactly one 60Hz frame.
    /// </summary>
    public void RequestStepOneTick()
    {
        if (IsPausedByDebugger)
        {
            _masterSync.Step(StepDeltaSeconds);
        }
    }
}
