using Hrot.Core.CommandHierarchy;
using Hrot.Map.Common.Replication;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Tests for TASK-CS001: TacticalDesignation dual-enum definitions and mapper.
    /// </summary>
    public class TacticalDesignationMapperTests
    {
        // -- CS001: cross-enum value parity --------------------------------------

        [Fact]
        public void TacticalDesignation_SquadLeader_MatchesDdsSideValue()
        {
            Assert.Equal(
                (ushort)TacticalDesignation.SquadLeader,
                (ushort)eTacticalDesignation.SquadLeader);
        }

        [Fact]
        public void TacticalDesignation_AllValues_MatchDdsEnum()
        {
            Assert.Equal((ushort)0, (ushort)TacticalDesignation.Undefined);
            Assert.Equal((ushort)0, (ushort)eTacticalDesignation.Undefined);

            Assert.Equal((ushort)TacticalDesignation.Commander,   (ushort)eTacticalDesignation.Commander);
            Assert.Equal((ushort)TacticalDesignation.SquadLeader, (ushort)eTacticalDesignation.SquadLeader);
            Assert.Equal((ushort)TacticalDesignation.Wingman,     (ushort)eTacticalDesignation.Wingman);
            Assert.Equal((ushort)TacticalDesignation.Support,     (ushort)eTacticalDesignation.Support);
        }

        [Fact]
        public void ETacticalDesignation_Default_IsUndefined()
        {
            Assert.Equal(eTacticalDesignation.Undefined, default(eTacticalDesignation));
        }

        // -- CS001: mapper round-trips -------------------------------------------

        [Fact]
        public void TacticalDesignationMapper_ToDds_Wingman_ReturnsWingman()
        {
            var result = TacticalDesignationMapper.ToDds(TacticalDesignation.Wingman);
            Assert.Equal(eTacticalDesignation.Wingman, result);
        }

        [Fact]
        public void TacticalDesignationMapper_ToEcs_Support_ReturnsSupport()
        {
            var result = TacticalDesignationMapper.ToEcs(eTacticalDesignation.Support);
            Assert.Equal(TacticalDesignation.Support, result);
        }

        [Fact]
        public void TacticalDesignationMapper_RoundTrip_AllValues()
        {
            var ecsSide = new[]
            {
                TacticalDesignation.Undefined,
                TacticalDesignation.Commander,
                TacticalDesignation.SquadLeader,
                TacticalDesignation.Wingman,
                TacticalDesignation.Support,
            };

            foreach (var ecs in ecsSide)
            {
                var dds = TacticalDesignationMapper.ToDds(ecs);
                var back = TacticalDesignationMapper.ToEcs(dds);
                Assert.Equal(ecs, back);
            }
        }
    }
}
