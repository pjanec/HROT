using System;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{

    /// <summary>
    /// ⭐ <c>CE-4061</c> (docs/DESIGN_Director_Timeline_Editor.md §11h) — what the editor's scenario verifier drives besides the film host:
    /// checkpoints (R1), a LIVE run and its recording (H1a), a review of it (H5), the product's replay of it (H1b). ⭐ Out of
    /// <see cref="IDirectorHost"/> so the film runner names none of it; the editor's host is both.
    /// </summary>
    public interface IVerifyHost
    {
        /// <summary>⭐ CE-4042 — this process has loaded a scenario already (by the director or the user): the next load is a RELOAD, and a
        /// reload is not where a golden may be recorded (CE-3294; Regression_Net §6 ④).</summary>
        bool HasLoadedAScenario { get; }

        /// <summary>⭐ <c>CE-3299</c> B5 (R1, restore design §1k) — writes the world as it is NOW as checkpoint <paramref name="label"/> at
        /// <paramref name="path"/> (a one-frame recording, <c>CheckpointFile</c>).</summary>
        void WriteCheckpoint(string path, string label) => throw new NotSupportedException("this host cannot write a checkpoint");

        /// <summary>⭐ B5 — puts the checkpoint at <paramref name="path"/> back (the cluster restore's own <c>PutBack</c>) and asks the
        /// clock to its mark (D5; the clock moves on a later frame). Returns the mark's sim time.</summary>
        double RestoreCheckpoint(string path) => throw new NotSupportedException("this host cannot restore a checkpoint");

        /// <summary>⭐ <c>CE-3316</c> — a slot-paced background run is in flight (the kernel's <c>SlotRunInFlight</c>): R1 writes its checkpoint
        /// on a step where this is false, as the recorder does.</summary>
        bool SlotRunInFlight => false;

        // ── ⭐ CE-4045 H1a (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) — a LIVE run and its recording ──────────────────────

        /// <summary>Loads <paramref name="scenario"/> LIVE through the cluster master, paused at 0 (the <c>POST /scenario/load/live</c>
        /// load) — a live run records (R-3053, R-4000); a preview never does.</summary>
        void LoadLive(string scenario) => throw new NotSupportedException("this host cannot load a live run");

        bool IsOperatingLive => false;

        /// <summary>Ends the live run (the master: Idle) — the recorder finalises and closes its file.</summary>
        void StopLive() => throw new NotSupportedException("this host cannot stop a live run");

        bool IsIdle => false;

        /// <summary>The file the live run records into while it records; null when nothing records (before, or once it is closed).</summary>
        string? LiveRecordingPath => null;

        /// <summary>Opens a finished recording for playback (the product's player, in this process).</summary>
        IRecordedRun OpenRecording(string path) => throw new NotSupportedException("this host cannot play a recording");

        // ── ⭐ CE-4045 H5 (docs/DESIGN_Scenario_Regression.md §10-3) — a During Action Review of the live run, through the master ────

        /// <summary>Reviews the live run in place, at <paramref name="simSeconds"/> of its own clock (DAR — <c>OperatingReview</c>).</summary>
        void EnterReview(double simSeconds) => throw new NotSupportedException("this host cannot review");

        /// <summary>Seeks the review to <paramref name="simSeconds"/> of the run's clock.</summary>
        void SeekReview(double simSeconds) => throw new NotSupportedException("this host cannot review");

        /// <summary>Back to the live run, where it paused.</summary>
        void ExitReview() => throw new NotSupportedException("this host cannot review");

        bool IsOperatingReview => false;

        // ── ⭐ CE-4045 H1b (docs/DESIGN_Scenario_Regression.md §10-2 ②) — the product's cluster REPLAY of the finished run, through the master ──

        /// <summary>Enters a replay of the newest finished exercise (<see cref="Guid.Empty"/> — the master picks, R-3055) at
        /// <paramref name="simSeconds"/> of the recording's own clock (<c>OperatingReplay</c>). Seeking it is <see cref="SeekReview"/> (the same
        /// <c>SeekReplayIntent</c>); leaving it is <see cref="StopLive"/> (an Idle transition).</summary>
        void EnterReplay(double simSeconds) => throw new NotSupportedException("this host cannot replay");

        bool IsOperatingReplay => false;

        /// <summary>⭐ <c>CE-4045</c> H3 (docs/DESIGN_Scenario_Regression.md §10-6, RH-7) — from the replay, a new LIVE run from the moment it
        /// stands at (the master's D18 branch, <c>LiveBranchProcessManager</c>); <see cref="IsOperatingLive"/> says when it is there.</summary>
        void BranchLive() => throw new NotSupportedException("this host cannot branch a replay");
    }
}
