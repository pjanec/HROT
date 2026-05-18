namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// Logical role of a subordinate entity within its commander's unit.
    /// Zero value (<c>Undefined</c>) is the safe default for new entities with no commander.
    /// </summary>
    /// IMPORTANT: Must be kept in sync with Hrot.NED.Descriptors.eTacticalDesignation
    public enum TacticalDesignation : ushort
    {
        Undefined    = 0,
        Commander    = 1,
        SquadLeader  = 2,
        Wingman      = 3,
        Support      = 4,
    }
}
