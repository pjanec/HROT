namespace FDP.Framework.Runner
{
    /// <summary>
    /// Snapshot of a discovered peer's status as reported via the
    /// <c>SubsystemStatusAnnounce</c> DDS topic.
    /// </summary>
    public class SubsystemPeerInfo
    {
        /// <summary>DDS node ID of the peer process.</summary>
        public int NodeId { get; set; }

        /// <summary>Logical subsystem name (e.g. "SimHost", "IG", "ExCon").</summary>
        public string SubsystemName { get; set; } = string.Empty;

        /// <summary>DDS domain the peer is operating on.</summary>
        public int DomainId { get; set; }

        /// <summary><c>true</c> once the peer has finished its own startup.</summary>
        public bool Ready { get; set; }

        /// <summary>UTC Unix-millisecond timestamp of the last received announcement.</summary>
        public long LastSeenTimestamp { get; set; }
    }
}
