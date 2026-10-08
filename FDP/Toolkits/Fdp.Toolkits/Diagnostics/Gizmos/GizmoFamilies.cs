using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-3120</c> — which units a gizmo family draws for.</summary>
    public enum GizmoScope
    {
        /// <summary>Every unit that carries the gizmo's components.</summary>
        All = 0,
        /// <summary>Only the selected units and the units that have this family pinned.</summary>
        SelectedOrPinned = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-3120</c> (R-227) — the map gizmo families: each family's runtime scope setting (<c>map.scope.&lt;Family&gt;</c> in the
    /// <see cref="GizmoSettingsRegistry"/>) and its default. ONE table, read by the visibility policy and written by the layer panel.
    /// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
    /// </summary>
    public static class GizmoFamilies
    {
        /// <summary>The families a unit can be pinned by, in menu order.</summary>
        public static readonly AiOverlayFlags[] All =
        {
            AiOverlayFlags.Path, AiOverlayFlags.Perception, AiOverlayFlags.TargetMemory, AiOverlayFlags.Eqs,
            AiOverlayFlags.UtilityDecision, AiOverlayFlags.SquadAssignment,
        };

        /// <summary>The settings key of a family's scope.</summary>
        public static string SettingKey(AiOverlayFlags family) => "map.scope." + family;

        /// <summary>The hash of <see cref="SettingKey"/>.</summary>
        public static uint SettingHash(AiOverlayFlags family) => GizmoSettingsRegistry.ComputeHash(SettingKey(family));

        /// <summary>The scope a family starts with: the busy per-unit drawings show the selection, the rest show every unit.</summary>
        public static GizmoScope DefaultScope(AiOverlayFlags family) => family switch
        {
            AiOverlayFlags.Path or AiOverlayFlags.UtilityDecision or AiOverlayFlags.SquadAssignment => GizmoScope.SelectedOrPinned,
            _ => GizmoScope.All,
        };

        /// <summary>What the menu and the layer panel call a family.</summary>
        public static string Label(AiOverlayFlags family) => family switch
        {
            AiOverlayFlags.Path            => "Path",
            AiOverlayFlags.Perception      => "Perception",
            AiOverlayFlags.TargetMemory    => "Contacts",
            AiOverlayFlags.Eqs             => "EQS",
            AiOverlayFlags.UtilityDecision => "Utility",
            AiOverlayFlags.SquadAssignment => "Squad",
            _                              => family.ToString(),
        };

        /// <summary>Registers every family's scope setting with its default (idempotent).</summary>
        public static void Register(GizmoSettingsRegistry settings)
        {
            foreach (var f in All) settings.RegisterSetting(SettingKey(f), GizmoSettingValue.From((int)DefaultScope(f)));
        }

        /// <summary>A family's current scope (its default when the registry has no value).</summary>
        public static GizmoScope ScopeOf(GizmoSettingsRegistry? settings, AiOverlayFlags family)
        {
            if (settings == null) return DefaultScope(family);
            var v = settings.Read(SettingHash(family));
            return v.Type == SettingType.CsInt32 ? (GizmoScope)v.IntValue : DefaultScope(family);
        }

        /// <summary>Sets a family's scope.</summary>
        public static void SetScope(GizmoSettingsRegistry settings, AiOverlayFlags family, GizmoScope scope) =>
            settings.Write(SettingHash(family), GizmoSettingValue.From((int)scope));
    }
}
