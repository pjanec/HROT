using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-4396 (docs/DESIGN_Geo_Origin.md §6.1a, R-4197) — THE rule from a terrain's (or a recording's) stored data to its geo frame, in ONE place:
    /// a declared exact <c>frame.utm</c> is used as it is; else the lat / lon origin IS the frame (zone = its longitude band, hemisphere = its
    /// latitude's sign, E0 / N0 = it projected there — or the optional zone override); no origin ⇒ <see cref="LocalOnlyFrame"/>.
    /// </summary>
    public static class TerrainGeoFrame
    {
        public static IGeographicTransform Of(TerrainGeoOrigin? origin, TerrainUtmFrame? utm = null, int? zoneOverride = null, bool? southOverride = null)
        {
            if (utm != null) return new UtmTransform(utm.Zone, utm.South, utm.Easting, utm.Northing, origin?.Alt ?? 0.0);
            if (origin is { } o) return UtmTransform.AtOrigin(o.Lat, o.Lon, o.Alt, zoneOverride, southOverride);
            return new LocalOnlyFrame();
        }

        /// <summary>
        /// ⭐ CE-4396 UE-e (AQ107 C) — the frame as the wire's <c>GeoReference</c> says it: <see cref="Frame"/> 1 = UTM (the zone derived from the origin's
        /// longitude, unless the override fields say another), 2 = none (local-only, origin 0, 0, 0 — nothing to say).
        /// </summary>
        public readonly record struct Wire(byte Frame, TerrainGeoOrigin Origin, int ZoneOverride, bool SouthOverride)
        {
            public const byte FrameUtm = 1, FrameNone = 2;
            public static Wire None => new(FrameNone, TerrainGeoOrigin.Zero, 0, false);
        }

        /// <summary>The wire description of a terrain's (or a recording's) frame, by the one rule of <see cref="Of"/>.</summary>
        public static Wire Describe(TerrainGeoOrigin? origin, TerrainUtmFrame? utm = null, int? zoneOverride = null, bool? southOverride = null)
        {
            if (origin == null && utm == null) return Wire.None;
            var o = origin ?? FromFrame(utm!);
            int zone = utm?.Zone ?? zoneOverride ?? UtmTransform.ZoneOf(o.Lon);
            bool south = utm?.South ?? southOverride ?? (o.Lat < 0);
            bool differs = zone != UtmTransform.ZoneOf(o.Lon) || south != (o.Lat < 0);
            return new Wire(Wire.FrameUtm, o, differs ? zone : 0, differs && south);
        }

        private static TerrainGeoOrigin FromFrame(TerrainUtmFrame utm)
        {
            var (lat, lon) = Utm.ToGeodetic(utm.Easting, utm.Northing, utm.Zone, utm.South);
            return new TerrainGeoOrigin(lat, lon, 0.0);
        }

        /// <summary>The same rule applied to a node's one switchable transform.</summary>
        public static void Apply(SwitchableGeoTransform geo, TerrainGeoOrigin? origin, TerrainUtmFrame? utm = null, int? zoneOverride = null, bool? southOverride = null)
        {
            if (utm != null) geo.UseUtm(utm.Zone, utm.South, utm.Easting, utm.Northing, origin?.Alt ?? 0.0);
            else if (origin is { } o) geo.UseOrigin(o.Lat, o.Lon, o.Alt, zoneOverride, southOverride);
            else geo.UseLocalOnly();
        }
    }
}