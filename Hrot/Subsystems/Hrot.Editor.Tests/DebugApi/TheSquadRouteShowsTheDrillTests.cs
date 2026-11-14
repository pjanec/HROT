using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-2157</c> (O-a, Squad Tactics §7.8.1) — <c>GET /entities/{id}/squad</c>'s <c>maneuver</c> block: what the demo checkers read
    /// (the drill by name, its phase by name, the history with the event that moved each phase, each member's order).
    /// </summary>
    public class TheSquadRouteShowsTheDrillTests
    {
        [Fact]
        public void CE2157_TheManeuverBlock_NamesTheDrill_ThePhase_TheCauses_AndTheOrders()
        {
            var repo = new EntityRepository();
            var commander = repo.CreateEntity();
            var a = repo.CreateEntity();
            var b = repo.CreateEntity();

            SquadDrillTrace.Started(repo, commander, BoundingOverwatchManeuver.ManeuverKind, BoundingOverwatchManeuver.PhaseElement0Moving, 1.0);
            SquadDrillTrace.Issued(repo, commander, new[]
            {
                new SquadDrillTrace.Order(a, (byte)MemberOrderKind.AdvanceTo, new Vector3(1, 30, 0), true, 1.0),
                new SquadDrillTrace.Order(b, 255, default, false, 1.0),
            });
            SquadDrillTrace.PhaseChanged(repo, commander, BoundingOverwatchManeuver.ManeuverKind, BoundingOverwatchManeuver.PhaseElement1Moving,
                SquadDrillTrace.EventTimeout, 6.0);

            var node = (JsonObject)DebugApiService.DrillNode(SquadDrillTrace.Of(repo, commander), e => new JsonObject { ["id"] = e.Index })!;

            Assert.Equal("BoundingOverwatch", (string)node["kindName"]!);
            Assert.Equal("Element1Moving", (string)node["phaseName"]!);
            var history = (JsonArray)node["history"]!;
            Assert.Equal("Start", (string)history[0]!["event"]!);
            Assert.Equal("Timeout", (string)history[1]!["event"]!);
            var members = (JsonArray)node["members"]!;
            Assert.Equal("AdvanceTo", (string)members[0]!["order"]!["kind"]!);
            Assert.True((bool)members[0]!["order"]!["cover"]!);
            Assert.Null(members[1]!["order"]);   // no order for that slot
            Assert.Null(node["gates"]);
        }

        [Fact]
        public void CE2157_ACommanderThatNeverRanADrill_HasNoManeuverBlock()
        {
            var repo = new EntityRepository();
            var commander = repo.CreateEntity();
            Assert.Null(DebugApiService.DrillNode(SquadDrillTrace.Of(repo, commander), _ => new JsonObject()));
        }
    }
}
