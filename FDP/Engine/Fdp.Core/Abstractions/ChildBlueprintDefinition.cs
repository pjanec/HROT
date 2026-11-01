using Fdp.Core.CommandHierarchy;
using Fdp.Core;

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
        public TkbTypeId ChildTkbType { get; set; }

        /// <summary>
        /// The tactical designation of this child within the parent unit.
        /// <c>Undefined</c> means no commander-subordinate link is created.
        /// </summary>
        public TacticalDesignation Designation { get; set; }

        public ChildBlueprintDefinition(int instanceId, TkbTypeId childTkbType, TacticalDesignation designation = TacticalDesignation.Undefined)
        {
            InstanceId   = instanceId;
            ChildTkbType = childTkbType;
            Designation  = designation;
        }
    }
}
