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

    private static EditorDirectorHost Host(Func<string?> loaded) => new(
        world: () => null, load: _ => { }, operatingEdit: () => true, inPreview: () => false, enterPreview: _ => { }, exitPreview: () => { },
        paused: () => true, pause: () => { }, stepBy: _ => { }, behaviourName: _ => null, takes: null!, slotRunInFlight: () => false,
        writeCheckpoint: (_, _) => { }, restoreCheckpoint: _ => 0d, loadLive: _ => { }, master: new Master(), state: () => ClusterState.OperatingEdit,
        recordingPath: () => null, openRecording: _ => throw new NotSupportedException(), loadedScenario: loaded);

    private static Hrot.UI.Common.RunMode.DoorQuestion Q(Hrot.UI.Common.RunMode.DoorQuestionKind kind, Action? discard = null) =>
        new(kind, "t", "This scenario has unsaved changes.", new[] { new Hrot.UI.Common.RunMode.RunModeConfirmation.Button("Discard", discard) }, Run: "a dry run");

    /// <summary>
    /// ⭐ CE-4267 — the director's own answers to the door. A run in progress (measured: the previous take's preview still LEAVING) and a closed gate are
    /// WAITS — asked again, the reason kept for the cap; unsaved edits of the director's OWN previous take are discarded; the user's own are refused with the reason.
    /// 🔴 Red-proof (compiles): StopAndLoad without <c>LoadMayRetry = true</c> ⇒ every take after a played one failed "a run is in progress (a dry run)" (5 of 11 on the reel).
    /// </summary>
    [Fact]
    public void CE4267_TheDirectorsAsker_WaitsOnARun_DiscardsItsOwnTake_RefusesTheUsersWork()
    {
        string? loaded = "s-a";
        var host = Host(() => loaded);
        var asker = new EditorDirectorHost.DirectorAsker(host);

        asker.Ask(Q(Hrot.UI.Common.RunMode.DoorQuestionKind.StopAndLoad));
        Assert.True(host.LoadMayRetry);
        Assert.Contains("a run is in progress (a dry run)", host.LoadRefusal);

        host.LoadScenario("s-a");                                          // no door: the director's own load of s-a
        bool discarded = false;
        asker.Ask(Q(Hrot.UI.Common.RunMode.DoorQuestionKind.Unsaved, () => discarded = true));
        Assert.True(discarded);                                            // its own previous take's edits

        loaded = "users-own";
        discarded = false;
        host.LoadScenario("s-b");                                          // resets the outcome; the editor now shows the user's scenario
        loaded = "users-own";
        asker.Ask(Q(Hrot.UI.Common.RunMode.DoorQuestionKind.Unsaved, () => discarded = true));
        Assert.False(discarded);                                           // ⛔ never the user's work
        Assert.False(host.LoadMayRetry);
        Assert.Contains("save or discard it in the editor", host.LoadRefusal);
    }
}
