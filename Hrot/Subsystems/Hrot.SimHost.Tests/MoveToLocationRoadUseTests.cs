using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3130</c> (R-230) — the authored <c>MoveToLocation</c> carries the actor's road use from the scenario/mission JSON
    /// to the MoveTo it issues. 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a D1.
    /// </summary>
    public sealed class MoveToLocationRoadUseTests
    {
        [Theory]
        [InlineData("{\"x\":10,\"y\":20,\"roadUse\":\"Never\"}", RoadUse.Never)]
        [InlineData("{\"x\":10,\"y\":20,\"roadUse\":\"StronglyPrefer\"}", RoadUse.StronglyPrefer)]
        [InlineData("{\"x\":10,\"y\":20}", RoadUse.Unspecified)]
        public void CE3130_TheAuthoredRoadUse_ReachesTheBlackboard(string json, RoadUse expected)
        {
            var authored = JsonSerializer.Deserialize<MoveToLocationParamsJsonDto>(json);
            var block = CgfNodes.ConvertMoveTo(authored, geoTransform: null);
            Assert.Equal(expected, block.RoadUse);
            Assert.Equal(10f, block.X);
        }

        /// <summary>An order that names no road use is written without the key, so authored files do not churn.</summary>
        [Fact]
        public void CE3130_AnUnspecifiedRoadUse_IsNotWritten_ANamedOneIsWrittenByName()
        {
            Assert.DoesNotContain("roadUse", JsonSerializer.Serialize(new MoveToLocationParamsJsonDto { X = 1, Y = 2 }));
            Assert.Contains("\"roadUse\":\"Prefer\"",
                JsonSerializer.Serialize(new MoveToLocationParamsJsonDto { X = 1, Y = 2, RoadUse = RoadUse.Prefer }));
        }

        [Fact]
        public unsafe void CE3130_TheMoveToLocationNode_IssuesTheMoveWithTheRoadUse()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<LocomotionChannel>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new LocomotionChannel());

            var p = new CgfNodes.MoveToLocationParams { X = 50f, Y = 60f, Speed = 2f, ArrivalRadius = 3f, RoadUse = RoadUse.Never };
            CgfNodes.Action_WriteMoveToChannel(ref p, e, repo);

            var ch = repo.GetComponent<LocomotionChannel>(e);
            MoveToParams issued;
            byte* src = ch.Params;
            issued = *(MoveToParams*)src;
            Assert.Equal(NavigationConstants.ActionIdMoveTo, ch.ActiveAction);
            Assert.Equal(RoadUse.Never, issued.RoadUse);
            Assert.Equal(50f, issued.Destination.X);
        }
    }
}
