using Fdp.Toolkit.Diagnostics.Gizmos.Settings;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ SR-191 (Hrot docs/DESIGN_Scenario_Macros.md §14.4) — the map's TRANSIENT LOOK setting (<c>map.entity.dimTransient</c>, a bool in the
    /// <see cref="GizmoSettingsRegistry"/>): an entity a scenario save never writes (<c>ScenarioIgnoreTag</c>) is drawn dimmed. Read by the
    /// entity presentation gizmo, written by the layer panel (its <i>Dim transient entities</i> row) and turned on by the scenario editor's map.
    /// Default OFF: in a run every entity is live. Beside <see cref="NavmeshLayerSetting"/>, which the panel edits the same way.
    /// </summary>
    public static class TransientLookSetting
    {
        public const string Key = "map.entity.dimTransient";
        public static readonly uint Hash = GizmoSettingsRegistry.ComputeHash(Key);
        public const bool Default = false;

        /// <summary>Registers the setting with its default (idempotent).</summary>
        public static void Register(GizmoSettingsRegistry settings) => settings.RegisterSetting(Key, GizmoSettingValue.From(Default));

        /// <summary>The current choice (the default when there is no registry).</summary>
        public static bool Of(GizmoSettingsRegistry? settings) => settings == null ? Default : settings.Read(Hash).BoolValue;

        public static void Set(GizmoSettingsRegistry settings, bool dim) => settings.Write(Hash, GizmoSettingValue.From(dim));
    }
}
