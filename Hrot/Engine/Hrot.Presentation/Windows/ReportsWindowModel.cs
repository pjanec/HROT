using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Presentation.Windows;

/// <summary>⭐ CE-1191 (UI-6, DESIGN_Command_Display §6 D3) — the SPOT / CONTACT fields of one report row, as words.</summary>
public sealed record ReportSpot(string Size, int Count, string Activity, string Equipment, float X, float Y, float Z);

/// <summary>
/// ⭐ CE-1191 — one row of the Reports window: a report the node's staff inbox saw, already flat (names, text, numbers — no entity
/// handles, no enums), so the window draws it and the panel dump carries it as it is.
/// </summary>
/// <param name="Serial">The log's running number (1-based; the newest has the largest).</param>
/// <param name="SeenAt">When this node took the report in (sim s).</param>
/// <param name="Kind"><see cref="ReportKind"/> by name.</param>
/// <param name="Subtype">The PROGRESS / REQUEST subtype by name, else null (<see cref="CommandReportText.SubtypeName"/>).</param>
/// <param name="From">The sending unit's name.</param>
/// <param name="To">The addressed unit's name.</param>
/// <param name="Spot">SPOT / CONTACT only: the element fields.</param>
/// <param name="AgeSeconds"><c>now − ObservedAt</c>: the report ages from when the fact was OBSERVED, not from its arrival (CB-31).</param>
/// <param name="Synthetic">Faked over the debug HTTP (CC-14) — drawn with a SYN mark.</param>
/// <param name="Verdict">What the inbox did: "folded", "stale", "not-owned-here".</param>
public sealed record ReportRow(
    long Serial, double SeenAt, string Kind, string? Subtype, string From, string To,
    ReportSpot? Spot, float AgeSeconds, bool Synthetic, string Verdict)
{
    /// <summary>"kind" or "kind / subtype" — the kind column.</summary>
    [JsonIgnore] public string KindText => Subtype is null ? Kind : $"{Kind} / {Subtype}";

    /// <summary>The SPOT column: "SquadSize x6 Moving MachineGun @ (12, 34)"; empty for the kinds that carry none.</summary>
    [JsonIgnore]
    public string SpotText => Spot is null
        ? string.Empty
        : $"{Spot.Size} x{Spot.Count} {Spot.Activity}{(Spot.Equipment == nameof(ElementEquipment.NoEquipment) ? "" : " " + Spot.Equipment)} @ ({Spot.X:F0}, {Spot.Y:F0})";
}

/// <summary>A unit the log mentions, for the window's unit filter.</summary>
public readonly record struct ReportUnit(Entity Unit, string Name);

/// <summary>
/// ⭐⭐ CE-1191 (UI-6, <c>docs/DESIGN_Command_Display.md</c> §6 D3) — the Reports window's MODEL: the node's <see cref="SquadReportLog"/> as
/// rows, newest first, filtered by a unit (from OR to) and a kind. Pure — no ImGui — so the rails drive exactly what the window draws.
/// 📐 It READS the one log (<see cref="SquadReportLog.Peek"/>); a second log of the ui's own would drift from it (R-132). Words come from
/// <see cref="CommandReportText"/>, the same helper the debug HTTP uses.
/// </summary>
public static class ReportsWindowModel
{
    /// <summary>Shown when the node has no log, or an empty one. ⭐ D4 (behaviors, R-2058) — the inbox fills the log on every node now, so an
    /// empty log means no report has arrived here yet.</summary>
    public const string EmptyText = "No report on this node yet.";

    /// <summary>Shown when the log has reports but the filters keep none.</summary>
    public const string NoMatchText = "No report matches the filters.";

    /// <summary>The kinds the kind filter offers (everything but the empty slot).</summary>
    public static IReadOnlyList<ReportKind> Kinds { get; } =
        Enum.GetValues<ReportKind>().Where(k => k != ReportKind.NoReport).ToArray();

    /// <summary>
    /// The log's reports, NEWEST FIRST.
    /// </summary>
    /// <param name="world">Resolves unit names (<see cref="CommandReportText.UnitName"/>); null ⇒ every unit shows as <c>#index</c>.</param>
    /// <param name="log">The node's log; null or empty ⇒ no rows.</param>
    /// <param name="now">Sim seconds now (<see cref="CommandPictureOps.Now"/>) — the age is <c>now − ObservedAt</c>.</param>
    /// <param name="unitFilter">Keeps a report whose FROM or TO is this unit; null = all.</param>
    /// <param name="kindFilter">Keeps this kind only; null = all.</param>
    public static IReadOnlyList<ReportRow> Rows(
        EntityRepository? world, SquadReportLog? log, float now, Entity? unitFilter, ReportKind? kindFilter)
    {
        var rows = new List<ReportRow>();
        if (log == null) return rows;
        foreach (var e in log.Recent(SquadReportLog.Capacity))
        {
            if (kindFilter is { } k && e.Report.Kind != k) continue;
            if (unitFilter is { } u && !e.FromUnit.Equals(u) && !e.ToUnit.Equals(u)) continue;
            rows.Add(Row(world, e, now));
        }
        return rows;
    }

    /// <summary>The units the log mentions (sender or addressee), by name — what the unit filter offers.</summary>
    public static IReadOnlyList<ReportUnit> Units(EntityRepository? world, SquadReportLog? log)
    {
        var seen = new HashSet<Entity>();
        var units = new List<ReportUnit>();
        if (log != null)
            foreach (var e in log.Recent(SquadReportLog.Capacity))
                foreach (var unit in new[] { e.FromUnit, e.ToUnit })
                    if (!unit.IsNull && seen.Add(unit))
                        units.Add(new ReportUnit(unit, NameOf(world, unit)));
        units.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return units;
    }

    /// <summary>A unit as the UI names it: its <see cref="EntityInfo.Name"/>, else <c>#index</c>; a null entity is "—".</summary>
    public static string NameOf(EntityRepository? world, Entity unit)
    {
        if (unit.IsNull) return "—";
        var name = world == null ? null : CommandReportText.UnitName(world, unit);
        return string.IsNullOrEmpty(name) ? $"#{unit.Index}" : name;
    }

    private static ReportRow Row(EntityRepository? world, SquadReportLogEntry e, float now)
    {
        var r = e.Report;
        ReportSpot? spot = CommandReportText.CarriesElementFields(r)
            ? new ReportSpot(r.Size.ToString(), r.Count, r.Activity.ToString(), r.Equipment.ToString(), r.At.X, r.At.Y, r.At.Z)
            : null;
        return new ReportRow(
            e.Serial, e.SeenAt, r.Kind.ToString(), CommandReportText.SubtypeName(r),
            NameOf(world, e.FromUnit), NameOf(world, e.ToUnit),
            spot, now - r.ObservedAt, r.Synthetic != 0, e.Verdict);
    }
}
