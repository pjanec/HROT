using System;
using Fdp.Core;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Represents entity position in WGS84 Geodetic coordinates (doubles, for network interoperability).
    /// ⭐ CE-4046 — an UNMANAGED struct (three doubles), no longer a managed class: a managed component changed in place never reached a
    /// delta frame, and a value type is recorded as a raw copy. 📄 docs/DESIGN_Replay_Census_H0.md §2.
    /// </summary>
    [ComponentId(GlobalComponentIds.GeoPositionGeodetic)]
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, recorded and saved, kept out of the background snapshot
    public struct PositionGeodetic
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; }
    }
}
