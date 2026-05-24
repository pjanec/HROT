using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs.Topics;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Managed event published by <c>EqsResultIngressTranslator</c> (online/DDS path) to
    /// bridge an inbound <c>EqsResultTopic</c> DDS sample onto the Brain-tier event bus so
    /// that <c>EqsResultUpdateSystem</c> can write the results into the entity's
    /// <c>EqsCognitiveBuffer</c> component.
    ///
    /// <para>This is a managed class (not a struct) because it carries a <see cref="List{T}"/>
    /// of result entries.  The list is allocated once by the ingress translator and consumed
    /// and discarded by <c>EqsResultUpdateSystem</c> in the same frame.</para>
    ///
    /// <para>Placed in <c>Fdp.Toolkit.Spatial.Eqs</c> so both <c>Hrot.Network.NED</c>
    /// (the ingress translator) and <c>Hrot.SimHost</c> (the update system) can reference it
    /// without introducing a circular project dependency.</para>
    /// </summary>
    public sealed class EqsResultUpdateEvent
    {
        /// <summary>The Brain-tier entity whose <c>EqsSensor</c> triggered this evaluation.</summary>
        public Entity Observer;
        /// <summary>Sensor epoch at solve time.  Stale deliveries (epoch != sensor.Epoch) are silently dropped.</summary>
        public uint Epoch;
        /// <summary>Simulation tick at which the Muscle solver completed this evaluation.</summary>
        public uint RefreshTick;
        /// <summary>Ranked result entries.  May be empty for Phase 1 stub results (EntryCount == 0).</summary>
        public List<EqsResultEntry> Results = new();
    }
}
