using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// Makes the terrain tiles covering a zone's footprint resident.
    ///
    /// <para><b>⛔ Tile residency is keyed by GEOGRAPHY, never by zone.</b> A tile is live while ANY
    /// loaded zone covers it, so a zone does not own its tiles and invalidating a zone must never free
    /// them — otherwise shrinking zone A would evict tiles zone B is still using. ⚠ This rule is stated
    /// on the SEAM, while the only implementation is a fake, precisely so the real one cannot inherit a
    /// zone-owns-its-tiles assumption from the stub.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.8, §5.6.
    /// </summary>
    public interface IZoneTileLoader
    {
        /// <summary>
        /// Makes the tiles covering <paramref name="boundsMin"/>..<paramref name="boundsMax"/> resident.
        /// </summary>
        /// <param name="footprintHash">
        /// The zone footprint this build is for, used only for diagnostics and for the marker the caller
        /// stamps. ⛔ It is NOT a tile cache key — tiles are shared between zones.
        /// </param>
        /// <returns><c>true</c> when the coverage is resident afterwards.</returns>
        bool Build(Vector2 boundsMin, Vector2 boundsMax, ulong footprintHash);
    }
}
