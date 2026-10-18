using Fdp.Core.CommandHierarchy;
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
        /// <summary>⭐ <c>CE-3253</c> (BR-8) — EVERY <c>TacticalDesignation</c> is on the wire enum by the same name and value (the "must be kept
        /// in sync" comment, enforced). ⛔ Before, TeamLeader and Medic crossed as numbers outside the enum. ✅ Red-proof: drop a wire member ⇒ red.</summary>
        [Fact]
        public void CE3253_EveryDesignation_IsOnTheWire_ByNameAndValue()
        {
            foreach (TacticalDesignation d in System.Enum.GetValues(typeof(TacticalDesignation)))
            {
                var wire = TacticalDesignationMapper.ToDds(d);
                Assert.True(System.Enum.IsDefined(typeof(eTacticalDesignation), wire), $"{d} is not on the wire enum");
                Assert.Equal(d.ToString(), wire.ToString());
                Assert.Equal(d, TacticalDesignationMapper.ToEcs(wire));
            }
        }
    }
}
