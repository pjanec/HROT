using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Core.CommandHierarchy
{
    /// <summary>
    /// ECS component placed on <em>subordinate</em> entities.  Carries the
    /// generation-safe handle to the commanding entity and the subordinate's
    /// logical role within that unit.
    ///
    /// <para><c>Commander == Entity.Null</c> (default zero) means "no commander".</para>
    ///
    /// <para>Size: 12 bytes -- <c>Entity</c> (int+ushort+2B pad = 8 B) + <c>TacticalDesignation ushort</c> (2 B)
    /// + 2 B trailing pad (struct aligned to 4 bytes).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnitSubordinate)]
    public struct UnitSubordinate
    {
        /// <summary>Generation-safe handle to the commanding entity.  <c>Entity.Null</c> when unassigned.</summary>
        public Entity Commander;

        /// <summary>Logical role of this entity within the commander's unit.  <c>Undefined</c> when unassigned.</summary>
        public TacticalDesignation Designation;
    }
}
