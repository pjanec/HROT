using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.Systems;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Tests.Squad
{
    /// <summary>
    /// ⭐ <c>CE-3072</c> B3 + B4 — the danger-area sensor end to end in one world (the editor's fused shape): the solve
    /// (<see cref="DangerAlongRouteSolve"/>), the answer applied (<see cref="DangerAreaSensorSystem"/>), the rating from the
    /// unit's memory, and the edges. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// </summary>
    public sealed class DangerAreaSensorSystemTests : IDisposable
    {
        private readonly EntityRepository _repo = new();

        public DangerAreaSensorSystemTests()
        {
            _repo.RegisterComponent<PartMetadata>();
            _repo.RegisterComponent<EqsSensor>();
            _repo.RegisterComponent<EqsCognitiveBuffer>();
            _repo.RegisterComponent<SensorTag>();
            _repo.RegisterComponent<ScenarioIgnoreTag>();
            _repo.RegisterComponent<BehaviorOwnedPart>();
            _repo.RegisterComponent<DangerAreaSensor>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<NavigationIntent>();
            _repo.RegisterComponent<EqsContextPoints>();   // ⭐ CE-3272 — the contacts the solver judges
            _repo.RegisterManagedEvent<DangerAreaResultEvent>();
            _repo.RegisterEvent<SensorChangedEvent>();
            _repo.SetSingletonManaged(TestTown());
            // ⭐ CE-3128 — the nav node's navmesh (open ground here): the planner compares walking direct with the roads.
            _repo.SetSingletonManaged<INavmeshProvider>(new StraightNavmesh());
        }

        // ⭐ CE-3128 — the roads are the road GRAPH (test-town's, as roads.json declares it), read through a holder like production.
        private readonly global::CarKinem.Road.RoadNetworkHolder _roads = new(TestTownRoads.Build(), takeOwnership: true);
        private readonly RoutePlanner _planner = new();

        public void Dispose() { _repo.Dispose(); _roads.Dispose(); }

        /// <summary>test-town's ground with one building north-east of the Cross Street crossing (the roads are <see cref="_roads"/>).</summary>
        private static TerrainWorld TestTown() => new()
        {
            Name = "test-town", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(400, 400), GroundZ = 0f,
            Prisms = new[] { Block(new Vector2(240, 320), new Vector2(280, 360), 12f) },
        };

        private static TerrainPrism Block(Vector2 min, Vector2 max, float height) => new()
        {
            Kind = TerrainPrismKind.Building,
            Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
            Triangles = new[] { 0, 1, 2, 0, 2, 3 },
            BaseZ = 0f, TopZ = height, Min = min, Max = max,
        };

        private Entity Unit(Vector3 at)
        {
            var u = _repo.CreateEntity();
            _repo.AddComponent(u, new SimTransform { Position = at });
            _repo.AddComponent(u, default(TargetMemory));
            return u;
        }

        private Entity Hostile(Vector3 at)
        {
            var h = _repo.CreateEntity();
            _repo.AddComponent(h, new SimTransform { Position = at });
            _repo.AddComponent(h, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            _repo.AddComponent(h, new Health { Current = 100f, Max = 100f });
            return h;
        }

        private void Remember(Entity unit, Entity contact, Vector3 at)
        {
            ref var mem = ref _repo.GetComponentRW<TargetMemory>(unit);
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: (long)contact.PackedValue, posX: at.X, posY: at.Y,
                scoreBoost: PerceptionConstants.FreshnessSaturation, tick: 1, modality: SensorModality.Visual);
        }

        // One solve, published locally (the fused world), then one Brain tick. ⭐ CE-3272 — the brain first hands the solver its contacts
        //   (as its tick does on every frame before), so the solve judges where they were last seen.
        private DangerAreaCognitiveBuffer SolveAndTick(Entity child, DangerAreaSensorSystem system)
        {
            using (var contacts = new EntityCommandBuffer())
            {
                DangerAreaSensorSystem.WriteContacts(_repo, contacts);
                contacts.Playback(_repo);
            }
            var sensor = _repo.GetComponentRO<EqsSensor>(child);
            var areas = new DangerAreaDescriptor[8];
            int n = DangerAlongRouteSolve.Solve(_repo, child, in sensor, areas, _planner, _roads);
            _repo.Bus.PublishManaged(new DangerAreaResultEvent
            {
                ParentNetworkId = 0, LocalChildIndex = child.Index, Epoch = sensor.Epoch, RefreshTick = 7, Areas = areas, Count = n,
            });
            _repo.Bus.SwapBuffers();
            system.Execute(_repo, 0.1f);
            return _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
        }

        private List<SensorChange> Edges()
        {
            ((EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer()).Playback(_repo);
            _repo.Bus.SwapBuffers();
            return ((ISimulationView)_repo).ReadEvents<SensorChangedEvent>().ToArray().Select(e => e.What).ToList();
        }

        [Fact]
        public void CE3072_B3_ToPoint_ListsTheCrossing_WithNoThreatWhenNothingIsKnown()
        {
            var unit = Unit(new Vector3(100, 100, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(100, 300, 0)));
            _repo.SetSimulationTime(12.5f);

            var b = SolveAndTick(child, new DangerAreaSensorSystem());

            Assert.True(b.IsReady);
            Assert.Equal(12.5f, b.LastUpdateTimeSeconds);               // Q4 — the stamp BecomesStale reads
            Assert.Equal(1, b.Count);                                   // (100,100) → (100,300) crosses Main Street once
            Assert.Equal(DangerAreaKind.StreetCrossing, b.GetSpanRO()[0].Kind);
            Assert.Equal(0f, b.GetSpanRO()[0].ThreatRating);           // nobody known ⇒ no threat
        }

        [Fact]
        public void CE3072_B4_Threat_IsTheKnownArmedContact_WithSightOfTheArea_AndNotABlockedOne()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));           // on the line, sees the Cross Street crossing
            Remember(unit, watcher, new Vector3(260, 300, 0));
            var system = new DangerAreaSensorSystem();

            var b = SolveAndTick(child, system);
            Assert.Equal(1, b.Count);
            Assert.Equal(1f, b.GetSpanRO()[0].ThreatRating);

            // the rifleman watched it walk behind the building: its last-known position no longer sees the crossing — ⭐ CE-3272: the
            //   SOLVER judges that on its next run (the brain reads no terrain), so the release waits for the next answer
            Remember(unit, watcher, new Vector3(290, 340, 0));
            Assert.Equal(0f, SolveAndTick(child, system).GetSpanRO()[0].ThreatRating);
        }

        /// <summary>
        /// ⭐ <c>CE-3272</c> (Q94 B, R-3018) — the brain rates ONLY from the solver's <see cref="DangerAreaDescriptor.SeenFrom"/>: a
        /// contact in plain sight of the crossing is no threat while the answer says no point sees it, and a threat once it says one
        /// does. ✅ Red-proof: a brain that still traced sight itself rates the first 1.
        /// </summary>
        [Fact]
        public void CE3272_TheBrainRatesFromTheSolversSight_NotFromItsOwnTerrain()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));           // in plain sight of the crossing
            Remember(unit, watcher, new Vector3(260, 300, 0));
            var system = new DangerAreaSensorSystem();
            SolveAndTick(child, system);
            _repo.Bus.SwapBuffers();                                    // the answer is applied; it must not be re-read below

            _repo.GetComponentRW<DangerAreaCognitiveBuffer>(child).GetSpanRW()[0].SeenFrom = 0;   // the solver: no remembered point sees it
            system.Execute(_repo, 0.1f);
            Assert.Equal(0f, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).GetSpanRO()[0].ThreatRating);

            _repo.GetComponentRW<DangerAreaCognitiveBuffer>(child).GetSpanRW()[0].SeenFrom = 1;   // slot 0 sees it
            system.Execute(_repo, 0.1f);
            Assert.Equal(1f, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).GetSpanRO()[0].ThreatRating);
        }

        /// <summary>
        /// ⭐ <c>CE-3272</c> — the solver's mask: bit i when contact point i sees the area on the solver's terrain; every bit with none.
        /// ✅ Red-proof: drop the terrain test in <c>MarkSeenFrom</c> ⇒ the blocked point's bit is set.
        /// </summary>
        [Fact]
        public void CE3272_TheSolverMarksWhichContactsSeeTheArea()
        {
            var carrier = _repo.CreateEntity();
            var points = new EqsContextPoints { Count = 2 };
            points.Set(0, new Vector3(260, 300, 0));                   // on the line: sees the crossing at x 200
            points.Set(1, new Vector3(290, 340, 0));                   // behind the building
            _repo.AddComponent(carrier, points);
            Span<DangerAreaDescriptor> areas = stackalloc DangerAreaDescriptor[1];
            areas[0].Center = new Vector3(200, 300, 0);

            DangerAlongRouteSolve.MarkSeenFrom(_repo, carrier, Fdp.Toolkit.World.WorldQuery.Of(_repo), areas);
            Assert.Equal((ushort)0b01, areas[0].SeenFrom);
            DangerAlongRouteSolve.MarkSeenFrom(_repo, carrier, null, areas);
            Assert.Equal((ushort)0b11, areas[0].SeenFrom);             // no terrain: every point sees (the old "sight 1")
        }

        /// <summary>⭐ <c>CE-2159</c> X-a (Squad Tactics §7.7.6) — a squad COMMANDER has no memory of its own: its sensor is rated from the
        /// squad's merged contact pool — a pooled contact with sight of the crossing threatens it, a dead one does not.
        /// 🔴 Red before: a unit without <c>TargetMemory</c> rated every area 0.</summary>
        [Fact]
        public unsafe void CE2159_ACommandersAreas_AreRatedFromTheSquadPool()
        {
            _repo.RegisterComponent<SquadCognitiveState>();
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new SimTransform { Position = new Vector3(150, 300, 0) });
            var child = DangerAreaChildSensor.Ensure(_repo, commander, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            var squad = new SquadCognitiveState();
            squad.Contacts.Count = 1;
            var pool = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
            pool[0] = new SquadContact { EntityId = (long)watcher.PackedValue, PositionX = 260, PositionY = 300, ThreatScore = 1f };
            _repo.AddComponent(commander, squad);
            var system = new DangerAreaSensorSystem();

            var b = SolveAndTick(child, system);
            Assert.Equal(1, b.Count);
            Assert.Equal(1f, b.GetSpanRO()[0].ThreatRating);

            _repo.GetComponentRW<Health>(watcher).Current = 0f;                                 // killed: still pooled, no threat
            system.Execute(_repo, 0.1f);
            Assert.Equal(0f, _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).GetSpanRO()[0].ThreatRating);
        }

        [Fact]
        public void CE3072_B4_AKilledWatcher_IsNoThreat()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            Remember(unit, watcher, new Vector3(260, 300, 0));
            _repo.GetComponentRW<Health>(watcher).Current = 0f;          // CE-3080
            Assert.Equal(0f, SolveAndTick(child, new DangerAreaSensorSystem()).GetSpanRO()[0].ThreatRating);
        }

        [Fact]
        public void CE3072_B4_Edges_AreaAhead_Threatened_Cleared()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            Remember(unit, watcher, new Vector3(260, 300, 0));
            var system = new DangerAreaSensorSystem();

            SolveAndTick(child, system);
            var first = Edges();
            Assert.Contains(SensorChange.AreaAhead, first);
            Assert.Contains(SensorChange.AreaThreatened, first);

            system.Execute(_repo, 0.1f);
            Assert.Empty(Edges());                                      // no change, no edge

            Remember(unit, watcher, new Vector3(290, 340, 0));           // gone behind the building
            SolveAndTick(child, system);                                 // ⭐ CE-3272 — the solver's next answer says so
            Assert.Equal(new[] { SensorChange.AreaCleared }, Edges());
        }

        /// <summary>
        /// ⭐ <c>R-3039</c> S1 (docs/DESIGN_System_State_Inventory.md §6 #10) — the edges' memory is on the sensor's buffer (recorded), so a
        /// FRESH system — a checkpoint restored into a new process — neither re-raises the area ahead nor its threat, and still owes the
        /// clear. 🔴 Red-proof: as the system's <c>_last</c>, the fresh one raised AreaAhead + AreaThreatened again and never AreaCleared.
        /// </summary>
        [Fact]
        public void R3039_TheEdges_SurviveARestoreIntoAFreshSystem()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var watcher = Hostile(new Vector3(260, 300, 0));
            Remember(unit, watcher, new Vector3(260, 300, 0));
            SolveAndTick(child, new DangerAreaSensorSystem());
            Assert.Contains(SensorChange.AreaThreatened, Edges());
            var kept = _repo.GetComponentRO<DangerAreaCognitiveBuffer>(child);
            Assert.Equal(1, kept.EdgeRated);
            Assert.Equal(1, kept.EdgeThreatened);

            var fresh = new DangerAreaSensorSystem();
            fresh.Execute(_repo, 0.1f);
            Assert.Empty(Edges());                                       // nothing new: no second AreaAhead / AreaThreatened

            Remember(unit, watcher, new Vector3(290, 340, 0));           // gone behind the building
            SolveAndTick(child, fresh);
            Assert.Equal(new[] { SensorChange.AreaCleared }, Edges());   // the clear it owed
        }

        [Fact]
        public void CE2224_ARefreshOfTheSameArea_KeepsItsHandles_WhileTheUnitWalks()
        {
            // ⭐ the classifier samples the route from the unit's CURRENT position, so a step forward moves the raw handles; the answer
            //   must not (a consumer that re-issues its move on a changed target — DangerCrossingBp — would restart its path each refresh)
            var unit = Unit(new Vector3(100, 100, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(100, 300, 0)));
            var system = new DangerAreaSensorSystem();
            var first = SolveAndTick(child, system).GetSpanRO()[0];

            _repo.GetComponentRW<SimTransform>(unit).Position = new Vector3(100, 101.3f, 0);
            var sensor = _repo.GetComponentRO<EqsSensor>(child);
            var raw = new DangerAreaDescriptor[8];
            DangerAlongRouteSolve.Solve(_repo, child, in sensor, raw, _planner, _roads);
            Assert.Equal(first.FeatureId, raw[0].FeatureId);
            Assert.NotEqual(first.FarSideHandle, raw[0].FarSideHandle);   // the premise: the raw answer DOES move

            var second = SolveAndTick(child, system).GetSpanRO()[0];
            Assert.Equal(first.FeatureId, second.FeatureId);
            Assert.Equal(first.NearSideHandle, second.NearSideHandle);
            Assert.Equal(first.FarSideHandle, second.FarSideHandle);
            Assert.NotEqual(first.DistanceAlongRoute, second.DistanceAlongRoute);   // the rest of the answer is fresh
        }

        [Fact]
        public void CE2224_KeepHandles_KeepsOnlyAnAreaThatIsStillListed()
        {
            var previous = new[] { new DangerAreaDescriptor { FeatureId = 7, Kind = DangerAreaKind.StreetCrossing, FarSideHandle = new Vector3(1, 2, 0) } };
            var fresh = new[]
            {
                new DangerAreaDescriptor { FeatureId = 7, Kind = DangerAreaKind.StreetCrossing, FarSideHandle = new Vector3(1, 3, 0) },
                new DangerAreaDescriptor { FeatureId = 8, Kind = DangerAreaKind.StreetCrossing, FarSideHandle = new Vector3(9, 9, 0) },
            };
            DangerAreaCognitiveBuffer.KeepHandles(previous, fresh);
            Assert.Equal(new Vector3(1, 2, 0), fresh[0].FarSideHandle);
            Assert.Equal(new Vector3(9, 9, 0), fresh[1].FarSideHandle);
        }

        [Fact]
        public void CE3072_B3_AStaleAnswer_IsDropped()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var areas = new[] { new DangerAreaDescriptor { FeatureId = 9 } };
            _repo.Bus.PublishManaged(new DangerAreaResultEvent
            {
                LocalChildIndex = child.Index, Epoch = _repo.GetComponentRO<EqsSensor>(child).Epoch + 1, Areas = areas, Count = 1,
            });
            _repo.Bus.SwapBuffers();
            new DangerAreaSensorSystem().Execute(_repo, 0.1f);
            Assert.False(_repo.GetComponentRO<DangerAreaCognitiveBuffer>(child).IsReady);
        }

        [Fact]
        public void CE3072_B3_OwnMove_WatchesTheRouteToTheUnitsDestination()
        {
            var unit = Unit(new Vector3(150, 300, 0));
            _repo.AddComponent(unit, new NavigationIntent { Mode = NavigationMode.PathToPoint, FinalDestination = new Vector3(300, 300, 0) });
            var child = DangerAreaChildSensor.Ensure(_repo, unit, 1, DangerAreaSettings.Default);   // OwnMove
            Assert.Equal(1, SolveAndTick(child, new DangerAreaSensorSystem()).Count);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3081</c> (§10.7b A5) — the solve finds OpenGround on either side of Cross Street once the node has a cover map; a
        /// sensor that does not ask for it keeps only the crossing (what every existing behaviour sees), one that asks for it keeps all
        /// three in route order. 🔴 Red-proof: drop the Brain filter and the default sensor's slot 0 becomes the open run.
        /// </summary>
        [Fact]
        public void CE3081_OpenGround_ReachesOnlyASensorThatAsksForIt()
        {
            _repo.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(TestTown()));
            var plain = DangerAreaChildSensor.Ensure(_repo, Unit(new Vector3(100, 300, 0)), 1, DangerAreaSettings.ToPoint(new Vector3(300, 300, 0)));
            var buffer = SolveAndTick(plain, new DangerAreaSensorSystem());
            Assert.Equal(1, buffer.Count);
            Assert.Equal(DangerAreaKind.StreetCrossing, buffer.GetSpanRO()[0].Kind);

            var wants = DangerAreaSettings.ToPoint(new Vector3(300, 300, 0));
            wants.Kinds = DangerAreaKinds.OpenGround | DangerAreaKinds.StreetCrossing | DangerAreaKinds.Intersection;
            var open = DangerAreaChildSensor.Ensure(_repo, Unit(new Vector3(100, 300, 0)), 2, wants);
            var all = SolveAndTick(open, new DangerAreaSensorSystem());
            Assert.Equal(new[] { DangerAreaKind.OpenGround, DangerAreaKind.StreetCrossing, DangerAreaKind.OpenGround },
                all.GetSpanRO().Slice(0, all.Count).ToArray().Select(a => a.Kind));
        }
    }
}
