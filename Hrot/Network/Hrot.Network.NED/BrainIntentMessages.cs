using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-3048</c> (V7) — a unit's AI intent on the wire.</b> 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    ///
    /// <para>Published by the owner of ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtBrainIntent"/> (= 98, in the
    /// Brain group, granted with it) whenever what its unit runs changes: the task slot, the SOP slot and the ROE as
    /// <c>{Name, Params, Origin}</c> — the scenario's own shape (<c>InitialBrainIntent</c>, R-192), as JSON. Every other Brain
    /// node keeps it; the node that GAINS the Brain starts it. ⚠ TransientLocal + KeepLast 1: a node that joins later still
    /// holds the last intent, which is what a failover needs.</para>
    /// <para>⚠ R-165 holds: behaviour COMPONENTS stay unsent; this is a declarative projection of them.</para>
    /// </summary>
    [DdsTopic("EntityBrainIntent")]
    [DdsIdlFile("hrot-brain-intent")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityBrainIntent
    {
        /// <summary>Network entity id.</summary>
        [DdsKey]
        public long EntityId;

        /// <summary><c>InitialBrainIntent</c> as JSON (<c>FdpJsonOptionsRegistry.DefaultRelaxed</c>).</summary>
        public string IntentJson;
    }
}
