using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-3133</c> — which baked navmesh the map's Navmesh layer draws (R-233: default Infantry).</summary>
    public enum NavmeshDrawLayers
    {
        Infantry = 0,
        Vehicle = 1,
        All = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3133</c> — the Navmesh layer's setting (<c>map.navmesh.layers</c> in the <see cref="GizmoSettingsRegistry"/>, an int as
    /// <see cref="GizmoFamilies"/> keeps its scopes): read by <c>NavmeshGizmo</c>, written by the layer panel.
    /// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5c N3.
    /// </summary>
    public static class NavmeshLayerSetting
    {
        public const string Key = "map.navmesh.layers";
        public static readonly uint Hash = GizmoSettingsRegistry.ComputeHash(Key);
        public const NavmeshDrawLayers Default = NavmeshDrawLayers.Infantry;

        /// <summary>Registers the setting with its default (idempotent).</summary>
        public static void Register(GizmoSettingsRegistry settings) => settings.RegisterSetting(Key, GizmoSettingValue.From((int)Default));

        /// <summary>The current choice (the default when there is no registry or no value).</summary>
        public static NavmeshDrawLayers Of(GizmoSettingsRegistry? settings)
        {
            if (settings == null) return Default;
            var v = settings.Read(Hash);
            return v.Type == SettingType.CsInt32 && v.IntValue is >= 0 and <= 2 ? (NavmeshDrawLayers)v.IntValue : Default;
        }

        public static void Set(GizmoSettingsRegistry settings, NavmeshDrawLayers layers) => settings.Write(Hash, GizmoSettingValue.From((int)layers));
    }
}
