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
            ITimeCommands?       commands = null)
        {
            _preview  = preview  ?? throw new System.ArgumentNullException(nameof(preview));
            _timeCtrl = timeCtrl ?? throw new System.ArgumentNullException(nameof(timeCtrl));
            _world    = world    ?? throw new System.ArgumentNullException(nameof(world));
            _commands = commands;
        }

        private bool InPreview => _preview.IsInPreviewMode;
        private bool Paused    => _timeCtrl.GetMode() == TimeMode.Deterministic;

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
        public bool IsPaused => !InPreview || Paused;

        /// <inheritdoc/>
        public double TotalTime => _world.HasSingleton<GlobalTime>()
            ? _world.GetSingleton<GlobalTime>().TotalTime
            : 0.0;

        /// <inheritdoc/>
        public float TimeScale => _timeCtrl.GetTimeScale();

        /// <inheritdoc/>
        public void TogglePlayPause()
        {
            if (!InPreview)
                _preview.EnterPreviewMode();   // entering preview is not time control
            else if (Paused)
                Resume();
            else
                Pause();
        }

        /// <inheritdoc/>
        public void Step()
        {
            if (!InPreview)
                _preview.EnterPreviewMode(startPaused: true);
            else if (!Paused)
                Pause();
            else if (_commands != null)
                _commands.StepOneTick();
            else
                _timeCtrl.Step(1f / 60f);
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
