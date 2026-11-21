namespace Fdp.Toolkit.NetworkSpawning.Events
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
        public int NetworkId;

        /// <summary>
        /// Human-readable reason (for logging/diagnostics).
        /// </summary>
        public string? Reason;

        /// <summary>
        /// True when this command originated from network ingress (e.g. EntityMaster DISPOSE).
        /// </summary>
        public bool IsRemote;

        /// <summary>
        /// ⭐ Edit framework F2 (CE-4276) — the user's request id (<c>Edits.Submit</c>), carried to the owner by the egress so its answer can be matched.
        /// ⭐ A command WITH an id that is not remote is a user's delete: a node that does not own the entity does NOT tear its replica down — the owner
        /// does, and its dispose removes the replica. <see cref="Guid.Empty"/> = as before (a local teardown).
        /// </summary>
        public Guid RequestId;
    }
}
