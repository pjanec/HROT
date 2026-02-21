namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// Universal command to destroy a network entity via proper ELM lifecycle teardown.
    /// Bridged from DDS EntityMaster DISPOSE or a local destruction request.
    /// </summary>
    public class DestroyEntityCommand
    {
        /// <summary>
        /// The network entity ID to destroy. Looked up in NetworkEntityMap.
        /// </summary>
        public long NetworkId;

        /// <summary>
        /// Human-readable reason (for logging/diagnostics).
        /// </summary>
        public string Reason;
    }
}
