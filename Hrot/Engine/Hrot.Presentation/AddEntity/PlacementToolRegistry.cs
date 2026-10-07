using System.Text.Json;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.AddEntity;

/// <summary>Which placement tool a type's pick arms. 📄 docs/DESIGN_Add_Entity_Picker.md D4.</summary>
public enum PlacementToolKind
{
    /// <summary>One click per entity (<c>EntityPlacementGizmo</c>) — every physical type.</summary>
    Point,
    /// <summary>A drawn polygon (<c>PointSequenceGizmo</c>, area style).</summary>
    Area,
    /// <summary>A drawn polyline.</summary>
    Route,
    /// <summary>A drawn polygon born as <c>TkbEntityTypes.TerrainZone</c>.</summary>
    Zone,
    /// <summary>Listed but not placeable yet (shown disabled with its reason).</summary>
    None,
}

/// <summary>One map graphic the picker lists under <c>Map Graphics</c>: a type with no TKB master, so the
/// registry supplies its name, its tool and why it may be disabled.</summary>
/// <param name="IconName">The glyph the entity icon library draws for it (S5).</param>
public sealed record MapGraphicInfo(long TkbType, string Name, PlacementToolKind Tool, string IconName, string? DisabledReason = null);

/// <summary>
/// ⭐ <c>CE-1017</c> S1 — the small table that says which placement tool a TKB type arms and whether it has a side.
/// 📄 docs/DESIGN_Add_Entity_Picker.md D4/D6/D7.
///
/// <para>⭐ Keyed by TKB type in <c>Hrot.Presentation</c>, not a TKB field: tools are editor UI and the TKB is shared
/// sim data (D4, rejected alternative). Every type not listed here is a physical entity ⇒ <see cref="PlacementToolKind.Point"/>
/// with a side.</para>
///
/// <para>🔒 Every pick ARMS a tool — nothing is created directly (D4). <see cref="Arm"/> is the one place that maps a
/// kind onto <see cref="ISpawnController"/>, shared by the map menu and the spawn panels.</para>
/// </summary>
public static class PlacementToolRegistry
{
    /// <summary>The side-less map graphics, in the order the picker lists them.</summary>
    public static readonly IReadOnlyList<MapGraphicInfo> Graphics = new[]
    {
        new MapGraphicInfo(TkbEntityTypes.TacGraphic_Area,     "Area",         PlacementToolKind.Area,  "_area"),
        new MapGraphicInfo(TkbEntityTypes.TacGraphic_Route,    "Route",        PlacementToolKind.Route, "_route"),
        new MapGraphicInfo(TkbEntityTypes.TerrainZone,         "Terrain Zone", PlacementToolKind.Zone,  "_zone"),
        new MapGraphicInfo(TkbEntityTypes.TacGraphic_FireLine, "Fire Line",    PlacementToolKind.None,  "_fireline",
                           DisabledReason: "No placement tool yet."),
    };

    /// <summary>The tool a type arms: a listed graphic's own tool, else <see cref="PlacementToolKind.Point"/>.</summary>
    public static PlacementToolKind ToolFor(long tkbType)
    {
        foreach (var g in Graphics)
            if (g.TkbType == tkbType) return g.Tool;
        return PlacementToolKind.Point;
    }

    /// <summary>True for a type placed with a side (Friendly/Hostile/Neutral); false for the map graphics.</summary>
    public static bool HasSide(long tkbType)
    {
        foreach (var g in Graphics)
            if (g.TkbType == tkbType) return false;
        return true;
    }

    /// <summary>
    /// Arms the type's tool on <paramref name="spawn"/>. <paramref name="side"/> is used only by a side-bearing type
    /// (carried as <c>{"Affiliation": "FORCE_…"}</c>, the shape <c>SpawnerPanel</c> and the placement ghost read).
    /// Returns false when the type has no tool (<see cref="PlacementToolKind.None"/>) — nothing is armed.
    /// </summary>
    public static bool Arm(ISpawnController spawn, long tkbType, eForceIdentifier side)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        switch (ToolFor(tkbType))
        {
            case PlacementToolKind.Point:
                spawn.StartPlacementMode(tkbType, AffiliationJson(side));
                return true;
            case PlacementToolKind.Area:
                spawn.StartAreaAuthoringMode();
                return true;
            case PlacementToolKind.Route:
                spawn.StartRouteAuthoringMode();
                return true;
            case PlacementToolKind.Zone:
                spawn.StartZoneAuthoringMode();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The initial-properties JSON carrying a side.</summary>
    public static string AffiliationJson(eForceIdentifier side)
        => JsonSerializer.Serialize(new { Affiliation = side.ToString() });
}
