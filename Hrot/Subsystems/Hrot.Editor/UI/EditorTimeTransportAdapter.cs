using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.UI
{
    /// <summary>
    /// Implements <see cref="ITimeTransportFacade"/> for the Editor perspective by
    /// delegating to the editor-local <see cref="IPreviewController"/> and
    /// <see cref="MasterSyncController"/>.
    /// </summary>
    internal sealed class EditorTimeTransportAdapter : ITimeTransportFacade
    {
        private readonly IPreviewController   _preview;
        private readonly MasterSyncController _timeCtrl;
        private readonly EntityRepository     _world;

        internal EditorTimeTransportAdapter(
            IPreviewController   preview,
            MasterSyncController timeCtrl,
            EntityRepository     world)
        {
            _preview  = preview;
            _timeCtrl = timeCtrl;
            _world    = world;
        }

        private bool InPreview => _preview.IsInPreviewMode;
        private bool Paused    => _timeCtrl.GetMode() == TimeMode.Deterministic;

        /// <inheritdoc/>
        public bool IsPlayPauseEnabled => true;

        /// <inheritdoc/>
        public bool IsStepEnabled => InPreview && Paused;

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
            if (InPreview && Paused)
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
