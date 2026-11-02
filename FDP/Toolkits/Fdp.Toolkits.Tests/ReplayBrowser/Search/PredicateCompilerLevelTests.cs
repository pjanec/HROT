using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    [ComponentId(481)] internal struct LvlTag { public int Value; }
    [EventId(99430)] internal struct LvlHit { public Entity Who; public int Damage; }

    /// <summary>
    /// ⭐ <c>CE-4164</c> (DESIGN_Scenario_Rules §00.9 S2-b) — <c>CompileLevelPredicate</c>: the four loop predicates answer a state, the new leaves
    /// (OnEntity / ScenarioTime / CountMatching) work in both modes, Search mode is unchanged.
    /// </summary>
    public sealed class PredicateCompilerLevelTests : IDisposable
    {
        private readonly EntityRepository _w;
        private readonly IPredicateCompiler _c;
        private static readonly AreaLink Circle = AreaLink.Circle(new WorldPoint { X = 0, Y = 0, Z = 0 }, 10f);

        public PredicateCompilerLevelTests()
        {
            _w = new EntityRepository();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterComponent<LvlTag>();
            _w.RegisterComponent<ObjectiveStatus>();
            _w.Bus.Register<LvlHit>();
            _c = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        }

        public void Dispose() => _w.Dispose();

        private Entity At(float x, float y, int net = 0)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new WorldPoint { X = x, Y = y }, Rotation = Quaternion.Identity });
            if (net != 0) _w.AddComponent(e, new NetworkIdentity(net));
            return e;
        }

        private Func<EntityRepository, Entity, bool> Level(SearchPredicateDto d) => _c.CompileLevelPredicate(d);
        private Func<EntityRepository, Entity, bool> Search(SearchPredicateDto d) => _c.CompileComponentPredicate(d);

        private static SpatialBoundingPredicateDto Spatial() => new() { Bounds = Circle };

        /// <summary>The spatial arm answers "inside Bounds"; Search mode still passes through. ✅ Red-proof: the arm returning true ⇒ the outside case red.</summary>
        [Fact]
        public void Spatial_Level_IsInsideTheBounds_SearchStaysAPassThrough()
        {
            var inside = At(2, 2); var outside = At(50, 50);
            var f = Level(Spatial());
            Assert.True(f(_w, inside));
            Assert.False(f(_w, outside));
            Assert.True(Search(Spatial())(_w, outside));   // unchanged
        }

        [Fact]
        public void Spatial_Level_ReadsACustomPositionComponent_ThroughThePropertyPaths()
        {
            var e = _w.CreateEntity();
            _w.RegisterComponent<LvlPos>();
            _w.AddComponent(e, new LvlPos { East = 1f, North = 1f });
            var far = _w.CreateEntity();
            _w.AddComponent(far, new LvlPos { East = 90f, North = 0f });
            var f = Level(new SpatialBoundingPredicateDto { Bounds = Circle, PositionComponentType = typeof(LvlPos), PositionXPath = "East", PositionYPath = "North" });
            Assert.True(f(_w, e));
            Assert.False(f(_w, far));
            Assert.False(f(_w, At(0, 0)));   // no LvlPos
        }

        [Fact]
        public void Lifecycle_Level_IsAliveAndIdentified_ByNetworkId_AndByHandle()
        {
            var e = At(0, 0, net: 77); var other = At(0, 0, net: 78);
            var byNet = Level(new LifecyclePredicateDto { IdentifierType = EntityIdentifierType.NetworkId, TargetValue = "77" });
            Assert.True(byNet(_w, e)); Assert.False(byNet(_w, other));
            var byIndex = Level(new LifecyclePredicateDto { IdentifierType = EntityIdentifierType.EcsHandle, TargetValue = other.Index.ToString() });
            Assert.True(byIndex(_w, other)); Assert.False(byIndex(_w, e));
            _w.DestroyEntity(e);
            Assert.False(byNet(_w, e));                       // gone
            Assert.True(Search(new LifecyclePredicateDto { TargetValue = "77" })(_w, e));   // Search: pass-through, as before
        }

        [Fact]
        public void Structural_Level_HasTheComponent_RemovedMeansHasNot()
        {
            var with = At(0, 0); _w.AddComponent(with, new LvlTag { Value = 1 });
            var without = At(1, 1);
            var has = Level(new StructuralPredicateDto { ComponentType = typeof(LvlTag), ModificationType = StructuralModification.AnyChange });
            var gone = Level(new StructuralPredicateDto { ComponentType = typeof(LvlTag), ModificationType = StructuralModification.Removed });
            Assert.True(has(_w, with)); Assert.False(has(_w, without));
            Assert.False(gone(_w, with)); Assert.True(gone(_w, without));
            Assert.True(Search(new StructuralPredicateDto { ComponentType = typeof(LvlTag) })(_w, without));   // Search: pass-through
        }

        [Fact]
        public void Event_Level_OccurredThisFrame_AndNamingThisEntity()
        {
            var a = At(0, 0); var b = At(1, 1);
            var any = Level(new TransientEventPredicateDto { EventType = typeof(LvlHit), AnyOccurrence = true });
            var names = Level(new TransientEventPredicateDto { EventType = typeof(LvlHit), AnyOccurrence = false, PropertyPath = "Who" });
            Assert.False(any(_w, a));                           // nothing published yet
            _w.Bus.Publish(new LvlHit { Who = a, Damage = 3 });
            _w.Bus.SwapBuffers();
            Assert.True(any(_w, a)); Assert.True(any(_w, b));
            Assert.True(names(_w, a)); Assert.False(names(_w, b));
            _w.Bus.SwapBuffers();                               // the next frame: gone
            Assert.False(any(_w, a)); Assert.False(names(_w, a));
            Assert.True(Search(new TransientEventPredicateDto { EventType = typeof(LvlHit) })(_w, a));   // Search: pass-through
        }

        /// <summary>OnEntity evaluates its predicate on the entity the link names, not on the one asked about. ✅ Red-proof: evaluating on the asked
        /// entity instead ⇒ red.</summary>
        [Fact]
        public void OnEntity_EvaluatesAnotherEntitysComponent()
        {
            var watched = At(0, 0, net: 500);
            _w.AddComponent(watched, new ObjectiveStatus { State = ObjectiveState.Achieved });
            var asker = At(5, 5, net: 501);                      // has no ObjectiveStatus
            Func<ObjectiveState, SearchPredicateDto> On = st => new OnEntityPredicateDto
            {
                Ref = new EntityLink(new EntityRef(500)),
                Predicate = new PropertyMatchDto
                {
                    ComponentType = typeof(ObjectiveStatus), PropertyPath = "State", Operator = SearchOperator.Equals,
                    Predicate = new NumericPredicateDto { MinValue = (double)st, MaxValue = (double)st },
                },
            };
            Assert.True(Level(On(ObjectiveState.Achieved))(_w, asker));
            Assert.False(Level(On(ObjectiveState.Failed))(_w, asker));
            Assert.True(Search(On(ObjectiveState.Achieved))(_w, asker));          // a leaf in both modes
            _w.DestroyEntity(watched);
            Assert.False(Level(On(ObjectiveState.Achieved))(_w, asker));          // dead ⇒ false
            Assert.False(Level(new OnEntityPredicateDto { Ref = default, Predicate = On(ObjectiveState.Achieved) })(_w, asker));   // unresolved
        }

        [Fact]
        public void ScenarioTime_IsAHalfOpenWindowOnTheSimClock()
        {
            var e = At(0, 0);
            var f = Level(new ScenarioTimePredicateDto { FromSeconds = 5, ToSeconds = 10 });
            void At_(double t) => _w.SetSingleton(new GlobalTime { TotalTime = t, TimeScale = 1f });
            At_(4.9); Assert.False(f(_w, e));
            At_(5.0); Assert.True(f(_w, e));
            At_(9.9); Assert.True(f(_w, e));
            At_(10.0); Assert.False(f(_w, e));
            At_(12.0); Assert.True(Search(new ScenarioTimePredicateDto { FromSeconds = 5 })(_w, e));   // a leaf in both modes
        }

        /// <summary>2 of 3 inside an area ⇒ Min 2 true, Min 3 false; Max caps it; a component filter narrows the set.</summary>
        [Fact]
        public void CountMatching_CountsTheEntitiesInsideTheArea()
        {
            At(1, 1); At(3, 3); At(80, 80);
            Func<int, int, SearchPredicateDto> Count = (min, max) => new CountMatchingPredicateDto { Predicate = Spatial(), Min = min, Max = max };
            var asker = At(500, 500);
            Assert.True(Level(Count(2, int.MaxValue))(_w, asker));
            Assert.False(Level(Count(3, int.MaxValue))(_w, asker));
            Assert.False(Level(Count(0, 1))(_w, asker));                      // 2 > Max 1
            Assert.True(Level(Count(0, 2))(_w, asker));

            var tagged = At(0, 0); _w.AddComponent(tagged, new LvlTag());
            var withTag = Level(new CountMatchingPredicateDto { Predicate = Spatial(), Min = 1, Max = 1, WithComponent = typeof(LvlTag) });
            Assert.True(withTag(_w, asker));                                  // only the tagged one counts
        }

        [Fact]
        public void TheNewLeaves_ContributeNothingToTheMandatorySet_AndDoNotCrashIt()
        {
            var tree = new CompoundPredicateDto
            {
                Conditions = new List<SearchPredicateDto>
                {
                    new OnEntityPredicateDto { Ref = default, Predicate = new PropertyMatchDto { ComponentType = typeof(LvlTag), PropertyPath = "Value", Predicate = new NumericPredicateDto() } },
                    new ScenarioTimePredicateDto(),
                    new CountMatchingPredicateDto { Predicate = Spatial() },
                    new PropertyMatchDto { ComponentType = typeof(LvlPos2), PropertyPath = "A", Predicate = new NumericPredicateDto() },
                },
            };
            Assert.Equal(new[] { typeof(LvlPos2) }, _c.ExtractMandatoryComponents(tree));
        }

        /// <summary>A nested Compound(OnEntity(PropertyMatch ObjectiveStatus.State), ScenarioTime) survives JSON and still evaluates.</summary>
        [Fact]
        public void ANestedLevelPredicate_RoundTripsThroughJson()
        {
            var tree = new CompoundPredicateDto
            {
                Conditions = new List<SearchPredicateDto>
                {
                    new OnEntityPredicateDto
                    {
                        Ref = new EntityLink(new EntityRef(500)),
                        Predicate = new PropertyMatchDto { ComponentType = typeof(ObjectiveStatus), PropertyPath = "State", Operator = SearchOperator.Equals,
                                                           Predicate = new NumericPredicateDto { MinValue = 2, MaxValue = 2 } },
                    },
                    new ScenarioTimePredicateDto { FromSeconds = 1, ToSeconds = 3 },
                    new CountMatchingPredicateDto { Predicate = Spatial(), Min = 2, WithComponent = typeof(LvlTag) },
                },
            };
            var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            string json = JsonSerializer.Serialize<SearchPredicateDto>(tree, opts);
            Assert.Contains("\"$type\":\"OnEntity\"", json); Assert.Contains("\"$type\":\"ScenarioTime\"", json); Assert.Contains("\"$type\":\"CountMatching\"", json);
            var back = (CompoundPredicateDto)JsonSerializer.Deserialize<SearchPredicateDto>(json, opts)!;
            Assert.Equal(3, back.Conditions.Count);
            var on = Assert.IsType<OnEntityPredicateDto>(back.Conditions[0]);
            Assert.Equal(500, on.Ref.Ref.NetworkId);
            Assert.Equal(typeof(ObjectiveStatus), Assert.IsType<PropertyMatchDto>(on.Predicate).ComponentType);
            Assert.Equal((1d, 3d), (((ScenarioTimePredicateDto)back.Conditions[1]).FromSeconds, ((ScenarioTimePredicateDto)back.Conditions[1]).ToSeconds));
            Assert.Equal(typeof(LvlTag), ((CountMatchingPredicateDto)back.Conditions[2]).WithComponent);

            var watched = At(0, 0, net: 500);
            _w.AddComponent(watched, new ObjectiveStatus { State = ObjectiveState.Achieved });
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0, TimeScale = 1f });
            At(1, 1); var t = At(2, 2); _w.AddComponent(t, new LvlTag());
            var f = Level(back);
            Assert.False(f(_w, watched));                      // only ONE tagged entity inside: Min 2 not reached
            var t2 = At(3, 3); _w.AddComponent(t2, new LvlTag());
            Assert.True(f(_w, watched));
            _w.SetSingleton(new GlobalTime { TotalTime = 4.0, TimeScale = 1f });
            Assert.False(f(_w, watched));                      // outside the time window
        }

        /// <summary>⭐ P5-style: a compiled Level Compound(PropertyMatch + ScenarioTime) allocates nothing per evaluation.</summary>
        [Fact]
        public void ACompiledLevelCompound_PropertyMatchAndScenarioTime_AllocatesNothingPerEvaluation()
        {
            var e = At(0, 0); _w.AddComponent(e, new LvlTag { Value = 9 });
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0, TimeScale = 1f });
            var f = Level(new CompoundPredicateDto
            {
                Conditions = new List<SearchPredicateDto>
                {
                    new PropertyMatchDto { ComponentType = typeof(LvlTag), PropertyPath = "Value", Operator = SearchOperator.GreaterThan, Predicate = new NumericPredicateDto { MinValue = 5 } },
                    new ScenarioTimePredicateDto { FromSeconds = 1, ToSeconds = 3 },
                },
            });
            Assert.True(f(_w, e));
            for (int i = 0; i < 100; i++) f(_w, e);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) f(_w, e);
            long perEval = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
            Assert.True(perEval <= 0, $"a compiled level evaluation allocated {perEval} B per call");
        }
    }

    [ComponentId(482)] internal struct LvlPos { public float East, North; }
    [ComponentId(483)] internal struct LvlPos2 { public int A; }
}
