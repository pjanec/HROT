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

        /// <summary>⭐ CE-4159 (re-homed from the trigger PoC) — a DTO the compiler does not know, nested in a Compound, is a LOUD error in both modes — it used to compile to a
        /// constant TRUE that made the whole Compound true. ✅ Red-proof: the compiler's <c>default:</c> arm returning <c>(_, _) =&gt; true</c> again ⇒ red.</summary>
        [Fact]
        public void AnUnknownDtoNestedInACompound_IsALoudError_InBothModes()
        {
            var nested = new CompoundPredicateDto { Operator = LogicalOperator.Or, Conditions = new List<SearchPredicateDto> { new LvlUnknownDto() } };
            var ex = Assert.Throws<NotSupportedException>(() => Search(nested));
            Assert.Contains(nameof(LvlUnknownDto), ex.Message);
            Assert.Throws<NotSupportedException>(() => Level(nested));
            Assert.Throws<NotSupportedException>(() => Level(new LvlUnknownDto()));
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

        /// <summary>⭐ rules S4 E-2 (SR-20) — <c>InputAbove</c>: the registered [UtilityInput] read on the entity asked about (the same reader a consideration evaluates), above the threshold; Level and Search mode
        /// agree; the leaf's number (<c>CompileLeafMeasure</c>) is the reading; an unregistered input reads 0 (false); no name is a loud error. ✅ Red-proof: the arm comparing <c>v &lt; threshold</c> ⇒ red.</summary>
        [Fact]
        public void InputAbove_ReadsTheRegisteredUtilityInput_AboveTheThreshold()
        {
            Fdp.Toolkit.Utility.StandardInputs.RegisterAll();
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()) _w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var e = At(0, 0);
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 3f, Max = 10f });   // HealthFraction 0.3
            var bare = At(1, 1);
            InputAbovePredicateDto Leaf(string input, float t) => new() { Input = input, Threshold = t };
            foreach (var compile in new Func<SearchPredicateDto, Func<EntityRepository, Entity, bool>>[] { Level, Search })
            {
                Assert.True(compile(Leaf("HealthFraction", 0.2f))(_w, e));
                Assert.False(compile(Leaf("HealthFraction", 0.5f))(_w, e));
                Assert.False(compile(Leaf("HealthFraction", 0.2f))(_w, bare));        // no Health => the reader says 0
                Assert.False(compile(Leaf("NoSuchInputAnywhere", -1f))(_w, e));       // unregistered => nothing read
            }
            Assert.Equal(0.3d, _c.CompileLeafMeasure(Leaf("HealthFraction", 0.2f))!(_w, e), 3);
            Assert.Throws<ArgumentException>(() => Level(Leaf("", 0f)));
            Assert.Equal("Input HealthFraction", RuleLeafPlan.LabelOf(Leaf("HealthFraction", 0f)));
            var round = JsonSerializer.Deserialize<SearchPredicateDto>(JsonSerializer.Serialize<SearchPredicateDto>(Leaf("HealthFraction", 0.25f)));
            Assert.Equal((0.25f, "HealthFraction"), (((InputAbovePredicateDto)round!).Threshold, ((InputAbovePredicateDto)round).Input));
        }
        // ── ⭐ condition editor C1 (docs/UX/DESIGN_Condition_Editor.md §5, R-4142): None · InUnit · InTheFight ───────────────────────────

        private static PropertyMatchDto TagIs(int v) => new()
        {
            ComponentType = typeof(LvlTag), PropertyPath = "Value", Operator = SearchOperator.Equals,
            Predicate = new NumericPredicateDto { MinValue = v, MaxValue = v },
        };

        /// <summary>None is true when no child holds; a one-child None is NOT; in both modes. ✅ Red-proof: None compiled as Or ⇒ every assert flips.</summary>
        [Fact]
        public void C1_None_IsTrueWhenNoChildHolds_AndOfOneChildIsNot()
        {
            var e = At(0, 0); _w.AddComponent(e, new LvlTag { Value = 3 });
            var not3 = new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { TagIs(3) } };
            var not5 = new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { TagIs(5) } };
            var neither = new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { TagIs(1), TagIs(2) } };
            var one = new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { TagIs(1), TagIs(3) } };
            Assert.False(Level(not3)(_w, e));
            Assert.True(Level(not5)(_w, e));
            Assert.True(Level(neither)(_w, e));
            Assert.False(Level(one)(_w, e));
            Assert.False(Search(not3)(_w, e));
            Assert.True(Search(not5)(_w, e));
            Assert.Empty(_c.ExtractMandatoryComponents(not3));   // its children must NOT hold: it demands nothing
            var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
            var back = Assert.IsType<CompoundPredicateDto>(JsonSerializer.Deserialize<SearchPredicateDto>(JsonSerializer.Serialize<SearchPredicateDto>(not3, opts), opts));
            Assert.Equal(LogicalOperator.None, back.Operator);
        }

        private Entity Unit(int net, params Entity[] members)
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>()) _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            if (!_w.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitEntity>()) _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitEntity>();
            var u = _w.CreateEntity();
            _w.AddComponent(u, new NetworkIdentity(net));
            _w.AddComponent(u, new Fdp.Core.CommandHierarchy.UnitEntity());
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            foreach (var m in members) Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, m);
            _w.AddComponent(u, roster);
            return u;
        }

        /// <summary>⭐⭐ <c>CE-4298</c> / <c>CE-4301</c> — mission plan D6: <c>TaskFinished</c> reads a unit's replicated task outcome. Another unit's task 3
        /// (by network id) holds once it ended Done or Failed; no Unit = the asked entity, Task 0 = its CURRENT task (the plan's own <i>when done</i>);
        /// no plan / a task it lacks ⇒ false; 0 B per steady call. ✅ Red-proof: the leaf reading <c>CurrentTask</c> for every Task ⇒ the "task 3" asserts red.</summary>
        [Fact]
        public void D6_TaskFinished_AUnitWaitsOnAnothersTask3_AndTheDefaultIsThisUnitsCurrentTask()
        {
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.MissionPlanQueue>();
            var me = At(0, 0);
            var bravo = At(5, 5, net: 812);
            var q = new Fdp.Toolkit.Behavior.Components.MissionPlanQueue { TaskCount = 4, CurrentTask = 3 };
            _w.AddComponent(bravo, q);
            var task3 = new TaskFinishedPredicateDto { Unit = new EntityLink(new EntityRef(812)), Task = 3 };
            var current = new TaskFinishedPredicateDto();

            Assert.False(Level(task3)(_w, me));
            Assert.False(Level(current)(_w, me));                          // no plan
            ref var rw = ref _w.GetComponentRW<Fdp.Toolkit.Behavior.Components.MissionPlanQueue>(bravo);
            Span<Fdp.Toolkit.Behavior.Components.MissionPhaseOutcome> o = rw.Outcomes;
            o[2] = Fdp.Toolkit.Behavior.Components.MissionPhaseOutcome.Done;
            Assert.True(Level(task3)(_w, me));
            Assert.False(Level(current)(_w, bravo));                       // its CURRENT task (4) has not ended
            o[3] = Fdp.Toolkit.Behavior.Components.MissionPhaseOutcome.Failed;
            Assert.True(Level(current)(_w, bravo));
            Assert.False(Level(new TaskFinishedPredicateDto { Unit = new EntityLink(new EntityRef(812)), Task = 9 })(_w, me));   // a task it lacks

            var f = Level(task3);
            f(_w, me);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) f(_w, me);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>InUnit: the unit itself (unless IncludeSelf is off), anyone under it at any depth, nobody else; unresolved ⇒ false.
        /// ✅ Red-proof: the walk not recursing ⇒ the platoon's man red.</summary>
        [Fact]
        public void C1_InUnit_IsTheUnitOrAnyoneUnderIt_DownTheRoster()
        {
            var a = At(0, 0); var b = At(1, 1); var stranger = At(2, 2);
            var squad = Unit(700, a, b);
            var platoon = Unit(701, squad);
            var inPlatoon = new InUnitPredicateDto { Unit = new EntityLink(new EntityRef(701)) };
            Assert.True(Level(inPlatoon)(_w, a));            // two levels down
            Assert.True(Level(inPlatoon)(_w, squad));
            Assert.True(Level(inPlatoon)(_w, platoon));      // IncludeSelf defaults on
            Assert.False(Level(inPlatoon)(_w, stranger));
            Assert.True(Search(inPlatoon)(_w, b));           // both modes
            Assert.False(Level(new InUnitPredicateDto { Unit = new EntityLink(new EntityRef(701)), IncludeSelf = false })(_w, platoon));
            Assert.False(Level(new InUnitPredicateDto { Unit = default })(_w, a));   // unresolved
            Assert.Empty(_c.ExtractMandatoryComponents(inPlatoon));
            var census = new List<Entity>();                 // the census reads the SAME walk (lifted, never a second one)
            Fdp.Toolkit.Squad.UnitTree.Collect(_w, platoon, census);
            Assert.Equal(new[] { platoon, squad, a, b }, census);
        }

        /// <summary>InTheFight: a man by his injury state (a dead man still EXISTS — Lifecycle holds, InTheFight does not), a unit by its roster.
        /// ✅ Red-proof: the arm compiled as IsAlive ⇒ the dead man red.</summary>
        [Fact]
        public void C1_InTheFight_ADeadManStillExistsButIsNotInTheFight_AUnitByItsRoster()
        {
            _w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var alive = At(0, 0, net: 710); _w.AddComponent(alive, new Fdp.Toolkit.Combat.Components.Health { Current = 80, Max = 100 });
            var dead = At(1, 0, net: 711); _w.AddComponent(dead, new Fdp.Toolkit.Combat.Components.Health { Current = 0, Max = 100 });
            var fight = new InTheFightPredicateDto();
            Assert.True(Level(fight)(_w, alive));
            Assert.False(Level(fight)(_w, dead));
            Assert.True(Level(new LifecyclePredicateDto { IdentifierType = EntityIdentifierType.NetworkId, TargetValue = "711" })(_w, dead));   // ⛔ "exists" ≠ "in the fight"
            Assert.False(Search(fight)(_w, dead));
            var wiped = Unit(712, dead);
            Assert.False(Level(fight)(_w, wiped));
            Assert.True(Level(fight)(_w, Unit(713, alive)));
            Assert.True(Level(new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { fight } })(_w, wiped));   // "destroyed" as the editor writes it
        }
    }

    /// <summary>A predicate DTO the compiler has no arm for.</summary>
    internal sealed class LvlUnknownDto : SearchPredicateDto { }

    [ComponentId(482)] internal struct LvlPos { public float East, North; }
    [ComponentId(483)] internal struct LvlPos2 { public int A; }
}
