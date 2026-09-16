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

        // Builds a compatible slice DOM: {$meta, Header?, Entities, Zones?}.
        private static JsonObject Dom(
            IEnumerable<string> entityGuids,
            string? tkbName = null,
            int schemaVersion = 2,
            JsonArray? zones = null)
        {
            var entities = new JsonObject();
            foreach (var g in entityGuids)
                entities[g] = new JsonObject { ["EntityInfo"] = new JsonObject { ["Name"] = g } };

            var dom = new JsonObject { ["Entities"] = entities };
            if (tkbName != null)
                dom["Header"] = new JsonObject { ["TkbName"] = JsonValue.Create(tkbName) };
            JsonEnvelope.Write(dom, new DocumentMeta(OurType, schemaVersion));
            if (zones != null)
                dom["Zones"] = zones;
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

        [Fact]
        public void Zones_TakenFromTheSingleBrainSource()   // I4
        {
            var zones  = new JsonArray { new JsonObject { ["Name"] = "urban" } };
            var brain  = Compatible(400, Dom(new[] { "g-a" }, tkbName: "tkb-1", zones: zones));
            var muscle = Compatible(1,   Dom(new[] { "g-b" }));   // no zones

            var result = ScenarioMergeCore.Merge(new[] { brain, muscle }, OurType);

            var z = (JsonArray)result.CanonicalDom!["Zones"]!;
            Assert.Single(z);
            Assert.Equal("urban", (string)((JsonObject)z[0]!)["Name"]!);
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

        [Fact]
        public void TwoZonesSources_FailLoud()   // I4 guard
        {
            var z1 = new JsonArray { new JsonObject { ["Name"] = "a" } };
            var z2 = new JsonArray { new JsonObject { ["Name"] = "b" } };
            var a = Compatible(400, Dom(new[] { "g-a" }, zones: z1));
            var b = Compatible(1,   Dom(new[] { "g-b" }, zones: z2));

            Assert.Throws<System.InvalidOperationException>(() =>
                ScenarioMergeCore.Merge(new[] { a, b }, OurType));
        }

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
    }
}
