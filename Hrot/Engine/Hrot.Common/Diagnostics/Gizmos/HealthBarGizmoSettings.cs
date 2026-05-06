using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.Common.Diagnostics.Gizmos
{
    // Keys are FNV-1a hashes of the strings below; computed via GizmoSettingsRegistry.ComputeHash().
    public static class HealthBarGizmoSettings
    {
        public const string BarHeightKey = "HealthBar.BarHeight";
        public const string BarWidthKey  = "HealthBar.BarWidth";

        public static readonly GizmoSettingValue DefaultBarHeight = GizmoSettingValue.From(6f);  // pixels
        public static readonly GizmoSettingValue DefaultBarWidth  = GizmoSettingValue.From(40f); // pixels

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(BarHeightKey, DefaultBarHeight);
            settings.RegisterSetting(BarWidthKey,  DefaultBarWidth);
        }
    }
}
