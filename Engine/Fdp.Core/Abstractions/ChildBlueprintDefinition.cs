namespace Fdp.Interfaces
{
    /// <summary>
    /// Defines a child entity (sub-entity) that should be spawned as part of a parent template.
    /// </summary>
    public struct ChildBlueprintDefinition
    {
        /// <summary>
        /// The instance ID of the part relative to the parent.
        /// </summary>
        public int InstanceId { get; set; }

        /// <summary>
        /// The TKB Type ID of the blueprint to use for this child.
        /// </summary>
        public long ChildTkbType { get; set; }

        public ChildBlueprintDefinition(int instanceId, long childTkbType)
        {
            InstanceId = instanceId;
            ChildTkbType = childTkbType;
        }
    }
}
