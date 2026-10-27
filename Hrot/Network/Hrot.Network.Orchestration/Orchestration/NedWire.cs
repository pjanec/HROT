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
        public const int ProtocolVersion = 1;

        /// <summary><see cref="GeoReference.Frame"/> 0 — an ENU tangent plane at the origin (X east, Y north, Z up). The only frame.</summary>
        public const byte FrameEnuTangentPlane = 0;
    }
}
