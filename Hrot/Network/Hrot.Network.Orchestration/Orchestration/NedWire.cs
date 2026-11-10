namespace Hrot.NED.Descriptors.Orchestration
{
    /// <summary>
    /// ⭐ <c>AQ107 C</c> — the NED-wide constants a gateway needs and every NED publisher states. ⭐ Lives in the lowest NED project
    /// (<c>Hrot.Network.Orchestration</c>, which <c>Hrot.Network.NED</c> references) so every NED writer can reach it.
    /// </summary>
    public static class NedWire
    {
        /// <summary>
        /// The NED wire version. ⭐ Starts at 1 with the single local-ENU frame (AQ107 B/D/E/F cutover); carried in
        /// <see cref="GeoReference.ProtocolVersion"/>. ⛔ Bump it with any change a gateway built for the previous layout would misread.
        /// </summary>
        public const int ProtocolVersion = 2;   // ⭐ CE-4396 UE-e (R-4197): 2 = the local frame is UTM grid metres (the tangent plane retired); Frame 1 / 2, the zone override

        /// <summary><see cref="GeoReference.Frame"/> 1 — UTM: local = (E − E0, N − N0, h − H0) in grid metres (X east, Y north = GRID north, Z up);
        /// the frame is a function of the origin (zone = its longitude band, hemisphere = its latitude's sign, (E0, N0) = it projected there) unless
        /// <see cref="GeoReference.UtmZoneOverride"/> is set.</summary>
        public const byte FrameUtm = 1;

        /// <summary><see cref="GeoReference.Frame"/> 2 — NONE: the terrain declares no origin; its local metres have no place on the Earth (the origin fields are 0 and mean nothing).</summary>
        public const byte FrameNone = 2;
    }
}
