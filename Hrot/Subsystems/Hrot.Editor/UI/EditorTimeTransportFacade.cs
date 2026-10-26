using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.UI
{
    /// <summary>
    /// Public <see cref="ITimeTransportFacade"/> for the Editor, adapting the editor's
    /// <see cref="IPreviewController"/> and <see cref="MasterSyncController"/>.
    /// Used by <see cref="Hrot.UI.Common.Panels.MainToolbarTimeControlSection"/> in the
    /// main toolbar time-control group (BATCH-24).
    /// </summary>
    public sealed class EditorTimeTransportFacade : ITimeTransportFacade
    {
        private readonly IPreviewController   _preview;
        private readonly MasterSyncController _timeCtrl;
        private readonly EntityRepository     _world;

        // T4: the toolbar STATES what it wants and lets the node's drainer honour it, instead of
        // reaching into the controller. Optional so the status-bar/test paths that only read state
        // keep working; when absent the old direct calls are used, which is correct for a host that
        // has no bus but would leave the cluster uninformed on one that does.
        private readonly ITimeCommands?       _commands;

        // ⭐ CE-4051 / CE-4052 — whether a replay is on: the world's GlobalTime is then the RECORDED frame's (playback writes it), so the
        //   clock this facade drives is the master's — the replay clock (R-3056: seconds after the replay's zero).
        private readonly System.Func<bool>?   _isReplaying;

        /// <summary>
        /// Creates a new <see cref="EditorTimeTransportFacade"/>.
        /// </summary>
        /// <param name="preview">The editor's preview controller (must not be null).</param>
        /// <param name="timeCtrl">The editor's master sync time controller (must not be null).</param>
        /// <param name="world">The ECS world (must not be null).</param>
        /// <param name="commands">
        /// The intent-publishing command surface (`T4`). When supplied, pause/resume/step are
        /// PUBLISHED rather than applied directly, which is what makes the toolbar behave like
        /// every other node's control path. Optional: a caller that only reads state need not
        /// supply it.
        /// </param>
        public EditorTimeTransportFacade(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world,
            ITimeCommands?       commands = null,
            System.Func<bool>?   isReplaying = null)
        {
            _preview  = preview  ?? throw new System.ArgumentNullException(nameof(preview));
            _timeCtrl = timeCtrl ?? throw new System.ArgumentNullException(nameof(timeCtrl));
            _world    = world    ?? throw new System.ArgumentNullException(nameof(world));
            _commands = commands;
            _isReplaying = isReplaying;
        }

        private bool InPreview => _preview.IsInPreviewMode;

        private bool Replaying => _isReplaying?.Invoke() == true;

        /// <summary>
        /// `T7`: was <c>GetMode() == Deterministic</c>, which is LATE by the Future Barrier window.
        /// The master freezes its sim time the instant <c>SwitchToDeterministic</c> is called but
        /// keeps answering <c>Continuous</c> until the barrier lands (200 ms by default), so for
        /// that window the toolbar still showed a pause button on an already-stopped clock — and, on
        /// the same node, disagreed with <c>ClusterUiCache.IsPaused</c>, which folds the event and
        /// turns over immediately.
        ///
        /// <para>Asking <see cref="HaltReasonResolver"/> instead of widening the mode check is the
        /// point: it is the one place that knows the barrier window is a pause, and using it here
        /// gives `T6`'s resolver its first production caller.</para>
        /// </summary>
        private bool Paused => Reason is HaltReason.PausedByOperator
                                      or HaltReason.SteppingHeld
                                      or HaltReason.PauseBarrierPending;

        /// <summary>
        /// Why time is stopped, resolved fresh on every read (`T6`/`R-126` — derived, never latched).
        /// </summary>
        public HaltReason Reason => HaltReasonResolver.Resolve(
            isPublishing:          true,   // the editor host does not suspend its own clock push
            // ⭐ CE-4051 — in a replay the world's clock is the RECORDED frame's: a stepped frame carries DeltaTime 1/60, so it read "running"
            //   while the master stood paused, and every step became a Pause() that never landed. ⇒ the replay clock is the master's mode.
            isAdvancing:           Replaying
                                       ? _timeCtrl.GetMode() == TimeMode.Continuous && !_timeCtrl.IsPauseBarrierPending
                                       : SimClock.Of(_world).IsAdvancing,
            isRewound:             false,  // the breakpoint's rewind is `W5`, not wired here yet
            isAwaitingStepAcks:    _timeCtrl.IsAwaitingStepAcks,
            isDeterministic:       _timeCtrl.GetMode() == TimeMode.Deterministic,
            isPauseBarrierPending: _timeCtrl.IsPauseBarrierPending);

        /// <inheritdoc/>
        public bool IsPlayPauseEnabled => true;

        /// <inheritdoc/>
        public bool IsStepEnabled => true;

        /// <inheritdoc/>
        public bool IsStopEnabled => InPreview;

        /// <inheritdoc/>
        // Not in preview: show play button (simulation not running).
        // In preview, paused: show play button.
        // In preview, running: show pause button.
        public bool IsPaused => Replaying ? Paused : !InPreview || Paused;

        /// <inheritdoc/>
        /// <remarks>⭐ CE-4052 — in a replay, the replay clock: the master's seconds after the replay's zero (R-3056), what
        /// <c>atSeconds</c> means. ⛔ Not the recorded frame's sim time, which stands still across the paused frames a live run recorded.</remarks>
        public double TotalTime => Replaying
            ? _timeCtrl.GetCurrentState().TotalTime
            : _world.HasSingleton<GlobalTime>()
                ? _world.GetSingleton<GlobalTime>().TotalTime
                : 0.0;

        /// <inheritdoc/>
        public float TimeScale => _timeCtrl.GetTimeScale();

        /// <inheritdoc/>
        public void TogglePlayPause()
        {
            if (Replaying)
            {
                if (Paused) Resume(); else Pause();   // a replay has no preview to enter: play / pause drive the replay clock
                return;
            }
            if (!InPreview)
                _preview.EnterPreviewMode();   // entering preview is not time control
            else if (Paused)
                Resume();
            else
                Pause();
        }

        /// <inheritdoc/>
        public void Step() => StepBy(1f / 60f);

        /// <inheritdoc/>
        public void StepBy(float seconds)
        {
            if (Replaying)
            {
                if (!Paused) Pause();
                else if (_commands != null) _commands.StepBy(seconds);
                else _timeCtrl.Step(seconds);
                return;
            }
            if (!InPreview)
                _preview.EnterPreviewMode(startPaused: true);
            else if (!Paused)
                Pause();
            else if (_commands != null)
                _commands.StepBy(seconds);
            else
                _timeCtrl.Step(seconds);
        }

        // ── T4: one place each, so the direct-call fallback is visible ───────────
        private void Pause()
        {
            if (_commands != null) _commands.Pause();
            else                   _timeCtrl.SwitchToDeterministic(new HashSet<int>());
        }

        private void Resume()
        {
            if (_commands != null) _commands.Resume();
            else                   _timeCtrl.SwitchToContinuous();
        }

        /// <inheritdoc/>
        public void Stop()
        {
            if (InPreview)
                _preview.ExitPreviewMode();
        }

        /// <inheritdoc/>
        public void SetTimeScale(float scale)
        {
            if (_commands != null) _commands.SetTimeScale(scale);
            else                   _timeCtrl.SetTimeScale(scale);
        }
    }
}
