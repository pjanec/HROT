using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// CE-277(c2) — rails for the distributed-save merge. Proves the four §4b invariants as executable
    /// behaviour: disjoint entity union, single globals source, foreign routing, and the fail-loud guards.
    /// </summary>
    public class ScenarioMergeCoreTests
    {
        private const string OurType = "Hrot.Scenario";

        // Builds a compatible slice DOM: {$meta, Header?, Entities}.
        private static JsonObject Dom(
            IEnumerable<string> entityGuids,
            string? tkbName = null,
            int schemaVersion = 2,
            string? terrainName = null)
        {
            var entities = new JsonObject();
            foreach (var g in entityGuids)
                entities[g] = new JsonObject { ["EntityInfo"] = new JsonObject { ["Name"] = g } };

            var dom = new JsonObject { ["Entities"] = entities };
            if (tkbName != null || terrainName != null)
            {
                var header = new JsonObject();
                if (tkbName != null)     header["TkbName"]     = JsonValue.Create(tkbName);
                if (terrainName != null) header["TerrainName"] = JsonValue.Create(terrainName);
                dom["Header"] = header;
            }
            JsonEnvelope.Write(dom, new DocumentMeta(OurType, schemaVersion));
            return dom;
        }

        private static ScenarioSlice Compatible(int node, JsonObject dom) => new(node, OurType, dom);
        private static ScenarioSlice Foreign(int node, string docType)    => new(node, docType, null);

        [Fact]
        public void DisjointEntities_Union_IntoOneCanonicalFile()   // I1 + I2
        {
            var brain  = Compatible(400, Dom(new[] { "g-a", "g-b" }, tkbName: "tkb-1"));
            var ig     = Compatible(100, Dom(new[] { "g-c" }));         // IG live-authored one
            var muscle = Compatible(1,   Dom(new string[0]));           // owns nothing → empty

            var result = ScenarioMergeCore.Merge(new[] { brain, ig, muscle }, OurType);

            Assert.NotNull(result.CanonicalDom);
            Assert.Empty(result.Foreign);
            var ents = (JsonObject)result.CanonicalDom!["Entities"]!;
            Assert.Equal(3, ents.Count);
            Assert.True(ents.ContainsKey("g-a") && ents.ContainsKey("g-b") && ents.ContainsKey("g-c"));
            // $meta preserved, Header carried from the slice that had it.
            Assert.Equal(OurType, JsonEnvelope.Read((JsonObject)result.CanonicalDom!).DocType);
            Assert.Equal("tkb-1", (string)((JsonObject)result.CanonicalDom!["Header"]!)["TkbName"]!);
        }

        /// <summary>
        /// ⭐ RE-HOMED from <c>Zones_TakenFromTheSingleBrainSource</c> (F2/F3). The old test asserted that
        /// a brain-only <c>Zones</c> SECTION survived the merge. That section is retired — a zone is an
        /// ordinary authored entity — so the claim worth keeping is the same one restated on the surface
        /// that now carries it: <b>the brain's zone reaches the canonical file, through the ordinary
        /// entity union and with no special case.</b>
        ///
        /// <para>⛔ The old assertion is NOT merely deleted: had it been, nothing would check that zones
        /// still survive a distributed save at all.</para>
        ///
        /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §5.1, §6 (retirement).
        /// </summary>
        [Fact]
        public void AZoneEntityFromTheBrain_ReachesTheCanonicalFile_ThroughTheOrdinaryEntityUnion()
        {
            var brain  = Compatible(400, Dom(new[] { "zone-urban", "g-a" }, tkbName: "tkb-1"));
            var muscle = Compatible(1,   Dom(new[] { "g-b" }));

            var result = ScenarioMergeCore.Merge(new[] { brain, muscle }, OurType);

            var entities = (JsonObject)result.CanonicalDom!["Entities"]!;
            Assert.True(entities.ContainsKey("zone-urban"));
            Assert.Equal(3, entities.Count);

            // ⛔ And no `Zones` section is reconstructed — a second home for the same truth is exactly
            //   what the retirement removed (§5.1, two producers for one slot).
            Assert.Null(result.CanonicalDom!["Zones"]);
        }

        [Fact]
        public void ForeignSlice_IsRouted_NotMerged_AndNeverParsed()   // §4c
        {
            var brain  = Compatible(400, Dom(new[] { "g-a" }));
            var excon  = Foreign(500, "ExCon.Observer");   // Dom == null: never parsed

            var result = ScenarioMergeCore.Merge(new[] { brain, excon }, OurType);

            Assert.NotNull(result.CanonicalDom);
            Assert.Single(result.Foreign);
            Assert.Equal(500, result.Foreign[0].OriginNodeId);
            Assert.Equal("ExCon.Observer", result.Foreign[0].DocType);
            // the foreign entity is NOT in the canonical file
            Assert.Single((JsonObject)result.CanonicalDom!["Entities"]!);
        }

        [Fact]
        public void AllForeign_YieldsNoCanonicalFile()
        {
            var a = Foreign(500, "ExCon.Observer");
            var b = Foreign(600, "SomeOther.Format");

            var result = ScenarioMergeCore.Merge(new[] { a, b }, OurType);

            Assert.Null(result.CanonicalDom);
            Assert.Equal(2, result.Foreign.Count);
        }

        [Fact]
        public void SchemaMismatch_FailsLoud()
        {
            var a = Compatible(400, Dom(new[] { "g-a" }, schemaVersion: 2));
            var b = Compatible(1,   Dom(new[] { "g-b" }, schemaVersion: 3));

            Assert.Throws<System.InvalidOperationException>(() =>
                ScenarioMergeCore.Merge(new[] { a, b }, OurType));
        }

        [Fact]
        public void TkbNameMismatch_FailsLoud()
        {
            var a = Compatible(400, Dom(new[] { "g-a" }, tkbName: "tkb-1"));
            var b = Compatible(1,   Dom(new[] { "g-b" }, tkbName: "tkb-2"));

            Assert.Throws<System.InvalidOperationException>(() =>
                ScenarioMergeCore.Merge(new[] { a, b }, OurType));
        }

        // ⛔ DELETED (F2/F3): `TwoZonesSources_FailLoud` — the I4 guard it drove no longer exists.
        //
        //   Its claim was "two slices both carrying zones is a corruption, fail rather than pick one".
        //   With zones as entities that situation is not a special case any more: two slices carrying
        //   the same zone collide on the entity GUID and fail at `GuidCollision_FailsLoud` below, with a
        //   better message. The claim is therefore RE-HOMED onto a test that already exists, rather than
        //   dropped — which is why nothing was written to replace it.
        //   ⚠ Two slices carrying DIFFERENT zones is now simply a legal union, and that is the intended
        //   behaviour change: zones are authored entities and authoring is not brain-only.

        [Fact]
        public void GuidCollision_FailsLoud()   // I2 guard (should be impossible; never overwrite)
        {
            var a = Compatible(400, Dom(new[] { "dup", "g-a" }));
            var b = Compatible(1,   Dom(new[] { "dup" }));

            Assert.Throws<System.InvalidOperationException>(() =>
                ScenarioMergeCore.Merge(new[] { a, b }, OurType));
        }

        [Fact]
        public void CompatibleSliceWithNullDom_IsRejected()
        {
            var bad = new ScenarioSlice(400, OurType, Dom: null);
            Assert.Throws<System.ArgumentException>(() =>
                ScenarioMergeCore.Merge(new[] { bad }, OurType));
        }

        // ── B5: the TERRAIN NAME is a global and must survive the merge ───────────────────────────

        [Fact]
        public void TerrainName_SurvivesTheMerge_AlongsideTkbName()
        {
            // ⛔ THE REGRESSION THIS PINS: the canonical Header used to be rebuilt as
            //    `new JsonObject { ["TkbName"] = ... }`, which would DROP the terrain name from every
            //    distributed save — silently, and only on the cluster path.
            var brain  = Compatible(400, Dom(new[] { "g-a" }, tkbName: "tkb-1", terrainName: "kandahar"));
            var muscle = Compatible(1,   Dom(new[] { "g-b" }));

            var result = ScenarioMergeCore.Merge(new[] { brain, muscle }, OurType);

            var header = (JsonObject)result.CanonicalDom!["Header"]!;
            Assert.Equal("tkb-1",    (string)header["TkbName"]!);
            Assert.Equal("kandahar", (string)header["TerrainName"]!);
        }

        [Fact]
        public void TerrainName_WithNoTkbName_StillReachesTheCanonicalHeader()
        {
            var brain = Compatible(400, Dom(new[] { "g-a" }, terrainName: "kandahar"));

            var result = ScenarioMergeCore.Merge(new[] { brain }, OurType);

            var header = (JsonObject)result.CanonicalDom!["Header"]!;
            Assert.Equal("kandahar", (string)header["TerrainName"]!);
            Assert.Null(header["TkbName"]);
        }

        [Fact]
        public void TerrainName_MismatchAcrossSlices_FailsLoudly()
        {
            // ⭐ Terrain is a GLOBAL fact about the exercise, like the TKB: two nodes disagreeing about
            //   which terrain they are on is a misconfiguration to surface, not a merge to reconcile.
            var a = Compatible(400, Dom(new[] { "g-a" }, terrainName: "kandahar"));
            var b = Compatible(1,   Dom(new[] { "g-b" }, terrainName: "helmand"));

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                ScenarioMergeCore.Merge(new[] { a, b }, OurType));

            Assert.Contains("TerrainName", ex.Message);
        }
    }
}
