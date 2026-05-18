using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// Requests the <c>UnitHierarchySystem</c> to assign <paramref name="Subordinate"/> under
    /// <paramref name="Commander"/> with the given <paramref name="Designation"/>.
    ///
    /// When <c>HasFormationSlot == 1</c> the hierarchy system also atomically writes
    /// <c>FormationFollower.SlotIndex</c> on the subordinate entity.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [EventId(2200)]
    public struct CmdAssignSubordinate
    {
        public Entity Subordinate;
        public Entity Commander;
        public TacticalDesignation Designation;
        public byte HasFormationSlot;  // 1 = write FormationFollower.SlotIndex atomically; 0 = skip
        public ushort SlotIndex;
    }

    /// <summary>
    /// Requests the <c>UnitHierarchySystem</c> to remove <paramref name="Subordinate"/>
    /// from its current commander.  Both <c>UnitSubordinate</c> and (when present)
    /// <c>FormationFollower</c> are removed atomically.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [EventId(2201)]
    public struct CmdRemoveSubordinate
    {
        public Entity Subordinate;
    }

    /// <summary>
    /// Published by <c>UnitHierarchySystem</c> when a <see cref="CmdAssignSubordinate"/>
    /// cannot be fulfilled (e.g. the commander's <c>UnitRoster</c> is at capacity).
    /// Consumers (e.g. <c>VehicleCommandSystem</c>) should set
    /// <c>LocomotionChannel.Status = NodeStatus.Failure</c> on the subordinate so that
    /// the waiting <c>JoinFormationExecutor</c> BTree node can unblock.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [EventId(2202)]
    public struct CmdAssignSubordinateRejected
    {
        public Entity Subordinate;
    }
}
