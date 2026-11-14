using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common.Components;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ S0 items 1–2 (<c>docs/DESIGN_Scenario_Rules.md</c> §9.8.0 / §9.8.8–§9.8.10) — the internal types are byte- and JSON-identical to the
    /// <see cref="Vector3"/> / <see cref="Quaternion"/> / <see cref="float"/> they replace; the authored types read every JSON form, keep the
    /// form through a round trip, and write by the value's state; <see cref="AreaLink.Contains"/> is right for all three shapes.
    /// </summary>
    public sealed class S0TypesTests
    {
        private static readonly JsonSerializerOptions File = FdpJsonOptionsRegistry.DefaultRelaxed;

        private static string Json<T>(T v) => JsonSerializer.Serialize(v, File);
        private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, File)!;

        // ── the internal types ────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Internal_TypesHaveTheMemoryOfWhatTheyReplace()
        {
            Assert.Equal(Unsafe.SizeOf<Vector3>(), Unsafe.SizeOf<WorldPoint>());
            Assert.Equal(Unsafe.SizeOf<Quaternion>(), Unsafe.SizeOf<WorldRotation>());
            Assert.Equal(sizeof(float), Unsafe.SizeOf<Bearing>());
            var v = new Vector3(1.5f, -2f, 3.25f);
            WorldPoint p = v;
            Assert.Equal(v, Unsafe.As<WorldPoint, Vector3>(ref p));   // same bytes, same order
        }

        [Fact]
        public void Internal_JsonIsExactlyTheReplacedType_InFilesAndThroughTheOptions()
        {
            var v = new Vector3(12.5f, -3f, 0.125f);
            Assert.Equal(Json(v), Json((WorldPoint)v));
            Assert.Equal(v, (Vector3)Read<WorldPoint>(Json(v)));
            var q = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
            Assert.Equal(Json(q), Json((WorldRotation)q));
            Assert.Equal(q, (Quaternion)Read<WorldRotation>(Json(q)));
            Assert.Equal(Json(42.5f), Json((Bearing)42.5f));
            Assert.Equal(42.5f, (float)Read<Bearing>("42.5"));
        }

        private sealed class Holder { public WorldPoint P; public Vector3 V; }

        [Fact]
        public void Internal_AFieldOfTheTypeSerialisesLikeAVector3Field()
        {
            var h = new Holder { P = new Vector3(1, 2, 3), V = new Vector3(1, 2, 3) };
            var node = System.Text.Json.Nodes.JsonNode.Parse(Json(h))!;
            Assert.Equal(node["V"]!.ToJsonString(), node["P"]!.ToJsonString());
        }

        [Fact]
        public void WorldPoint_Arithmetic_PointMinusPointIsADisplacement_PointPlusDisplacementIsAPoint()
        {
            WorldPoint a = new Vector3(10, 20, 0), b = new Vector3(4, 5, 0);
            Vector3 d = a - b;
            Assert.Equal(new Vector3(6, 15, 0), d);
            WorldPoint c = b + d;
            Assert.Equal(a, c);
            Assert.Equal(new Vector3(7, 12.5f, 0), (a + b) * 0.5f);   // a midpoint
            Assert.Equal(Vector3.Distance(a, b), d.Length());
        }

        [Theory]
        [InlineData(0f)] [InlineData(45f)] [InlineData(90f)] [InlineData(180f)] [InlineData(271.5f)]
        public void WorldRotation_HeadingIsTheBridgesConvention_BothWays(float heading)
        {
            var r = WorldRotation.FromHeadingDeg(heading);
            Assert.Equal(heading, r.HeadingDeg, 3);
            Assert.Equal(SimTransformBridgeSystem.RotationToHeadingDeg(r), r.HeadingDeg);         // the bridge routes here: one copy
            Assert.Equal((Quaternion)r, SimTransformBridgeSystem.HeadingDegToRotation(heading));
            Assert.Equal(heading, ((Bearing)heading).ToRotation().HeadingDeg, 3);
        }

        [Fact]
        public void WorldRotation_TextIsTheOneOrientationDisplay()
        {
            var r = WorldRotation.FromHeadingDeg(45f);
            Assert.StartsWith("HPR [045.0, 0.0, 0.0] deg, Quat [", r.ToString());
            Assert.Equal("HPR [090.0, 0.0, 0.0] deg, Quat [0.000, 0.000, 0.000, 1.000]", WorldRotation.Identity.ToString());
            var q = (Quaternion)r;
            float w = q.W < 0 ? -1 : 1;   // the wire carries the vector part with w >= 0
            Assert.Equal(OrientationDisplay.Format(q.X * w, q.Y * w, q.Z * w), OrientationText.Format(new Quaternion(q.X * w, q.Y * w, q.Z * w, q.W * w)));
        }

        // ── RefSpec ───────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RefSpec_Is36UnmanagedBytes_KeyAndGuidOverlay_AndALongKeyIsRefusedLoudly()
        {
            Assert.Equal(36, Unsafe.SizeOf<RefSpec>());
            Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<RefSpec>());
            Assert.Equal("@BlueHQ", RefSpec.ByKey("@BlueHQ").ToString());
            Assert.Equal("BluePlatoon/SL", RefSpec.ByKey("BluePlatoon/SL").KeyText);
            var g = Guid.NewGuid();
            Assert.Equal(g, RefSpec.ByExact(g).Exact);
            Assert.True(RefSpec.ByKey("A") == RefSpec.ByKey("@A"));
            Assert.False(RefSpec.ByKey("A") == RefSpec.ByKey("B"));
            RefSpec.ByKey(new string('k', 31));
            var ex = Assert.Throws<ArgumentException>(() => RefSpec.ByKey(new string('k', 32)));
            Assert.Contains("at most 31", ex.Message);
            Assert.False(RefSpec.TryParse("12", out _));
        }

        // ── EntityLink ────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void EntityLink_ReadsEveryForm_AndWritesByItsState()
        {
            var g = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");
            Assert.Equal(new EntityLink(new EntityRef(9812)), Read<EntityLink>("9812"));
            Assert.Equal(new EntityLink(EntityRef.None, RefSpec.ByKey("BlueHQ")), Read<EntityLink>("\"@BlueHQ\""));
            Assert.Equal(new EntityLink(EntityRef.None, RefSpec.ByExact(g)), Read<EntityLink>($"\"{g}\""));
            Assert.Equal(new EntityLink(new EntityRef(9812), RefSpec.ByKey("BlueHQ")), Read<EntityLink>("{\"NetworkId\": 9812, \"Ref\": \"@BlueHQ\"}"));
            Assert.Equal(EntityLink.None, Read<EntityLink>("null"));

            Assert.Equal("9812", Json(new EntityLink(new EntityRef(9812))));                                    // not authored: unchanged
            Assert.Equal("\"@BlueHQ\"", Json(new EntityLink(EntityRef.None, RefSpec.ByKey("BlueHQ"))));          // authored, unresolved
            Assert.Equal("{\"NetworkId\":9812,\"Ref\":\"@BlueHQ\"}", Json(new EntityLink(new EntityRef(9812), RefSpec.ByKey("BlueHQ"))));
            var both = new EntityLink(new EntityRef(9812), RefSpec.ByExact(g));
            Assert.Equal(both, Read<EntityLink>(Json(both)));                                                    // every output reads back
            Assert.Equal($"{{\"NetworkId\":9812,\"Ref\":\"{g:D}\"}}", Json(both));                                // ⭐ CE-4206 R1: what an Exact pick saves — the GUID form (S9′)
            Assert.Throws<JsonException>(() => Read<EntityLink>("\"BlueHQ\""));                                  // no @, not a GUID: loud
        }

        [Fact]
        public void EntityLink_Text_ShowsTheFormAndTheAnswer()
        {
            Assert.Equal("@BlueHQ → #9812", new EntityLink(new EntityRef(9812), RefSpec.ByKey("BlueHQ")).ToString());
            Assert.Equal("@BlueHQ → unresolved", new EntityLink(EntityRef.None, RefSpec.ByKey("BlueHQ")).ToString());
            Assert.Equal("#9812", ((EntityLink)new EntityRef(9812)).ToString());
        }

        // ── PointLink ─────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void PointLink_Coordinates_AreExactlyAVector3_AndAnEntityFormKeepsItsSpec()
        {
            var v = new Vector3(100, 200, 5);
            Assert.Equal(Json(v), Json((PointLink)v));
            Assert.Equal(v, (Vector3)Read<PointLink>(Json(v)));
            var key = Read<PointLink>("\"@Crossroads\"");
            Assert.Equal(RefForm.Key, key.Spec.Form);
            Assert.Equal("\"@Crossroads\"", Json(key));                                                         // unresolved: the text
            var resolved = key.WithLocal(v);
            Assert.Equal(resolved, Read<PointLink>(Json(resolved)));                                            // both, back
            Assert.Contains("\"Ref\":\"@Crossroads\"", Json(resolved));
        }

        /// <summary>⭐ CE-4575 (X-9) — a point reads as the send-time check accepts it: <c>[x, y]</c> (z = 0) or <c>[x, y, z]</c>; anything else
        /// is LOUD. 🔴 Red-proof: the options' three-number <c>Vector3</c> reader back ⇒ <c>[30, -40]</c> throws.</summary>
        [Fact]
        public void PointLink_ReadsTwoOrThreeNumbers_AndRefusesAnythingElse()
        {
            Assert.Equal(new Vector3(30, -40, 0), (Vector3)Read<PointLink>("[30, -40]"));
            Assert.Equal(new Vector3(30, -40, 2), (Vector3)Read<PointLink>("{\"Local\":[30, -40, 2]}"));
            Assert.Throws<JsonException>(() => Read<PointLink>("[1]"));
            Assert.Throws<JsonException>(() => Read<PointLink>("[1, 2, 3, 4]"));
            Assert.Throws<JsonException>(() => Read<PointLink>("[\"a\", \"b\"]"));
        }

        // ── AreaLink ──────────────────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void AreaLink_CircleAndRotatedRectangle_RoundTrip_AndContainmentIsRight()
        {
            var circle = Read<AreaLink>("{\"circle\": {\"center\": [10, 0, 0], \"radius\": 5}}");
            Assert.True(circle.Contains(null, new Vector3(14, 0, 99)));
            Assert.False(circle.Contains(null, new Vector3(16, 0, 0)));
            Assert.Equal(circle, Read<AreaLink>(Json(circle)));

            // 2 m wide (across), 20 m long, its length along 90° (east): it reaches x = ±10, y = ±1
            var rect = Read<AreaLink>("{\"rect\": {\"center\": [0, 0, 0], \"size\": [2, 20], \"heading\": 90}}");
            Assert.True(rect.Contains(null, new Vector3(9, 0.5f, 0)));
            Assert.False(rect.Contains(null, new Vector3(0, 5, 0)));
            var north = AreaLink.Rectangle(WorldPoint.Zero, 2, 20, 0f);                                       // heading 0: long north-south
            Assert.True(north.Contains(null, new Vector3(0.5f, 9, 0)));
            Assert.False(north.Contains(null, new Vector3(9, 0, 0)));
            Assert.Equal(rect, Read<AreaLink>(Json(rect)));
        }

        [Fact]
        public void AreaLink_AnAreaEntity_IsItsOutline_ThroughTheOneContainment()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<EditablePolyline>();
            var village = repo.CreateEntity();
            repo.AddComponent(village, new NetworkIdentity(4242));
            repo.AddComponent(village, new SimTransform { Position = new Vector3(100, 100, 0) });
            repo.SetManagedComponent(village, new EditablePolyline { Points = new List<Vector2> { new(-10, -10), new(10, -10), new(10, 10), new(-10, 10) } });

            var area = Read<AreaLink>("{\"Area\": 4242, \"Ref\": \"@Village\"}");
            Assert.Equal(RefForm.Key, area.Spec.Form);
            Assert.True(area.Contains(repo, new Vector3(105, 95, 0)));
            Assert.False(area.Contains(repo, new Vector3(5, 5, 0)));
            Assert.False(Read<AreaLink>("\"@Village\"").Contains(repo, new Vector3(105, 95, 0)));             // unresolved: contains nothing
            Assert.Equal("\"@Village\"", Json(AreaLink.OfEntity(EntityRef.None, RefSpec.ByKey("Village"))));
        }
    }
}
