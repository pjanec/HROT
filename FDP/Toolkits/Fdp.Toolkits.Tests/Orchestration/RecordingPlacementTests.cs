using System.Linq;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests
{
    /// <summary>
    /// ⭐⭐ CE-4331 ② / ⑤ (📄 docs/DESIGN_Recording_Storage.md §9a, the case table — one rail per row) — the ONE placement of an exercise's recordings on
    /// the CURRENT cluster: by name, then by kind, never across kinds; the extras listen; only "nothing placeable" is refused.
    /// </summary>
    public sealed class RecordingPlacementTests
    {
        private const int Brain = 1, Muscle = 2, Fused = Brain | Muscle;

        private static RecordedFile F(int id, string name, string sub, int roles = Brain) => new($"node_{id}.fdp", id, name, sub, roles);
        private static PlacementNode N(int id, string name, string sub, int roles = Brain) => new(id, name, sub, roles);
        private static int[] PlacedOn(Placement p, string file) => p.Placed.Where(x => x.File.FileName == file).Select(x => x.NodeId).ToArray();

        /// <summary>The same cluster with names configured — each node its own file. 🔴 Mutation: the name pass skipped ⇒ by kind the lowest id wins ⇒ swapped ⇒ red.</summary>
        [Fact]
        public void CE4331_TheSameCluster_ByName_EachNodeItsOwnFile()
        {
            var p = RecordingPlacement.Place(new[] { F(401, "cgf-a", "CGF"), F(402, "cgf-b", "CGF") }, new[] { N(400, "cgf-b", "CGF"), N(401, "cgf-a", "CGF") });
            Assert.Equal(new[] { 401 }, PlacedOn(p, "node_401.fdp"));
            Assert.Equal(new[] { 400 }, PlacedOn(p, "node_402.fdp"));
            Assert.Empty(p.Unplaced);
            Assert.Empty(p.Listeners);
        }

        /// <summary>Names not configured (ids granted anew): by kind, one file per free node of the kind.</summary>
        [Fact]
        public void CE4331_NamesNotConfigured_ByKind_OneFilePerNode()
        {
            var p = RecordingPlacement.Place(new[] { F(401, "CGF-401", "CGF"), F(1, "SimHost-1", "SimHost", Muscle) },
                                             new[] { N(10401, "CGF-10401", "CGF"), N(10002, "SimHost-10002", "SimHost", Muscle) });
            Assert.Equal(new[] { 10401 }, PlacedOn(p, "node_401.fdp"));
            Assert.Equal(new[] { 10002 }, PlacedOn(p, "node_1.fdp"));
            Assert.False(p.Refused);
        }

        /// <summary>Fewer nodes of a kind (CGF ×3 recorded, ×2 now): two placed, the third NOT played (named at the AAR start). More nodes: the extras listen.</summary>
        [Fact]
        public void CE4331_FewerNodesOfAKind_LeaveAFileUnplaced_AndMoreNodesListen()
        {
            var three = new[] { F(400, "", "CGF"), F(401, "", "CGF"), F(402, "", "CGF") };
            var fewer = RecordingPlacement.Place(three, new[] { N(400, "x", "CGF"), N(401, "y", "CGF") });
            Assert.Equal(2, fewer.Placed.Count);
            Assert.Equal("node_402.fdp", Assert.Single(fewer.Unplaced).FileName);

            var more = RecordingPlacement.Place(new[] { F(400, "", "CGF") }, new[] { N(400, "x", "CGF"), N(401, "y", "CGF"), N(600, "ig", "IG", 0) });
            Assert.Single(more.Placed);
            Assert.Equal(new[] { 401, 600 }, more.Listeners);
        }

        /// <summary>
        /// ⛔ Fused ↔ split never match: a fused Brain|MuscleGround file does not play on a pure Brain node, nor a Brain file on a fused one; with nothing else
        /// placeable the AAR is REFUSED in the cluster. 🔴 Mutation: SameKind ignoring the roles ⇒ placed ⇒ red.
        /// </summary>
        [Fact]
        public void CE4331_FusedAndSplit_NeverMatch_AndNothingPlaceableIsRefused()
        {
            var p = RecordingPlacement.Place(new[] { F(1, "", "SimHost", Fused) }, new[] { N(1, "", "SimHost", Brain), N(2, "", "SimHost", Muscle) });
            Assert.True(p.Refused);
            Assert.NotNull(p.Refusal);
            Assert.Single(p.Unplaced);
            Assert.Equal(new[] { 1, 2 }, p.Listeners);
        }

        /// <summary>A kind entirely absent (no Player node now): its file is not played; the other kinds still play.</summary>
        [Fact]
        public void CE4331_AnAbsentKind_IsNotPlayed_TheOthersAre()
        {
            var p = RecordingPlacement.Place(new[] { F(100, "p1", "Player", 0), F(400, "c", "CGF") }, new[] { N(400, "c", "CGF") });
            Assert.False(p.Refused);
            Assert.Equal("node_100.fdp", Assert.Single(p.Unplaced).FileName);
        }

        /// <summary>A file recorded before CE-4331 (no kind in its metadata) keeps the old rule: the node of its id, else unplaced.</summary>
        [Fact]
        public void CE4331_ALegacyFile_GoesToTheNodeOfItsId()
        {
            var p = RecordingPlacement.Place(new[] { F(3, "", ""), F(9, "", "") }, new[] { N(3, "a", "CGF"), N(4, "b", "CGF") });
            Assert.Equal(new[] { 3 }, PlacedOn(p, "node_3.fdp"));
            Assert.Equal("node_9.fdp", Assert.Single(p.Unplaced).FileName);
            Assert.Equal(new[] { 4 }, p.Listeners);
        }
    }
}
