using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    /// <summary>Published when a gizmo setting value changes via <see cref="GizmoSettingsRegistry.Write"/>.</summary>
    [EventId(8050)]
    public struct GizmoSettingChangedEvent
    {
        public uint KeyHash;
    }
}
