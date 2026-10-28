using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.DebugApi;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>⭐ R-3052…R-3055 — a cluster master that records what the debug surface asked it, for rails.</summary>
internal sealed class RecordingMasterControl : IClusterMasterControl
{
    public readonly List<object> Sent = new();
    public readonly List<TransitionStateIntent> Transitions = new();
    public readonly List<CancelOperationIntent> Cancels = new();

    public void Transition(TransitionStateIntent intent) { Transitions.Add(intent); Sent.Add(intent); }
    public void SeekReplay(SeekReplayIntent intent) => Sent.Add(intent);
    public void SetReplayAxis(SetReplayAxisIntent intent) => Sent.Add(intent);
    public void SetReplayLoop(SetReplayLoopIntent intent) => Sent.Add(intent);   // CE-3329 §12n
    public void Storage(ExecuteStorageOpIntent intent) => Sent.Add(intent);
    public void TakeCheckpoint(TakeCheckpointIntent intent) => Sent.Add(intent);
    public void RestoreCheckpoint(RestoreCheckpointIntent intent) => Sent.Add(intent);
    public void SetRunSettings(SetRunSettingsIntent intent) => Sent.Add(intent);   // CE-4090 D14
    public void CheckUnsaved(CheckUnsavedIntent intent) => Sent.Add(intent);   // CE-4093 D20
    public void Cancel(CancelOperationIntent intent) { Cancels.Add(intent); Sent.Add(intent); }
    public void Dump(ExecuteDiagnosticDumpIntent intent) => Sent.Add(intent);
}
