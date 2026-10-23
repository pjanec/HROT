using System;
using Fdp.Core;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Represents entity position in WGS84 Geodetic coordinates.
    /// Managed component (uses doubles) for network interoperability.
    /// </summary>
    [ComponentId(GlobalComponentIds.GeoPositionGeodetic)]
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public class PositionGeodetic
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; }
    }
}
