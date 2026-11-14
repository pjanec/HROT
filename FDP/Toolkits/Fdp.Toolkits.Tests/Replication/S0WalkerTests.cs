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
            public readonly Dictionary<string, (int Id, Vector3 At)> Keys = new();
            public readonly Dictionary<Guid, (int Id, Vector3 At)> Guids = new();
            public string Describe => "test scope";
            public bool TryResolve(in RefSpec spec, out EntityRef entity)
            {
                (int Id, Vector3 At) hit = default;
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
            var map = new Dictionary<int, int> { [7] = 70, [8] = 80 };
            const string json = "{\"Target\":{\"NetworkId\":7,\"Ref\":\"@BlueHQ\"},\"Zone\":8,\"Nested\":{\"Escort\":7},\"Facing\":30}";
            var outJson = EntityRefRemap.CompileJson(typeof(OrderParams))(json, map)!;
            var o = JsonNode.Parse(outJson)!;
            Assert.Equal(70, (long)o["Target"]!["NetworkId"]!);
            Assert.Equal("@BlueHQ", (string)o["Target"]!["Ref"]!);
            Assert.Equal(80, (long)o["Zone"]!);
            Assert.Equal(70, (long)o["Nested"]!["Escort"]!);
            Assert.Equal(30, (double)o["Facing"]!);
            Assert.Same(json, EntityRefRemap.CompileJson(typeof(OrderParams))(json, new Dictionary<int, int> { [99] = 1 }));   // nothing to do: same string
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

        /// <summary>⭐⭐ <c>CE-4301</c> (mission plan G2, §5.7) — a mission task's CONDITION is walked by the one walker over the condition language
        /// (polymorphic: a <c>TaskFinished</c> inside a <c>Compound</c>): its <c>@Key</c> resolves in the scenario's scope, the form kept; a key that
        /// names nothing fails the load. ⛔ Before G2 the load walked a task's params only.</summary>
        [Fact]
        public void CE4301_AMissionConditionsReferences_ResolveOnLoad_AndAMissingKeyIsLoud()
        {
            var scope = new Scope();
            scope.Keys["Bravo12"] = (4321, Vector3.Zero);
            string cond = "{\"$type\":\"Compound\",\"Operator\":\"And\",\"Conditions\":[{\"$type\":\"TaskFinished\",\"Unit\":\"@Bravo12\",\"Task\":3}]}";

            var outJson = Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper.ResolveConditionJson(cond, scope)!;
            var dto = (Fdp.Toolkit.ReplayBrowser.Search.CompoundPredicateDto)Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.Parse(outJson);
            var leaf = Assert.IsType<Fdp.Toolkit.ReplayBrowser.Search.TaskFinishedPredicateDto>(dto.Conditions[0]);
            Assert.Equal(new EntityRef(4321), leaf.Unit.Ref);
            Assert.Equal("@Bravo12", leaf.Unit.Spec.ToString());
            Assert.Equal(3, leaf.Task);

            Assert.Throws<InvalidOperationException>(() => Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper.ResolveConditionJson(cond.Replace("Bravo12", "Nobody"), scope));
        }

        // a contract one of whose fields was renamed (S0 item 5: targetLat/targetLon → Target)
        [RetiredJsonKeys("targetLat", "targetLon")]
        public sealed class RenamedParams { public PointLink Target; }

        [Fact]
        public void Resolve_AtLoad_RefusesAnOldForm_ARetiredKeyOrALeafItsConverterCannotRead_NamingIt_WhileRemapLeavesBoth()
        {
            var scope = new Scope();
            // ⛔ CE-4153 S10 — a [lat, lon] pair where a PointLink is: LOUD at load, naming the key
            var old = Assert.Throws<System.Text.Json.JsonException>(() =>
                EntityRefRemap.WalkJson(typeof(OrderParams), "{\"Nested\":{\"At\":[52.5,13.4]}}", new RefResolve(scope)));
            Assert.Contains("'At'", old.Message);
            Assert.Contains("hrot-content-migrate", old.Message);
            // … and inside a list
            Assert.Throws<System.Text.Json.JsonException>(() =>
                EntityRefRemap.WalkJson(typeof(OrderParams), "{\"Route\":[[1,1,0],[52.5,13.4]]}", new RefResolve(scope)));
            // ⛔ a key the contract retired: LOUD at load, naming it
            var key = Assert.Throws<System.Text.Json.JsonException>(() =>
                EntityRefRemap.WalkJson(typeof(RenamedParams), "{\"TargetLat\":52.5,\"targetLon\":13.4}", new RefResolve(scope)));
            Assert.Contains("TargetLat", key.Message);
            // ⭐ the new forms load
            Assert.NotNull(EntityRefRemap.WalkJson(typeof(RenamedParams), "{\"Target\":[1,2,0]}", new RefResolve(scope)));
            // ⭐ every OTHER applier keeps the old leniency: a saved blob is remapped, never refused
            var remapped = JsonNode.Parse(EntityRefRemap.CompileJson(typeof(OrderParams))(
                "{\"Nested\":{\"At\":[52.5,13.4]},\"Target\":7}", new Dictionary<int, int> { [7] = 70 })!)!;
            Assert.Equal(70, (long)remapped["Target"]!);
            Assert.Equal("[52.5,13.4]", remapped["Nested"]!["At"]!.ToJsonString());
            const string retired = "{\"targetLat\":1}";
            Assert.Same(retired, EntityRefRemap.WalkJson(typeof(RenamedParams), retired, new RefRemap(new Dictionary<int, int>())));
        }

        [Fact]
        public void VisitLeaves_SeesEachLeafRaw_WithItsTypeAndPath_AndAReturnReplacesIt()
        {
            var obj = JsonNode.Parse("{\"Objective\":[52.5,13.4],\"Route\":[[1,1,0],[52.6,13.5]],\"Nested\":{\"At\":[0,0],\"Escort\":7},\"Facing\":30}")!.AsObject();
            var seen = new List<string>();
            bool changed = EntityRefRemap.VisitLeaves(typeof(OrderParams), obj, (type, node, path) =>
            {
                seen.Add($"{type.Name}:{path}");
                return type == typeof(PointLink) && node is JsonArray { Count: 2 } ? new JsonArray(9, 9, 0) : null;
            });
            Assert.True(changed);
            Assert.Equal(new[] { "PointLink:Objective", "PointLink:Route[0]", "PointLink:Route[1]", "PointLink:Nested.At", "EntityLink:Nested.Escort", "Bearing:Facing" }, seen);
            Assert.Equal("[9,9,0]", obj["Objective"]!.ToJsonString());
            Assert.Equal("[1,1,0]", obj["Route"]![0]!.ToJsonString());   // a 3-element place is not the old form
            Assert.Equal("[9,9,0]", obj["Route"]![1]!.ToJsonString());
            Assert.Equal("[9,9,0]", obj["Nested"]!["At"]!.ToJsonString());
            Assert.False(EntityRefRemap.VisitLeaves(typeof(OrderParams), obj, (_, _, _) => null));
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
