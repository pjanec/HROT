using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.Common.Diagnostics.Gizmos
{
    // Keys are FNV-1a hashes of the strings below; computed via GizmoSettingsRegistry.ComputeHash().
    internal static class SpatialGridGizmoSettings
    {
        public const string ShowTilesKey  = "SpatialGrid.ShowTiles";
        public const string ShowCountsKey = "SpatialGrid.ShowCounts";

        public static readonly GizmoSettingValue DefaultShowTiles  = GizmoSettingValue.From(true);
        public static readonly GizmoSettingValue DefaultShowCounts = GizmoSettingValue.From(false);

        public static void Register(GizmoSettingsRegistry settings)
        {
            settings.RegisterSetting(ShowTilesKey,  DefaultShowTiles);
            settings.RegisterSetting(ShowCountsKey, DefaultShowCounts);
        }
    }
}
