using System;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐ ui's REQUEST (AQ108 §0 R2″ — <i>Restart from a checkpoint ▸</i> in Live · ended): <b>the checkpoints of the exercise NOW recording</b>, held by the master —
/// the one place every checkpoint round passes (<see cref="StartCheckpoint"/>) — and published on the replay-session topic while the cluster is Live
/// (<c>ReplaySessionProcessManager</c>: an INACTIVE session carrying <see cref="ReplaySession.Checkpoints"/>), so every node's <c>ClusterView.ReplaySession</c>
/// lists them. ⛔ No second wire: the topic already carries a replay's marks (CE-3329 §9f).
/// </summary>
/// <remarks>
/// ⭐ The list mirrors what the recording holds: a mark is added when a checkpoint round SUCCEEDS on every node; a fresh live run starts empty; a restore to X
/// keeps the marks up to and including X — the new exercise's recording inherits exactly those (<c>RecordingModule.StartBranch</c>: <c>m.Frame &lt; branch.FrameCount</c>).
/// ⭐ A new array per change (readers compare the reference; 0 B per read).
/// </remarks>
public sealed partial class ClusterMaster
{
    private CheckpointMark[] _liveCheckpoints = Array.Empty<CheckpointMark>();

    /// <summary>The checkpoints of the exercise the cluster is recording now (Live / DAR), oldest first; empty outside a live run.</summary>
    public CheckpointMark[] LiveCheckpoints => _liveCheckpoints;

    /// <summary>A checkpoint round succeeded: its mark joins the live list (Live or DAR only — a replay's marks are the recording's own).</summary>
    private void NoteLiveCheckpoint(CheckpointPayload taken)
    {
        if (_currentDsmState is not (ClusterState.OperatingLive or ClusterState.OperatingReview)) return;
        var next = new CheckpointMark[_liveCheckpoints.Length + 1];
        Array.Copy(_liveCheckpoints, next, _liveCheckpoints.Length);
        next[^1] = new CheckpointMark { Label = taken.Label, WallTicks = taken.AtWallTicks };
        _liveCheckpoints = next;
    }

    /// <summary>A fresh live run (a load or a branch from a replay) starts with no checkpoints.</summary>
    private void ResetLiveCheckpoints() { _liveCheckpoints = Array.Empty<CheckpointMark>(); _checkpointLabelsAsked.Clear(); }

    /// <summary>⭐ CE-1467 (e) — every label this run's checkpoint rounds were STARTED with (taken or still in flight): two stations asking for one label in the same moment both
    /// see the live list without it, so the list alone cannot keep them apart.</summary>
    private readonly System.Collections.Generic.HashSet<string> _checkpointLabelsAsked = new(StringComparer.Ordinal);

    /// <summary>⭐ CE-1467 (e) — <paramref name="asked"/> when no checkpoint of this run has it, else <c>asked (2)</c>, <c>(3)</c> … — the master is the ONE writer, so the label it
    /// answers is unique by construction (the recording refuses a second checkpoint with a taken name).</summary>
    internal string UniqueCheckpointLabel(string asked)
    {
        bool Taken(string l) => _checkpointLabelsAsked.Contains(l) || Array.Exists(_liveCheckpoints, m => m.Label == l);
        if (!Taken(asked)) return asked;
        for (int n = 2; ; n++) { var l = $"{asked} ({n})"; if (!Taken(l)) return l; }
    }

    /// <summary>A restore to <paramref name="label"/>: the marks up to and including it stay (the new exercise inherits them); later ones go. An unknown label keeps the list.</summary>
    private void TruncateLiveCheckpointsAt(string label)
    {
        int at = Array.FindIndex(_liveCheckpoints, m => m.Label == label);
        if (at < 0 || at == _liveCheckpoints.Length - 1) return;
        _liveCheckpoints = _liveCheckpoints[..(at + 1)];
    }
}
