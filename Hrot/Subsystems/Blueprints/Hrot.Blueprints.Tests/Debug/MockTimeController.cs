using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Test double for IBlueprintTimeController.
/// Records all pause/resume/step requests for assertion.
/// </summary>
public sealed class MockTimeController : IBlueprintTimeController
{
    public bool PauseWasRequested  { get; private set; }
    public int  PauseRequestCount  { get; private set; }
    public int  ResumeCount        { get; private set; }
    public int  StepRequestCount   { get; private set; }
    public bool IsPausedByDebugger { get; private set; }

    public void RequestPause()
    {
        PauseWasRequested = true;
        PauseRequestCount++;
        IsPausedByDebugger = true;
    }

    public void RequestResume()
    {
        ResumeCount++;
        IsPausedByDebugger = false;
    }

    public void RequestStepOneTick()
    {
        StepRequestCount++;
    }
}
