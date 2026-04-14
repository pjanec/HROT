using System.Collections.Generic;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Systems;

/// <summary>
/// Static registry of all five standardised map layers supported by this IG instance.
///
/// <para>Each entry is a <see cref="MapLayerDefinition"/> record that captures:
/// a human-readable JSON key, a bit-mask, and a membership predicate evaluated by
/// <see cref="MapLayerAssignmentSystem"/> once per entity per rescan pass.</para>
///
/// <para>Consumers:</para>
/// <list type="bullet">
///   <item><see cref="MapLayerAssignmentSystem"/> — calculates per-entity <c>MapDisplayComponent.LayerMask</c>.</item>
///   <item><c>IgCapabilitiesPublisher</c> — populates <c>IGCapabilitiesAnnounce.LayerTreeJson</c>.</item>
///   <item><c>IgApplication.ParseAndApplyConfig</c> — translates ExCon JSON flags into <c>MapCanvas.ActiveLayerMask</c>.</item>
/// </list>
/// </summary>
public static class MapLayerRegistry
{
    // ── DIS domain constants ─────────────────────────────────────────────────
    // Matches IEEE 1278.1 enumeration table.

    /// <summary>DIS Domain: Land (ground vehicles, infantry).</summary>
    private const byte DomainLand = 1;

    /// <summary>DIS Domain: Air (fixed-wing, rotary, UAV).</summary>
    private const byte DomainAir = 2;

    /// <summary>DIS Kind: Platform (armored vehicles, aircraft).</summary>
    private const byte KindPlatform = 1;

    // ── Bit allocations ──────────────────────────────────────────────────────

    /// <summary>Rendering bit for the <c>"units_ground"</c> layer.</summary>
    public const uint GroundUnitsBit      = 1u << 0;

    /// <summary>Rendering bit for the <c>"units_air"</c> layer.</summary>
    public const uint AirUnitsBit         = 1u << 1;

    /// <summary>Rendering bit for the <c>"vehicles"</c> layer (motorised platforms).</summary>
    public const uint VehiclesBit         = 1u << 2;

    /// <summary>Rendering bit for the <c>"tactical_graphics"</c> layer (area overlays).</summary>
    public const uint TacticalGraphicsBit = 1u << 3;

    /// <summary>Rendering bit for the <c>"road_graphs"</c> layer (route entities).</summary>
    public const uint RoadGraphsBit       = 1u << 4;

    /// <summary>
    /// Ordered list of all layer definitions.
    /// Registration order has no semantic significance; only
    /// <see cref="MapLayerDefinition.Name"/> and <see cref="MapLayerDefinition.BitMask"/>
    /// are externally visible.
    /// </summary>
    public static readonly IReadOnlyList<MapLayerDefinition> All =
        new List<MapLayerDefinition>
        {
            // 1. Ground units — any entity assigned to the DIS Land domain.
            //    Includes both wheeled/tracked platforms (Kind=1) and infantry (Kind=3).
            new("units_ground", GroundUnitsBit,
                (entity, dis, view) => dis.Value != 0 && dis.Domain == DomainLand),

            // 2. Air units — any entity in the DIS Air domain.
            new("units_air", AirUnitsBit,
                (entity, dis, view) => dis.Value != 0 && dis.Domain == DomainAir),

            // 3. Vehicles — DIS Platforms (Kind=1) on land or in the air.
            //    Excludes lifeforms (Kind=3) and munitions (Kind=2).
            new("vehicles", VehiclesBit,
                (entity, dis, view) =>
                    dis.Value != 0
                    && dis.Kind == KindPlatform
                    && (dis.Domain == DomainLand || dis.Domain == DomainAir)),

            // 4. Tactical graphics — component-based: any entity carrying MapOverlayStyle.
            new("tactical_graphics", TacticalGraphicsBit,
                (entity, dis, view) => view.HasComponent<MapOverlayStyle>(entity)),

            // 5. Road graphs — route entities identified by their TKB type.
            new("road_graphs", RoadGraphsBit,
                (entity, dis, view) =>
                    view.HasComponent<TkbIdentity>(entity)
                    && view.GetComponentRO<TkbIdentity>(entity).TkbType
                        == TkbEntityTypes.TacGraphic_Route),
        };
}
