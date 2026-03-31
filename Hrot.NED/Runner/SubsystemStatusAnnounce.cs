using CycloneDDS.Schema;

namespace Hrot.DDS.DataModel.Runner
{
    /// <summary>
    /// Announces the presence and readiness of a Runner subsystem on the DDS bus.
    /// Published by each subsystem process during startup so peers can discover
    /// each other via the Waiting Room protocol.
    ///
    /// <para>QoS: Reliable + TransientLocal (KeepLast/1) ensures late-joining
    /// processes receive the most recent announcement from all peers.</para>
    /// </summary>
    [DdsTopic("SubsystemStatusAnnounce")]
    [DdsIdlFile("runner-msgs")]
    [DdsManaged]
    [DdsQos(
        Reliability  = DdsReliability.Reliable,
        Durability   = DdsDurability.TransientLocal,
        HistoryKind  = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]
    public partial struct SubsystemStatusAnnounce
    {
        /// <summary>Unique node identifier within the DDS domain. Used as the DDS key.</summary>
        [DdsKey]
        public int NodeId;

        /// <summary>Human-readable subsystem name (e.g. "SimHost", "IG", "ExCon").</summary>
        public string SubsystemName;

        /// <summary>DDS domain ID the subsystem is operating on.</summary>
        public int DomainId;

        /// <summary>
        /// <c>true</c> once the subsystem has completed initialisation and is ready
        /// to begin normal operation.
        /// </summary>
        public bool Ready;

        /// <summary>UTC Unix-millisecond timestamp when this status was published.</summary>
        public long Timestamp;
    }
}
