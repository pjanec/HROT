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
        /// <summary>⭐ <c>CE-1329</c> — only the units that have this family pinned (the Brain tab's <i>Show on map</i> ticks, the right-click
        /// <i>Pin gizmos</i>); selecting a unit draws none of it.</summary>
        PinnedOnly = 2,
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
            AiOverlayFlags.Channels,   // ⭐ CE-3136 — the action status (why a unit is not firing)
            AiOverlayFlags.CommandPicture, AiOverlayFlags.SquadContacts,   // ⭐ CE-1317 BV-50 — split out of Squad
        };

        /// <summary>The settings key of a family's scope.</summary>
        public static string SettingKey(AiOverlayFlags family) => "map.scope." + family;

        /// <summary>The hash of <see cref="SettingKey"/>.</summary>
        public static uint SettingHash(AiOverlayFlags family) => GizmoSettingsRegistry.ComputeHash(SettingKey(family));

        /// <summary>The scope a family starts with. ⭐ CE-1065 (docs/DESIGN_Terrain_Combat_Tuning.md §5d) — EVERY family now starts on the
        /// selected and pinned units: Perception (a vision cone per unit) and Contacts (a line per remembered contact) drew for every
        /// unit and, with the rest, buried the scene (🔒 user, 2026-10-14). ⛔ SUPERSEDED: §5b B's "Perception, Contacts = All".</summary>
        /// <remarks>⭐⭐ <c>CE-1329</c> (docs/DESIGN_Entity_Brain_View.md §12r BV-66; 🔒 user, 2026-10-25: <i>"clicking a unit no longer shows its
        /// vision cone until you tick Perception — this is exactly what I want"</i>) — every BRAIN family is <see cref="GizmoScope.PinnedOnly"/>:
        /// the unit's own tick is the one switch (selection drew all of them at once, so the tick could add nothing). Only
        /// <see cref="AiOverlayFlags.Path"/> (routes and mission lines, not on the Brain tab — R-1038) keeps "selected or pinned" and its
        /// layer-panel scope.</remarks>
        public static GizmoScope DefaultScope(AiOverlayFlags family) => IsBrainFamily(family) ? GizmoScope.PinnedOnly : GizmoScope.SelectedOrPinned;

        /// <summary>⭐ <c>CE-1329</c> — a family the Brain tab's <i>Show on map</i> owns (every family but <see cref="AiOverlayFlags.Path"/>).</summary>
        public static bool IsBrainFamily(AiOverlayFlags family) => family != AiOverlayFlags.None && family != AiOverlayFlags.Path;

        /// <summary>What the menu and the layer panel call a family.</summary>
        public static string Label(AiOverlayFlags family) => family switch
        {
            AiOverlayFlags.Path            => "Path",
            AiOverlayFlags.Perception      => "Perception",
            AiOverlayFlags.TargetMemory    => "Contacts",
            AiOverlayFlags.Eqs             => "EQS",
            AiOverlayFlags.UtilityDecision => "Utility",
            AiOverlayFlags.SquadAssignment => "Squad",
            AiOverlayFlags.Channels        => "Actions",
            AiOverlayFlags.CommandPicture  => "Command picture",
            AiOverlayFlags.SquadContacts   => "My units' contacts",
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
            // ⭐ CE-1329 — a brain family has no scope setting any more (its "selected only" row is gone): a value saved before is ignored
            if (settings == null || IsBrainFamily(family)) return DefaultScope(family);
            var v = settings.Read(SettingHash(family));
            return v.Type == SettingType.CsInt32 ? (GizmoScope)v.IntValue : DefaultScope(family);
        }

        /// <summary>Sets a family's scope.</summary>
        public static void SetScope(GizmoSettingsRegistry settings, AiOverlayFlags family, GizmoScope scope) =>
            settings.Write(SettingHash(family), GizmoSettingValue.From((int)scope));
    }
}
