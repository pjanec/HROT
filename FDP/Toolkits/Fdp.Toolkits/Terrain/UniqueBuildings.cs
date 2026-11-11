using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>A street of a block, in block-LOCAL metres: its centre line and carriageway width.</summary>
    public readonly record struct UniqueStreet(Vector2 A, Vector2 B, float Width);

    /// <summary>What a block asks <see cref="IUniqueBuildings"/> for: houses along these streets.</summary>
    public sealed record UniqueRequest(IReadOnlyList<UniqueStreet> Streets, int Count, int MinStoreys, int MaxStoreys, float AreaP10, float AreaP50, float AreaP90, ulong Seed);

    /// <summary>One generated, ENTERABLE building: a template (<see cref="TemplateJson"/>, the building-template format, origin = its first footprint vertex, no turn) and where that origin stands in block-local metres.</summary>
    public sealed record UniqueBuilding(string TemplateJson, Vector2 Origin);

    /// <summary>
    /// ⭐ T-3 part 2 (CE-4430, docs/DESIGN_Terrain_Authoring.md §18i I-6) — the SEAM through which a block's <c>grid</c> element gets UNIQUE 3–6-storey houses. The generator (the synth's <c>BuildingGenerator</c> + the importer's <c>BuildingTemplateGenerator</c>) lives in
    /// <c>Hrot.Terrain.*</c>, which this assembly cannot reference, so the host passes an implementation (<c>Hrot.Terrain.Random.SynthUniqueBuildings</c>) to <see cref="OverlayExpansion.Block"/>. ⭐ Deterministic: the same request is the same buildings, byte for byte
    /// (the templates are written into the overlay and hashed).
    /// </summary>
    public interface IUniqueBuildings
    {
        /// <summary>Up to <see cref="UniqueRequest.Count"/> enterable houses along the streets, in block-local metres, at least their setback off every street.</summary>
        IReadOnlyList<UniqueBuilding> Make(UniqueRequest request);
    }
}
