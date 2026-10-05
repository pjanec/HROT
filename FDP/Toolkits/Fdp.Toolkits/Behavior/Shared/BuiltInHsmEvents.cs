namespace Fdp.Toolkit.Behavior.Shared
{
    /// <summary>
    /// ⭐⭐ <c>CE-3040</c> — the HSM events the ENGINE raises, by name and reserved id, in the ONE spelling the runtime
    /// (<c>HsmRunner</c>) and the authoring side (the HSM emitter and the editor's mapper, which LINK this file) agree on.
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.3b.
    /// <para>An authored event named after one of these gets its reserved id, whatever id it stored — so an author writes
    /// <i>On Sensor.FirstThreat → Engaged</i> and never types an id.</para>
    /// <para>⛔⛔ The ids are <b>ABI</b>: <c>0xFF00 + SensorChange</c>, far from the sequential authored range (from 1) and below
    /// the kernel's reserved <c>0xFFFD</c> / <c>0xFFFE</c>. <c>BuiltInHsmEventsParityTests</c> pins the names against the
    /// runtime <c>SensorChange</c> enum. ⚠ MobilityLost (id 1) is NOT here: 1 is inside the sequential range.</para>
    /// </summary>
    internal static class BuiltInHsmEvents
    {
        /// <summary>The base of the sensor events: id = <c>SensorBase + (byte)SensorChange</c>.</summary>
        public const ushort SensorBase = 0xFF00;

        /// <summary>The sensor events' names, indexed by <c>SensorChange - 1</c>.</summary>
        public static readonly string[] SensorNames =
        {
            "Sensor.Acquired", "Sensor.Lost", "Sensor.TopChanged", "Sensor.FirstThreat", "Sensor.AllClear", "Sensor.Hit",
            "Sensor.NearMiss",   // ⭐ CE-3064 — id 0xFF07
        };

        /// <summary>The reserved id of a sensor change (its <c>SensorChange</c> value, 1-based).</summary>
        public static ushort SensorEventId(byte sensorChange) => (ushort)(SensorBase + sensorChange);

        /// <summary>The reserved id of a built-in event named <paramref name="name"/>.</summary>
        public static bool TryGetId(string? name, out ushort id)
        {
            for (int i = 0; i < SensorNames.Length; i++)
            {
                if (string.Equals(SensorNames[i], name, System.StringComparison.Ordinal))
                {
                    id = SensorEventId((byte)(i + 1));
                    return true;
                }
            }
            id = 0;
            return false;
        }
    }
}
