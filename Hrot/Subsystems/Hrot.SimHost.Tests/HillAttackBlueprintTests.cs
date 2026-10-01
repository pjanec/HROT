using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-464</c> — the hill-attack commander rebuilt as ONE blueprint behaviour (<c>PlatoonHillAttackBp.bp.json</c>),
    /// driven through the REAL pipeline (behaviour ingress, brain tick, EQS solver) and held against the C# reference
    /// (<c>PlatoonHillAttack</c>) on the same scenario. 📄 <c>docs/blueprints/DESIGN_Hill_Attack_Blueprint_Behaviour.md</c>.
    /// <para>
    /// ⚠ The tick below deliberately has NO tactical-intent resolution: the orders the commander sends stay on the bus
    /// for the test to read, and no subordinate behaviour takes over the tanks — the commander is the subject.
    /// Both commanders run with a geographic transform (production always has one; the blueprint has no
    /// Cartesian fallback — design §4).
    /// </para>
    /// </summary>
    public sealed class HillAttackBlueprintTests
    {
        private const string Bp = "PlatoonHillAttackBp";
        private static readonly int RunHash = BehaviorHash.FromName(BehaviorNames.HullDownAttackRun);

        /// <summary>One self-contained world: repo + grid + transform + the pipeline systems.</summary>
        private sealed class World : IDisposable
        {
            public readonly EntityRepository Repo = new();
            public readonly WGS84Transform Geo = new();
            public readonly List<AssignTacticalIntentEvent> Orders = new();
            public readonly List<BehaviorFinishedEvent> Finished = new();
            private SpatialHashGrid _grid;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            // ⭐ EQS 1.3 (DESIGN_Hill_Attack_Eqs_Migration.md): the area query is the EntitiesOfForceInArea sensor,
            //    solved and applied in this one world (Path B) — no AreaQuerySolverSystem.
            private readonly EqsSolverSystem _eqs = new();
            private readonly EqsResultUpdateSystem _eqsUpdate = new();

            public World()
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                Repo.RegisterComponent<NetworkIdentity>();
                BlueprintTierTable.RegisterAll(Repo);
                _grid = SpatialHashGrid.Create(100, 100, 5f, 1000, Allocator.Persistent);
                _grid.Clear();
                Repo.SetSingleton(new SpatialGridData { Grid = _grid });
                Geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(Geo);
                EqsTemplateRegistry.InstallDefault(Repo);
                var registry = new BehaviorRegistry();
                CgfBehaviorSetup.LoadFromAiAssembly(registry);
                _ingress = new BehaviorIngressSystem(registry);
                _brain = new BrainTickSystem(registry);
            }

            public (double Lat, double Lon) LatLon(float x, float y)
            {
                var (lat, lon, _) = Geo.ToGeodetic(new Vector3(x, y, 0f));
                return (lat, lon);
            }

            public Entity Area(params Vector2[] polygon)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                var ecb = (EntityCommandBuffer)((ISimulationView)Repo).GetCommandBuffer();
                ecb.AddManagedComponent(e, new EditablePolyline { Points = polygon.ToList(), Version = 1 });
                ecb.Playback(Repo);
                return e;
            }

            public Entity Hostile(float x, float y, long netId)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
                Repo.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
                Repo.AddComponent(e, new NetworkIdentity { Value = netId });
                _grid.Add(e, new Vector2(x, y));
                Repo.SetSingleton(new SpatialGridData { Grid = _grid });
                Hostiles.Add(e);
                return e;
            }

            public readonly List<Entity> Hostiles = new();

            /// <summary>The hostiles are destroyed: Health 0, bodies stay in the world (CE-272). ⭐ EQS's area template
            /// rejects them by health (AliveFilterTest) — it walks entities, so emptying a grid no longer hides anyone.</summary>
            public void KillHostiles()
            {
                foreach (var h in Hostiles)
                    if (Repo.HasComponent<Health>(h)) Repo.GetComponentRW<Health>(h).Current = 0f;
                    else Repo.AddComponent(h, new Health { Current = 0f, Max = 100f });
            }

            public unsafe Entity[] Platoon(int n)
            {
                var subs = new Entity[n];
                for (int i = 0; i < n; i++)
                {
                    subs[i] = Repo.CreateEntity();
                    Repo.AddComponent<BehaviorState>(subs[i], default);
                    Repo.AddComponent(subs[i], new NavigationStatus { Result = NavigationResult.Arrived });
                }
                return subs;
            }

            public unsafe Entity Commander(Entity[] subs)
            {
                var c = Repo.CreateEntity();
                Repo.AddComponent<BehaviorState>(c, default);
                Repo.AddComponent(c, new NetworkIdentity { Value = 9000 + c.Index });   // a child sensor's parent needs one (EQS §16.2 H5)
                RootStateAccess.EnsureRootState(Repo, c);
                var roster = new UnitRoster { Count = subs.Length };
                for (int i = 0; i < subs.Length; i++) roster.SubordinateEntities[i] = subs[i];
                Repo.AddComponent(c, roster);
                return c;
            }

            public void Assign(Entity commander, string behaviour, string json)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = commander, BehaviorName = behaviour, JsonParams = json });

            /// <summary>ingress → brain → (read the orders) → EQS solver → apply the answer, like HillAttackIntegrationTests.TickOnce.</summary>
            public void Tick(float dt = 0.1f)
            {
                Repo.Bus.SwapBuffers();
                _ingress.Execute(Repo, dt);
                _brain.Execute(Repo, dt);
                Repo.Bus.SwapBuffers();
                Orders.AddRange(Repo.Bus.ReadManaged<AssignTacticalIntentEvent>().Where(o => o != null));
                foreach (var f in Repo.Bus.Read<BehaviorFinishedEvent>()) Finished.Add(f);
                var ecb = (EntityCommandBuffer)((ISimulationView)Repo).GetCommandBuffer();
                ecb.Playback(Repo);       // the brain's structural changes (a new sensor) land before the solver runs — as in production
                _eqs.Execute(Repo, dt);
                ecb.Playback(Repo);
                Repo.Bus.SwapBuffers();
                _eqsUpdate.Execute(Repo, dt);
            }

            public void Dispose()
            {
                if (Repo.HasSingleton<AreaQueryBatchData>())
                {
                    ref var b = ref Repo.GetSingleton<AreaQueryBatchData>();
                    if (b.Results.IsCreated) b.Results.Dispose();
                }
                if (Repo.HasSingleton<EqsTargetPool>())
                {
                    var p = Repo.GetSingleton<EqsTargetPool>();
                    if (p.Targets.IsCreated) p.Targets.Dispose();
                }
                if (Repo.HasSingleton<EqsResultPool>())
                {
                    var r = Repo.GetSingleton<EqsResultPool>();
                    if (r.Results.IsCreated) r.Results.Dispose();
                }
                _grid.Dispose();
            }
        }

        /// <summary>The doctrine's JSON: firing line x = 0, y 0..90; baseline x = 50, y 0..90; area network id.</summary>
        private static string Json(World w, long areaNetId, float spacing = 30f)
        {
            string P(float x, float y)
            {
                var (lat, lon) = w.LatLon(x, y);
                return $"[{lat.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}]";
            }
            // camelCase keys, as a scenario writes them
            return "{\"firingLineStart\":" + P(0, 0) + ",\"firingLineEnd\":" + P(0, 90)
                 + ",\"baselineStart\":" + P(50, 0) + ",\"baselineEnd\":" + P(50, 90)
                 + ",\"tankSpacing\":" + spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 + ",\"targetAreaNetworkId\":" + areaNetId + "}";
        }

        private static (World W, Entity Commander, Entity[] Subs) Scenario(string behaviour, int tanks, bool withHostiles)
        {
            var w = new World();
            var area = w.Area(new(10f, 10f), new(80f, 10f), new(80f, 80f), new(10f, 80f));
            var map = new NetworkEntityMap();
            map.Register(9100L, area);
            w.Repo.SetSingletonManaged<NetworkEntityMap>(map);
            if (withHostiles)
            {
                w.Hostile(40f, 40f, 501L);
                w.Hostile(60f, 60f, 502L);
            }
            var subs = w.Platoon(tanks);
            var c = w.Commander(subs);
            w.Assign(c, behaviour, Json(w, 9100L));
            return (w, c, subs);
        }

        private static IEnumerable<AssignTacticalIntentEvent> Waves(World w)
            => w.Orders.Where(o => o.IntentId == "HullDownAttack");

        private static string Key(AssignTacticalIntentEvent o)
        {
            using var d = JsonDocument.Parse(o.JsonParams);
            float F(string n) => MathF.Round(d.RootElement.GetProperty(n).GetSingle(), 2);
            return $"{o.Entity.Index}: slot=({F("SlotX")},{F("SlotY")}) base=({F("BaselineX")},{F("BaselineY")}) "
                 + $"dir=({F("AttackDirX")},{F("AttackDirY")}) target={d.RootElement.GetProperty("TargetNetworkId").GetInt64()}";
        }

        // ── A ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Empty area ⇒ the blueprint commander stages the platoon, queries, finds nothing, sends every tank back to its
        /// baseline slot and FINISHES — the SC-HA015-1 outcome (incl. CE-459's return) through the blueprint.
        /// </summary>
        [Fact]
        public void CE464_Blueprint_EmptyArea_ReturnsThePlatoonAndFinishes()
        {
            var (w, c, subs) = Scenario(Bp, tanks: 2, withHostiles: false);
            using var _ = w;
            for (int t = 0; t < 20 && !w.Finished.Any(f => f.Entity == c); t++) w.Tick();

            Assert.Contains(w.Finished, f => f.Entity == c);
            Assert.Empty(Waves(w));
            foreach (var s in subs)   // staged once, returned once
                Assert.Equal(2, w.Orders.Count(o => o.Entity == s && o.IntentId == "MoveToLocation"));
        }

        // ── B ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴 Parity: the same scenario (4 tanks, 2 hostiles) under the C# commander and the blueprint commander — the first
        /// wave's orders are the same set: which tanks go, their firing and baseline slots, the attack direction and the
        /// round-robin targets.
        /// </summary>
        [Fact]
        public void CE464_Blueprint_FirstWave_MatchesTheCSharpDoctrine()
        {
            var (cs, _, _) = Scenario(BehaviorNames.PlatoonHillAttack, tanks: 4, withHostiles: true);
            var (bp, _, _) = Scenario(Bp, tanks: 4, withHostiles: true);
            using var d1 = cs; using var d2 = bp;
            for (int t = 0; t < 20 && !Waves(cs).Any(); t++) cs.Tick();
            for (int t = 0; t < 20 && !Waves(bp).Any(); t++) bp.Tick();

            var expected = Waves(cs).Select(Key).OrderBy(k => k).ToList();
            var actual   = Waves(bp).Select(Key).OrderBy(k => k).ToList();
            Console.WriteLine("C#:\n  " + string.Join("\n  ", expected) + "\nBP:\n  " + string.Join("\n  ", actual));
            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);
        }

        // ── C ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The whole cycle: the wave goes out, the runs start (a subordinate shows <c>HullDownAttackRun</c>), the hostiles
        /// are gone and the runs end ⇒ the commander re-queries, finds the area clear, returns the platoon and finishes.
        /// </summary>
        [Fact]
        public void CE464_Blueprint_FullCycle_WaveRunsEnd_AreaClears_PlatoonReturns_Finishes()
        {
            var (w, c, subs) = Scenario(Bp, tanks: 4, withHostiles: true);
            using var _ = w;
            for (int t = 0; t < 20 && !Waves(w).Any(); t++) w.Tick();
            var runners = Waves(w).Select(o => o.Entity).ToList();
            Assert.NotEmpty(runners);

            foreach (var r in runners) w.Repo.GetComponentRW<BehaviorState>(r).ActiveBehaviorHash = RunHash;
            w.Tick();                                    // the commander sees the runs start
            w.KillHostiles();                            // the hostiles are destroyed
            foreach (var r in runners) w.Repo.GetComponentRW<BehaviorState>(r).ActiveBehaviorHash = 0;

            for (int t = 0; t < 30 && !w.Finished.Any(f => f.Entity == c); t++) w.Tick();
            Assert.Contains(w.Finished, f => f.Entity == c);
            foreach (var s in subs)
                Assert.True(w.Orders.Count(o => o.Entity == s && o.IntentId == "MoveToLocation") >= 2,
                    $"tank {s.Index} was not returned to the baseline");
        }
    }
}
