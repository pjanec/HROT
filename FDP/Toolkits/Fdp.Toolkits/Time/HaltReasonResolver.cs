namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// Resolves <see cref="HaltReason"/> from the authorities that each own one part of the answer.
    ///
    /// <para>Deliberately a PURE function over explicit inputs, with no defaults. Every argument must
    /// be supplied by whoever calls it, so a caller cannot silently omit a signal it actually had —
    /// that omission is the recurring defect in this area, and an optional parameter with a
    /// convenient default is how it happens.</para>
    ///
    /// <para>No single object can answer this: the kernel knows whether it is publishing, the
    /// debugger knows whether it has rewound, and the time controller knows whether it is stepping.
    /// Rather than latch those into a thirteenth cached flag, the resolver reads them at the moment
    /// of the question and combines them in a fixed order.</para>
    /// </summary>
    public static class HaltReasonResolver
    {
        /// <summary>
        /// Combines the signals in a precedence that is itself load-bearing — see the ordering note
        /// on each branch.
        /// </summary>
        /// <param name="isPublishing">
        /// Is the kernel still pushing the clock singleton? <c>false</c> during replay preparation.
        /// </param>
        /// <param name="isAdvancing">The clock's own answer — <c>DeltaTime &gt; 0</c>.</param>
        /// <param name="isRewound">Has a breakpoint rewound the live repository?</param>
        /// <param name="isAwaitingStepAcks">Is a deterministic step issued and unacknowledged?</param>
        /// <param name="isDeterministic">Is the controller in deterministic (stepping) mode?</param>
        /// <param name="isPauseBarrierPending">
        /// Is a pause broadcast but its Future Barrier not yet crossed (`T7`)? A separate signal
        /// from <paramref name="isDeterministic"/> on purpose: <c>GetMode()</c> answers
        /// <c>Continuous</c> for that whole window, so a caller that had only the mode could not
        /// distinguish the window from a genuinely unexplained halt — and did not, which is how
        /// <see cref="HaltReason.Unknown"/> came to be reachable in normal operation.
        /// </param>
        public static HaltReason Resolve(
            bool isPublishing,
            bool isAdvancing,
            bool isRewound,
            bool isAwaitingStepAcks,
            bool isDeterministic,
            bool isPauseBarrierPending)
        {
            // FIRST, and not negotiable: while the push is suspended the singleton is frozen at its
            // last value, which may carry a non-zero delta. Asking "is it advancing" before this
            // would answer Running while four system groups sit disabled. This ordering is the
            // difference between reporting the truth and repeating the clock's stale word for it.
            if (!isPublishing) return HaltReason.NotPublishing;

            if (isAdvancing) return HaltReason.Running;

            // The debugger owns the world while rewound, including over any step state below.
            if (isRewound) return HaltReason.HeldByBreakpoint;

            // Mid-step outranks "paused": both are deterministic mode, but only one is waiting on
            // something, and that is the distinction a surface needs in order to say why.
            if (isAwaitingStepAcks) return HaltReason.SteppingHeld;

            if (isDeterministic) return HaltReason.PausedByOperator;

            // Below deterministic, because once the barrier HAS landed the mode is the better
            // answer; above Unknown, because this is the state Unknown was wrongly covering.
            if (isPauseBarrierPending) return HaltReason.PauseBarrierPending;

            // Halted, publishing, continuous, nothing holding it. Nothing here explains that, and
            // saying so is more useful than picking the nearest plausible answer.
            return HaltReason.Unknown;
        }
    }
}
