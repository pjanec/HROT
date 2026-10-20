using System;
using System.IO;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ CE-1203 FL-9 (docs/DESIGN_Terrain_Combat_Tuning.md §5e) — the operator's map layers, remembered: the shared
    /// <see cref="GizmoSettingsRegistry"/>'s Global settings (both layer sets <c>map.layers.*</c>, the family scopes <c>map.scope.*</c>, the
    /// navmesh choice) in ONE per-user file beside <c>audio.json</c> (<see cref="LayoutPaths.UserDirectory"/>), with the registry's own
    /// SaveToDisk / LoadFromDisk — the save path gizmos-1 built (GZ008, Global = "a global user preferences file") and nothing called.
    /// Loaded when a map host builds its map (<see cref="MapInteractionPack.Build"/>), saved on every change. One file per machine, shared
    /// by the editor, SimHost, CGF and IG. A missing or unreadable file gives the defaults; a failed save is ignored — map preferences
    /// must never stop a host (as <see cref="Hrot.UI.Common.Audio.AudioSettings"/>).
    /// </summary>
    public static class MapLayerPreferences
    {
        public const string FileName = "map-layers.json";

        /// <summary>Where this user's file is (the folder is created); null when there is no user folder.</summary>
        public static string? DefaultPath()
        {
            try { return Path.Combine(LayoutPaths.UserDirectory(Hrot.UI.Common.Audio.AudioSettings.AppFolderName), FileName); }
            catch (Exception) { return null; }
        }

        /// <summary>The file's settings into <paramref name="settings"/>; nothing when the path is null or empty, or the file is missing or bad.</summary>
        public static void Load(GizmoSettingsRegistry settings, string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { settings.LoadFromDisk(path, SettingScope.Global); }
            catch (Exception) { }
        }

        /// <summary>Writes <paramref name="settings"/>' Global settings; false (and nothing thrown) when it cannot. Null or empty path saves nothing.</summary>
        public static bool Save(GizmoSettingsRegistry settings, string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { settings.SaveToDisk(path, SettingScope.Global); return true; }
            catch (Exception) { return false; }
        }
    }
}
