namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ CE-11009 (RR-9, R-11003; Hrot docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — WHAT a round struck, as a renderer's puff, spark
    /// or splinters need it: the terrain material's THERMAL CLASS (the fixed list, <see cref="Fdp.Toolkit.Terrain.TerrainThermalClass"/>), folded to one byte.
    /// Carried as a byte on <see cref="Contracts.RoundImpactNotification.Material"/> and on the wire's <c>RoundImpact</c>.
    /// </summary>
    public enum ImpactMaterial : byte
    {
        Unknown = 0, Asphalt, Concrete, Brick, Metal, Glass, Wood, Soil, Sand, Vegetation, Water, Snow,
    }

    /// <summary>⭐ CE-11009 — a terrain material's name → <see cref="ImpactMaterial"/>, through its thermal class (the shared library; a name it does not
    /// know is <see cref="ImpactMaterial.Unknown"/>). The ground a round stops in is <see cref="ImpactMaterial.Soil"/>.</summary>
    public static class ImpactMaterials
    {
        public static ImpactMaterial OfThermalClass(string? thermalClass) => thermalClass switch
        {
            "asphalt" => ImpactMaterial.Asphalt,
            "concrete" => ImpactMaterial.Concrete,
            "brick" => ImpactMaterial.Brick,
            "metal" => ImpactMaterial.Metal,
            "glass" => ImpactMaterial.Glass,
            "wood" => ImpactMaterial.Wood,
            "soil-dry" or "soil-wet" => ImpactMaterial.Soil,
            "sand" => ImpactMaterial.Sand,
            "grass" or "dense-vegetation" => ImpactMaterial.Vegetation,
            "water" => ImpactMaterial.Water,
            "snow" => ImpactMaterial.Snow,
            _ => ImpactMaterial.Unknown,
        };

        /// <summary>The material named <paramref name="material"/> (a crossing's <c>Material</c>), Unknown when none or unknown to the library.</summary>
        public static ImpactMaterial Of(string? material)
            => material != null && Fdp.Toolkit.Terrain.TerrainMaterialLibrary.Shared.TryGet(material, out var m) ? OfThermalClass(m.ThermalClass) : ImpactMaterial.Unknown;
    }
}
