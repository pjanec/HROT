using System;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// Bridges the gizmo settings system and the <see cref="GlobalGizmoManager"/>.
    /// Registers a <see cref="MeasureGizmo"/> when the Active setting is true;
    /// unregisters it when the setting turns false.
    /// Call <see cref="Update"/> once per frame from IgApplication.
    /// </summary>
    internal sealed class MeasureToolGizmoAdapter
    {
        private readonly GlobalGizmoManager    _manager;
        private readonly GizmoSettingsRegistry _settings;

        private readonly uint _activeHash;
        private readonly uint _unitsHash;

        private bool         _wasActive;
        private long?        _activeId;
        private MeasureGizmo? _activeGizmo;

        public MeasureToolGizmoAdapter(
            GlobalGizmoManager    manager,
            GizmoSettingsRegistry settings)
        {
            _manager  = manager   ?? throw new ArgumentNullException(nameof(manager));
            _settings = settings  ?? throw new ArgumentNullException(nameof(settings));
            _activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            _unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);
        }

        /// <summary>Exposes the active gizmo for unit-test assertions.</summary>
        internal MeasureGizmo? TestHook_ActiveGizmo => _activeGizmo;

        /// <summary>
        /// Call once per frame (from IgApplication.Update) before canvas.Update().
        /// </summary>
        public void Update()
        {
            bool active = _settings.Read(_activeHash).BoolValue;

            if (active && !_wasActive)
            {
                long id = GlobalGizmoManager.NewId();
                var gizmo = new MeasureGizmo(onRemove: () =>
                {
                    _activeId    = null;
                    _activeGizmo = null;
                    _wasActive   = false;
                });
                SyncUnits(gizmo);
                _activeId    = id;
                _activeGizmo = gizmo;
                _manager.Register(id, gizmo);
            }
            else if (!active && _wasActive)
            {
                if (_activeId.HasValue)
                {
                    _manager.Unregister(_activeId.Value);
                    _activeId    = null;
                    _activeGizmo = null;
                }
            }
            else if (active && _wasActive)
            {
                // Refresh units every frame in case they changed.
                if (_activeGizmo != null)
                    SyncUnits(_activeGizmo);
            }

            _wasActive = active;
        }

        private void SyncUnits(MeasureGizmo gizmo)
        {
            int units = _settings.Read(_unitsHash).IntValue;
            gizmo.DisplayUnits = units == 1 ? MeasureDisplayUnits.Kilometers : MeasureDisplayUnits.Meters;
        }
    }
}
