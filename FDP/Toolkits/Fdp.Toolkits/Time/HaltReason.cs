namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// <b>Why</b> simulation time is not advancing — not merely <i>that</i> it isn't (`T6`).
    ///
    /// <para>This is the field whose absence produced the dozen scattered <c>IsPaused</c> flags. Each
    /// of them existed because some surface needed to answer a "why" question — is the debugger
    /// holding it? is the operator paused? is a step still waiting on the cluster? — and a bool could
    /// not carry the answer, so every surface invented its own. One enum answers all of them.</para>
    ///
    /// <para><b>Derived, never latched.</b> Each value is computed from the authority that owns it at
    /// the moment it is asked. A cached copy is exactly how the twelve got out of step with each
    /// other in the first place.</para>
    /// </summary>
    public enum HaltReason
    {
        /// <summary>Time is advancing. Nothing is holding it.</summary>
        Running = 0,

        /// <summary>
        /// The kernel is not publishing the clock singleton, so the world's <c>GlobalTime</c> is
        /// frozen at whatever it last held — which may carry a NON-ZERO delta.
        ///
        /// <para>Checked FIRST, before "is it advancing", and that ordering is the whole point: while
        /// the push is suspended the clock reports the last frame's delta, so asking "is it
        /// advancing" first would answer <c>Running</c> while nothing runs. Replay preparation does
        /// exactly this — it disables four system groups and suspends the push.</para>
        /// </summary>
        NotPublishing = 1,

        /// <summary>
        /// A breakpoint has rewound the live repository to its pre-tick snapshot. The debugger owns
        /// the world until it resumes.
        /// </summary>
        HeldByBreakpoint = 2,

        /// <summary>
        /// Stepping, with a step already issued and its acknowledgements still outstanding. The
        /// cluster is mid-step: not idle, but not free-running either.
        /// </summary>
        SteppingHeld = 3,

        /// <summary>Deterministic mode with no step in flight — the operator pressed pause.</summary>
        PausedByOperator = 4,

        /// <summary>
        /// The operator's pause has been broadcast and sim time is already frozen, but the cluster
        /// has not yet crossed the Future Barrier, so the controller is still nominally continuous
        /// (`T7`).
        ///
        /// <para>This value exists because <c>Unknown</c> showed up in a real, reachable state and
        /// the enum's own documentation said that when it does, the missing probe is the finding.
        /// It was: <c>GetMode()</c> hides <c>BarrierPending</c> by design, so for the whole 200 ms
        /// lookahead window every probe answered "nothing is holding it" while nothing ran.</para>
        ///
        /// <para>Reported separately from <see cref="PausedByOperator"/> rather than folded into it
        /// because the states differ where it matters for diagnosis: here the cluster has not
        /// converged yet, and a step issued now is queued until the barrier lands rather than
        /// executed.</para>
        /// </summary>
        PauseBarrierPending = 5,

        /// <summary>
        /// Time is not advancing and no probe accounted for it.
        ///
        /// <para>A real answer, not a placeholder: it means the halt has a cause nothing here can
        /// see, which is worth surfacing rather than guessing at. If this appears in practice, the
        /// missing probe is the finding.</para>
        /// </summary>
        Unknown = 255,
    }
}
