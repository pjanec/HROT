using System;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Time
{
    /// <summary>
    /// <b>The staged-write drain (`W2`).</b> Runs in <see cref="SystemPhase.PreFrame"/>, before
    /// <c>Input</c>, and applies whatever the editor has staged — but only on a frame that is
    /// actually advancing.
    ///
    /// <para><b>It is a PULL, not a release event.</b> The loop asks every frame whether anything is
    /// waiting; nobody has to remember to raise anything. A release event would have to be raised by
    /// each of resume, step and continue, and the one path that forgot would drop the edit with no
    /// symptom — which is the class of defect this whole area keeps producing.</para>
    ///
    /// <para><b>Why it must not restore.</b> While a breakpoint holds the pre-tick snapshot as the
    /// active view, this system SKIPS. Moving the restore out of <c>RequestStep</c>/
    /// <c>RequestContinue</c> is a separate task in the other lane, and draining into a rewound
    /// repository would write bytes that the restore then overwrites.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PreFrame)]
    public sealed class ResumeAndDrainSystem : IEcsModuleSystem
    {
        private readonly IStagedWrites _staged;
        private readonly Func<bool>?  _isPublishing;

        /// <param name="staged">The staged-write seam to drain.</param>
        /// <param name="isPublishing">
        /// Optional probe for the kernel's <c>IsPublishingGlobalTime</c>. Supply it and the drain
        /// also skips while the clock push is suspended.
        ///
        /// <para>This closes a residual that was previously named-but-accepted: replay preparation
        /// disables four system groups and suspends the push, but a PreFrame system is in none of
        /// those groups and the delta parameter still reads as advancing — so a staged edit could be
        /// drained into a world the replay was about to overwrite. The edit was lost, not corrupted,
        /// and only if a replay started between the edit and the resume. It is cheap to close now
        /// that the kernel exposes the read.</para>
        ///
        /// <para>Optional because a host with no kernel to ask is a legitimate caller; when it is
        /// omitted the behaviour is exactly what it was before.</para>
        /// </param>
        public ResumeAndDrainSystem(IStagedWrites staged, Func<bool>? isPublishing = null)
        {
            _staged       = staged ?? throw new ArgumentNullException(nameof(staged));
            _isPublishing = isPublishing;
        }

        /// <summary>
        /// Number of frames on which a drain actually happened. Exists so a caller can tell
        /// "nothing was staged" apart from "the drain never ran" — the two look identical from
        /// outside, and telling them apart is the whole difficulty of this area.
        /// </summary>
        public long DrainCount { get; private set; }

        /// <inheritdoc />
        public void Execute(ISimulationView view, float deltaTime)
        {
            // The delta PARAMETER, deliberately. The scheduler is handed GlobalTime.DeltaTime for
            // this frame, so it is the same number the clock singleton carries — but asking a time
            // CONTROLLER instead would be wrong: GetCurrentState() builds its state with a
            // hard-coded zero delta and therefore reports "halted" on every frame, including the
            // running ones.
            if (deltaTime <= 0f) return;

            // Replay preparation halts the world without zeroing this parameter, so the delta alone
            // cannot tell us the world is about to be overwritten. Ask the kernel when we can.
            if (_isPublishing != null && !_isPublishing()) return;

            // A breakpoint has rewound the live repository. Its own resume path owns the restore;
            // anything written here would be overwritten by it.
            if (_staged.IsRewound) return;

            if (!_staged.HasPending) return;

            _staged.DrainInto(view);
            DrainCount++;
        }
    }
}
