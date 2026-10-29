using System;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.Director;
using Xunit;

namespace Hrot.Editor.Tests.Director;

/// <summary>
/// ⭐⭐ <c>CE-4101</c> (AQ108 §2.11a, the OUT door) — the film director's stop of its LIVE run publishes the ONE Idle intent (<see cref="ScenarioLoadSequence.UnloadIntent"/>), not a hand-built copy of it: equal apart from the
/// TransactionId, sent through the master control. (A live run holds no scenario being edited, so the door has no question for it.)
/// 🔴 Mutation: <c>StopLive</c> building <c>new TransitionStateIntent { TargetState = Idle, TimeMode = "x" }</c> ⇒ the equality fails.
/// </summary>
public sealed class TheDirectorsStopIsTheOneIdleIntentTests
{
    private sealed class Master : Hrot.Presentation.DebugApi.IClusterMasterControl
    {
        public readonly System.Collections.Generic.List<TransitionStateIntent> Transitions = new();
        public void Transition(TransitionStateIntent intent) => Transitions.Add(intent);
        public void SeekReplay(SeekReplayIntent intent) { }
        public void SetReplayAxis(SetReplayAxisIntent intent) { }
        public void SetReplayLoop(SetReplayLoopIntent intent) { }
        public void Storage(ExecuteStorageOpIntent intent) { }
        public void TakeCheckpoint(TakeCheckpointIntent intent) { }
        public void RestoreCheckpoint(RestoreCheckpointIntent intent) { }
        public void SetRunSettings(SetRunSettingsIntent intent) { }
        public void CheckUnsaved(CheckUnsavedIntent intent) { }
        public void Cancel(CancelOperationIntent intent) { }
        public void Dump(ExecuteDiagnosticDumpIntent intent) { }
    }

    [Fact]
    public void StopLive_PublishesTheOneIdleIntent()
    {
        var master = new Master();
        var host = new EditorDirectorHost(
            world: () => null, load: _ => { }, operatingEdit: () => false, inPreview: () => false, enterPreview: _ => { }, exitPreview: () => { },
            paused: () => true, pause: () => { }, stepBy: _ => { }, behaviourName: _ => null, takes: null!, slotRunInFlight: () => false,
            writeCheckpoint: (_, _) => { }, restoreCheckpoint: _ => 0d, loadLive: _ => { }, master: master, state: () => ClusterState.OperatingLive,
            recordingPath: () => null, openRecording: _ => throw new NotSupportedException());

        host.StopLive();

        var sent = Assert.Single(master.Transitions);
        var one = ScenarioLoadSequence.UnloadIntent();
        one.TransactionId = sent.TransactionId;
        Assert.Equal(one, sent);
        Assert.Equal(ClusterState.Idle, sent.TargetState);
    }
}
