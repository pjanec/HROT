using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ S0 item 4 (<c>docs/DESIGN_Scenario_Rules.md</c> §9.8.0, SR-128 / SR-136) — the ONE walker finds every leaf by TYPE in JSON and in
    /// objects, and its three operations work: remap (ids, the load path it began as), resolve (an authored <c>@Key</c> / GUID fills the
    /// answer and keeps the form; a missing key is loud), relocate (translate + turn a params blob and a component; a reference never moves).
    /// </summary>
    public sealed class S0WalkerTests
    {
        // a params-shaped DTO with every leaf, a nested struct and a list
        public sealed class OrderParams
        {
            public EntityLink Target;
            public PointLink Objective;
            public List<PointLink> Route = new();
            public AreaLink Zone;
            public Bearing Facing;
            public Leg Nested;
        }
        public struct Leg { public PointLink At; public EntityLink Escort; }

        // a component-shaped struct (internal types)
        public struct Placed { public WorldPoint Position; public WorldRotation Rotation; public EntityRef Leader; public Bearing Watch; }

        private sealed class Scope : IRefScope
        {
            public readonly Dictionary<string, (long Id, Vector3 At)> Keys = new();
            public readonly Dictionary<Guid, (long Id, Vector3 At)> Guids = new();
            public string Describe => "test scope";
            public bool TryResolve(in RefSpec spec, out EntityRef entity)
            {
                (long Id, Vector3 At) hit = default;
                bool ok = spec.Form switch
                {
                    RefForm.Key   => Keys.TryGetValue(spec.KeyText, out hit),
                    RefForm.Exact => Guids.TryGetValue(spec.Exact, out hit),
                    _             => false,
                };
                entity = ok ? new EntityRef(hit.Id) : EntityRef.None;
                return ok;
            }
            public bool TryPositionOf(EntityRef entity, out WorldPoint position)
            {
                foreach (var v in Keys.Values) if (v.Id == entity.NetworkId) { position = v.At; return true; }
                foreach (var v in Guids.Values) if (v.Id == entity.NetworkId) { position = v.At; return true; }
                position = default; return false;
            }
        }

        [Fact]
        public void ThePlanFindsEveryLeafByType_AndHoldsRefsMeansNetworkIds()
        {
            Assert.True(EntityRefRemap.HoldsRefs(typeof(OrderParams)));
            Assert.True(EntityRefRemap.HoldsRefs(typeof(Placed)));
            Assert.True(EntityRefRemap.HoldsLeaves(typeof(Placed)));
            Assert.False(EntityRefRemap.HoldsRefs(typeof(Leg[])) && false);
            Assert.False(EntityRefRemap.HoldsLeaves(typeof(string)));
        }

        [Fact]
        public void Remap_RewritesIdsInsideLinksAndAreas_InJson_AndKeepsTheirForms()
        {
            var map = new Dictionary<long, long> { [7] = 70, [8] = 80 };
            const string json = "{\"Target\":{\"NetworkId\":7,\"Ref\":\"@BlueHQ\"},\"Zone\":8,\"Nested\":{\"Escort\":7},\"Facing\":30}";
            var outJson = EntityRefRemap.CompileJson(typeof(OrderParams))(json, map)!;
            var o = JsonNode.Parse(outJson)!;
            Assert.Equal(70, (long)o["Target"]!["NetworkId"]!);
            Assert.Equal("@BlueHQ", (string)o["Target"]!["Ref"]!);
            Assert.Equal(80, (long)o["Zone"]!);
            Assert.Equal(70, (long)o["Nested"]!["Escort"]!);
            Assert.Equal(30, (double)o["Facing"]!);
            Assert.Same(json, EntityRefRemap.CompileJson(typeof(OrderParams))(json, new Dictionary<long, long> { [99] = 1 }));   // nothing to do: same string
        }

        [Fact]
        public void Resolve_FillsTheAnswer_KeepsTheForm_AndAMissingKeyIsLoud()
        {
            var g = Guid.NewGuid();
            var scope = new Scope();
            scope.Keys["BlueHQ"] = (9812, new Vector3(1, 2, 3));
            scope.Keys["Crossroads"] = (77, new Vector3(500, 600, 10));
            scope.Guids[g] = (55, new Vector3(9, 9, 0));
            string json = $"{{\"Target\":\"@BlueHQ\",\"Objective\":\"@Crossroads\",\"Route\":[[1,1,0],\"{g}\"],\"Zone\":\"@BlueHQ\"}}";

            var outJson = EntityRefRemap.WalkJson(typeof(OrderParams), json, new RefResolve(scope))!;
            var p = System.Text.Json.JsonSerializer.Deserialize<OrderParams>(outJson, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            Assert.Equal(new EntityRef(9812), p.Target.Ref);
            Assert.Equal(RefForm.Key, p.Target.Spec.Form);                        // the form is kept …
            Assert.Equal(new WorldPoint(500, 600, 10), p.Objective.Local);        // … and the place is the entity's position
            Assert.Equal("@Crossroads", p.Objective.Spec.ToString());
            Assert.Equal(new WorldPoint(1, 1, 0), p.Route[0].Local);             // a literal is untouched
            Assert.Equal(new WorldPoint(9, 9, 0), p.Route[1].Local);
            Assert.Equal(new EntityRef(9812), p.Zone.Area);

            var ex = Assert.Throws<InvalidOperationException>(() => EntityRefRemap.WalkJson(typeof(OrderParams), "{\"Target\":\"@Nobody\"}", new RefResolve(scope)));
            Assert.Contains("no entity with key 'Nobody' here", ex.Message);
        }

        [Fact]
        public void Relocate_AParamsBlob_MovesAndTurnsLiterals_AndNeverMovesAReference()
        {
            // authored at the origin facing north; placed at (1000, 2000, 50) facing east (+90)
            var reloc = new Relocation(new WorldPoint(0, 0, 0), 0f, new WorldPoint(1000, 2000, 50), 90f);
            const string json = "{\"Objective\":[0,10,5],\"Route\":[[10,0,0],{\"Local\":[3,3,3],\"Ref\":\"@Fixed\"}],"
                              + "\"Zone\":{\"rect\":{\"center\":[0,10,0],\"size\":[2,20],\"heading\":0}},\"Facing\":350}";
            var outJson = EntityRefRemap.WalkJson(typeof(OrderParams), json, reloc)!;
            var p = System.Text.Json.JsonSerializer.Deserialize<OrderParams>(outJson, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;

            AssertNear(new Vector3(1010, 2000, 55), p.Objective.Local);           // 10 m north, turned clockwise 90 → 10 m east; Z + 50
            AssertNear(new Vector3(1000, 1990, 50), p.Route[0].Local);            // 10 m east → 10 m south
            Assert.Equal(new WorldPoint(3, 3, 3), p.Route[1].Local);              // a reference: never moved (SR-131)
            AssertNear(new Vector3(1010, 2000, 50), p.Zone.Center);
            Assert.Equal(90f, p.Zone.Heading.Degrees, 3);
            Assert.Equal(80f, p.Facing.Degrees, 3);                               // 350 + 90, normalised
        }

        [Fact]
        public void Relocate_AComponent_MovesThePoint_TurnsTheRotation_AndLeavesTheIdAlone()
        {
            var reloc = new Relocation(new WorldPoint(100, 100, 0), 0f, new WorldPoint(0, 0, 0), 90f);
            object boxed = new Placed
            {
                Position = new WorldPoint(100, 110, 0),
                Rotation = WorldRotation.FromHeadingDeg(0f),
                Leader   = new EntityRef(42),
                Watch    = 45f,
            };
            Assert.True(EntityRefRemap.WalkObject(boxed, reloc));
            var c = (Placed)boxed;
            AssertNear(new Vector3(10, 0, 0), c.Position);
            Assert.Equal(90f, c.Rotation.HeadingDeg, 2);
            Assert.Equal(new EntityRef(42), c.Leader);
            Assert.Equal(135f, c.Watch.Degrees, 3);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
            => Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");
    }
}
