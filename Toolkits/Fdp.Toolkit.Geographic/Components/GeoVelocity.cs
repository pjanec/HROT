using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Geodetic velocity and acceleration. Mirrors SimVelocity in the geographic domain,
    /// extended with a linear acceleration vector.
    ///
    /// Written by SimTransformBridgeSystem each tick for locally-owned entities.
    /// Read by application-layer egress translators (e.g. GeoSpatialEgressTranslator in SimHost).
    ///
    /// All vectors are expressed in the local ENU frame (X=East, Y=North, Z=Up),
    /// consistent with SimVelocity.Linear and SimVelocity.Angular conventions.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.GeoVelocity)]
    public struct GeoVelocity
    {
        /// <summary>
        /// Linear velocity in local ENU coordinates (m/s).
        /// X=East, Y=North, Z=Up. Mirrors SimVelocity.Linear.
        /// </summary>
        public Vector3 Linear;

        /// <summary>
        /// Angular velocity in local ENU coordinates (rad/s).
        /// X=roll-rate, Y=pitch-rate, Z=yaw-rate. Mirrors SimVelocity.Angular.
        /// </summary>
        public Vector3 Angular;

        /// <summary>
        /// Linear acceleration in local ENU coordinates (m/s?).
        /// X=East, Y=North, Z=Up. No SimVelocity equivalent � present here
        /// because geodetic DR formats (e.g. GeoSpatialDR) include acceleration.
        /// </summary>
        public Vector3 Accel;
    }
}
