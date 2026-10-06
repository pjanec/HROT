using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Standing query configuration placed on a sensor child entity.
    /// Carries the blueprint identity, refresh cadence, and staleness epoch for the
    /// danger-area pipeline (squad danger-area pipeline, SS5.1).
    /// Sequential layout. ⭐ <c>CE-3072</c> B0: also carries the sensor's <see cref="Settings"/> (it grows by their size).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DangerAreaSensor)]
    public struct DangerAreaSensor
    {
        /// <summary>FNV-1a-32 hash of the query template blueprint id.</summary>
        public uint BlueprintId;
        /// <summary>Incremented on every successful refresh. Downstream caches compare this
        /// to detect staleness (matches EqsSensor.Epoch precedent).</summary>
        public uint Epoch;
        /// <summary>Minimum seconds between refreshes. 0 = refresh every call.</summary>
        public float RefreshIntervalSeconds;
        /// <summary>Simulation time (seconds) at which the last refresh completed.</summary>
        public float LastRefreshSimTime;

        /// <summary>⭐ <c>CE-3072</c> B0 — what the sensor watches and how (corridor, route, cadence). Set by
        /// <c>DangerAreaChildSensor.Ensure</c> / <c>Configure</c>; read by the solver (B3) and the producer (B4).</summary>
        public DangerAreaSettings Settings;
    }
}
