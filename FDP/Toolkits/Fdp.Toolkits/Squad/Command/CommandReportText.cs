using Fdp.Core;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐ CE-1191 (UI-6, DESIGN_Command_Display §6 D3) — how a <see cref="CommandReport"/> and the units it names are put into WORDS: the
    /// subtype text, which kinds carry SPOT fields, a unit's display name. ONE copy, read by the debug HTTP (<c>GET /squad/reports</c>,
    /// <c>GET /entities/{id}/reports</c>) and by the Reports window — it used to be private to the HTTP route, and a second copy in the
    /// window would drift (R-132).
    /// </summary>
    public static class CommandReportText
    {
        /// <summary>The subtype's NAME: a <see cref="ProgressKind"/> for PROGRESS, a <see cref="RequestKind"/> for REQUEST, else <c>null</c>.</summary>
        public static string? SubtypeName(in CommandReport r)
            => r.Kind == ReportKind.Progress ? ((ProgressKind)r.Subtype).ToString()
             : r.Kind == ReportKind.Request ? ((RequestKind)r.Subtype).ToString()
             : null;

        /// <summary>SPOT and CONTACT carry the element fields (size, count, activity, equipment, radius).</summary>
        public static bool CarriesElementFields(in CommandReport r) => r.Kind is ReportKind.Spot or ReportKind.Contact;

        /// <summary>The unit's <see cref="EntityInfo.Name"/>; <c>null</c> when it is gone or carries no <see cref="EntityInfo"/>
        /// (an empty name is returned as is — the caller decides what to show instead).</summary>
        public static string? UnitName(EntityRepository world, Entity unit)
            => !unit.IsNull && world.IsAlive(unit) && world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(unit)
                ? world.GetComponentRO<EntityInfo>(unit).Name.ToString()
                : null;
    }
}
