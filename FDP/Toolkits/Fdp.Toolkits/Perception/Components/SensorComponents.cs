using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Components
{
    /// <summary>
    /// ⭐ What a sensor child IS — its kind and, for a TKB sensor, where it came from (docs/DESIGN_Sensors_And_Doctrine.md §4).
    /// Present on the Brain and the Muscle alike; <c>UnitSensors.Of(unit, kind)</c> finds a unit's sensor by it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SensorTag)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct SensorTag
    {
        /// <summary>What it senses with.</summary>
        public SensorModality Kind;

        /// <summary>Its index in the unit's TKB sensor list (meaningful when <see cref="FromTkb"/>).</summary>
        public byte TkbIndex;

        /// <summary>1 = built from the unit's TKB on every node (part id <c>1000 + TkbIndex</c>), 0 = made by a behaviour.</summary>
        public byte FromTkb;

        /// <summary>
        /// ⭐ CE-3038 — 1 = the IMPLICIT visual sensor of a TKB that lists no sensors but has a vision range. Its range and
        /// field of view are the unit's <see cref="PerceptionReceptor"/> — the value a Brain retunes over the wire
        /// (<c>SensorConfig</c>) — not a capability record. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.5.
        /// </summary>
        public byte Implicit;
    }

    /// <summary>
    /// ⭐ A sensor's per-kind parameters — the TKB's own <see cref="SensorEntryDto"/>: the one it was BUILT from and the
    /// one in FORCE (an override replaces <see cref="Current"/>; clearing it restores <see cref="Default"/>). The sensing
    /// tests of a sensor's template read <see cref="Current"/>. Records are immutable, so a snapshot shares them safely.
    /// </summary>
    [ComponentId(GlobalComponentIds.SensorCapability)]
    // ⚠ NOT Transient: Transient includes NoPreview, which would keep it out of the background snapshot the solver reads.
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    public sealed class SensorCapability
    {
        public required SensorEntryDto Default { get; init; }
        public required SensorEntryDto Current { get; init; }
    }

    /// <summary>
    /// ⭐ On the Brain: the JSON config a sensor's wire sample carries (R-186 M′ / R-187 N′). On a behaviour-made sensor it is
    /// that sensor's per-kind config; on a TKB sensor its PRESENCE means "overridden" — the sample goes on the wire, and
    /// removing it sends the sensor back to its TKB default. <see cref="Json"/> empty = override the fields only (e.g. switch it off).
    /// </summary>
    [ComponentId(GlobalComponentIds.SensorConfigPayload)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class SensorConfigPayload
    {
        /// <summary>The format tag of <see cref="Json"/> — <see cref="SensorConfigCodec.KindSensorEntry"/>.</summary>
        public string Kind { get; init; } = string.Empty;
        public string Json { get; init; } = string.Empty;
    }
}
