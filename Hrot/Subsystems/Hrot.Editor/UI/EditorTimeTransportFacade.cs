using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Time;
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

        /// <summary>
        /// Creates a new <see cref="EditorTimeTransportFacade"/>.
        /// </summary>
        /// <param name="preview">The editor's preview controller (must not be null).</param>
        /// <param name="timeCtrl">The editor's master sync time controller (must not be null).</param>
        /// <param name="world">The ECS world (must not be null).</param>
        public EditorTimeTransportFacade(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world)
        {
            _preview  = preview  ?? throw new System.ArgumentNullException(nameof(preview));
            _timeCtrl = timeCtrl ?? throw new System.ArgumentNullException(nameof(timeCtrl));
            _world    = world    ?? throw new System.ArgumentNullException(nameof(world));
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
                _preview.EnterPreviewMode();
            else if (Paused)
                _timeCtrl.SwitchToContinuous();
            else
                _timeCtrl.SwitchToDeterministic(new HashSet<int>());
        }

        /// <inheritdoc/>
        public void Step()
        {
            if (!InPreview)
                _preview.EnterPreviewMode(startPaused: true);
            else if (!Paused)
                _timeCtrl.SwitchToDeterministic(new HashSet<int>());
            else
                _timeCtrl.Step(1f / 60f);
        }

        /// <inheritdoc/>
        public void Stop()
        {
            if (InPreview)
                _preview.ExitPreviewMode();
        }

        /// <inheritdoc/>
        public void SetTimeScale(float scale) => _timeCtrl.SetTimeScale(scale);
    }
}
