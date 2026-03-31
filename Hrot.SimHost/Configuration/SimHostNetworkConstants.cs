namespace Hrot.SimHost.Configuration
{
    /// <summary>
    /// Named network constants for the SimHost subsystem.
    ///
    /// <para>Centralised here so that any change is a single-line edit
    /// (CODE-STANDARDS §1 — no magic numbers in production code).</para>
    /// </summary>
    public static class SimHostNetworkConstants
    {
        /// <summary>
        /// Local node ID assigned to the SimHost instance.
        /// Used as <c>OwnerNodeId</c> in <c>SpawnEntityCommand</c> and as the
        /// <c>spawningSystem localNodeId</c> parameter.
        /// </summary>
        public const int LocalNodeId = 1;
    }
}
