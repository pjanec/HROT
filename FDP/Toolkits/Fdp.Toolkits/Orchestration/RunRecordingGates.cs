using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-4090</c> (AQ108 D14, D22, §5a) — the TEXTS the run-recording gates answer with, in ONE place: the master's refusal
    /// (<c>ClusterMaster</c>), every host's HTTP 409 (<c>DebugApiService</c> → <c>DebugApiHost</c>) and the menu's grey-out read the same
    /// sentence, so a gate cannot be refused with one reason and explained with another.
    /// </summary>
    public static class RunRecordingGates
    {
        /// <summary>Why a checkpoint, a restore, a review (DAR), a replay / keep of the last run is refused: the run started with <i>Record live runs</i> off.</summary>
        public const string NotRecordedReason = "this run was not recorded";

        /// <summary>Why <c>POST /scenario/save</c> answers 409 in Live and AAR: that world is not the scenario.</summary>
        public const string SaveRefusedInRunReason =
            "a live run or a recording does not save the scenario: take a checkpoint, then make a scenario from it (or return to editing)";

        /// <summary>True when <paramref name="error"/> is one of the gate refusals above — the HTTP layer answers those with 409 (a state
        /// conflict), not 400 (a malformed request): the same request is fine in another state.</summary>
        public static bool IsConflict(string? error)
            => error != null
            && (error.StartsWith(NotRecordedReason, StringComparison.Ordinal)
             || error.StartsWith(SaveRefusedInRunReason, StringComparison.Ordinal));
    }
}
