using System;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Vis2D;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// Bridges the gizmo settings system and the MapCanvas tool stack.
    /// Pushes <see cref="MeasureTool"/> when the Active setting is true;
    /// pops it when the setting turns false.
    /// Call <see cref="Update"/> once per frame from IgApplication.
    /// </summary>
    internal sealed class MeasureToolGizmoAdapter
    {
        private readonly MapCanvas             _canvas;
        private readonly GizmoSettingsRegistry _settings;
        private readonly MeasureTool           _tool;

        private readonly uint _activeHash;
        private readonly uint _unitsHash;

        private bool _wasActive;

        public MeasureToolGizmoAdapter(
            MapCanvas canvas,
            GizmoSettingsRegistry settings)
        {
            _canvas   = canvas   ?? throw new ArgumentNullException(nameof(canvas));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tool     = new MeasureTool();
            _activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            _unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);
        }

        /// <summary>
        /// Call once per frame (from IgApplication.Update) before canvas.Update().
        /// </summary>
        public void Update()
        {
            bool active = _settings.Read(_activeHash).BoolValue;

            if (active && !_wasActive)
            {
                // Sync units before pushing.
                SyncUnits();
                _canvas.PushTool(_tool);
            }
            else if (!active && _wasActive)
            {
                // Only pop if our tool is the active one.
                if (_canvas.ActiveTool == _tool)
                    _canvas.PopTool();
            }
            else if (active && _wasActive)
            {
                // Refresh units every frame in case they changed.
                SyncUnits();
            }

            _wasActive = active;
        }

        private void SyncUnits()
        {
            int units = _settings.Read(_unitsHash).IntValue;
            _tool.DisplayUnits = units == 1 ? MeasureDisplayUnits.Kilometers : MeasureDisplayUnits.Meters;
        }
    }
}
