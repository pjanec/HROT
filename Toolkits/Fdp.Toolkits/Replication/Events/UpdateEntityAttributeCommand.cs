namespace Fdp.Toolkit.Replication.Events
{
    /// <summary>
    /// Managed event expressing an intent to patch an entity's attributes via JSON.
    /// Bridged to DDS by the egress translator.
    ///
    /// <para>
    /// Published by UI tools (e.g. <c>EntityRotationTool</c>) when the operator
    /// requests a change to an attribute on an entity that may be owned by a remote
    /// authoritative node.  In distributed mode the translator converts this event into
    /// an <c>UpdateEntityAttributeRequest</c> DDS sample; in offline (Editor) mode the
    /// local fast-path in the tool applies the change directly to the ECS component.
    /// </para>
    /// </summary>
    public sealed class UpdateEntityAttributeCommand
    {
        /// <summary>
        /// Network entity ID of the target entity.
        /// Resolved from <c>NetworkIdentity.Value</c> by the publishing tool.
        /// </summary>
        public long NetworkId;

        /// <summary>
        /// Hierarchical JSON attribute patch, e.g. <c>{"Heading":340.7}</c>.
        /// Processed by <c>JsonAttributeCompiler</c> on the authoritative node.
        /// </summary>
        public string AttributePatchJson = string.Empty;
    }
}
