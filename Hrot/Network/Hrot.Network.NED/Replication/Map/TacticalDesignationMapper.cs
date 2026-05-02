using Hrot.Core.CommandHierarchy;
using Hrot.NED.Descriptors;

namespace Hrot.Map.Common.Replication
{
    /// <summary>
    /// Bidirectional mapper between <see cref="TacticalDesignation"/> (ECS side) and
    /// <see cref="eTacticalDesignation"/> (DDS/NED side).
    ///
    /// Both enums share identical underlying <c>ushort</c> values, so conversion is a
    /// simple cast — no lookup table needed.
    /// </summary>
    public static class TacticalDesignationMapper
    {
        /// <summary>Maps the ECS enum to the DDS-wire enum.</summary>
        public static eTacticalDesignation ToDds(TacticalDesignation ecs)
            => (eTacticalDesignation)(ushort)ecs;

        /// <summary>Maps the DDS-wire enum to the ECS enum.</summary>
        public static TacticalDesignation ToEcs(eTacticalDesignation dds)
            => (TacticalDesignation)(ushort)dds;
    }
}
