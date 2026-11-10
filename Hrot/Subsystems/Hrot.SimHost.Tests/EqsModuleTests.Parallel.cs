using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ CE-3427 P-1 — parallel solving (docs/DESIGN_Sensors_And_Doctrine.md §5.8).</summary>
    public partial class EqsModuleTests
    {
        private static readonly Guid P1Light = new("5e2c0d19-0000-4000-8000-0000003427a1");
        private static readonly Guid P1Heavy = new("5e2c0d19-0000-4000-8000-0000003427a2");

        /// <summary>
        /// ⭐⭐ CE-3427 P-1 — the parallel solver IS the serial solver: one world run 20 ticks with <c>Workers = 1</c> and with
        /// <c>Workers = 4</c> gives the same schedule, units, twin copies, every <c>EqsResultEvent</c> and its pool entries, every perception
        /// event (sightings, sounds), every sensor's eval state and hearing cursor. The world is over budget in all three bands, carries
        /// twins, visual AND hearing sensors with shots in the ring, an epoch bump and a never-solved cohort arriving mid-run — and the
        /// parallel run must really have solved ahead and DISCARDED solves (a discarded hearing solve must not move its cursor).
        /// 🔴 Red-proofs (§5.8): start every predicted place without the actual-cost check ⇒ the schedule differs; write the hearing cursor
        /// during the solve (no deferral) ⇒ the cursors and sounds differ.
        /// </summary>
        [Fact]
        public void P1_ParallelSolving_IsTheSerialSolver_AndADiscardedSolveLeavesNoTrace()
        {
            var serial   = RunP1World(workers: 1, out _, out _);
            var parallel = RunP1World(workers: 4, out int parallelSolves, out int discarded);

            Assert.True(parallelSolves > 100, $"the parallel run must solve on the window ({parallelSolves} solved ahead)");
            Assert.True(discarded > 0, "the world must make the window mispredict, so a discarded solve is proven to leave no trace");
            Assert.Contains(serial, t => t.Contains(" | S "));   // hearing really heard the ring's shots
            Assert.Contains(serial, t => t.Contains(" | T "));   // sight really acquired someone (the memory stage)
            Assert.Contains(serial, t => t.Contains(" c"));      // hearing cursors moved
            Assert.Equal(serial.Count, parallel.Count);
            for (int t = 0; t < serial.Count; t++)
                Assert.True(serial[t] == parallel[t], $"tick {t} differs:\nserial:   {serial[t]}\nparallel: {parallel[t]}");
        }

        // One deterministic world, run 20 ticks; each tick's observable output as one string.
        private static List<string> RunP1World(int workers, out int parallelSolves, out int discarded)
        {
            parallelSolves = 0; discarded = 0;
            var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            var grid = SpatialHashGrid.Create(100, 100, 5f, 4000, Allocator.Persistent);
            var perception = new Fdp.Toolkit.Perception.Modules.PerceptionGridProvider();
            try
            {
                grid.Clear();
                var view = (ISimulationView)w;
                var rnd = new Random(3427);
                var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(w);
                var sight = new Fdp.Toolkit.Perception.LineOfSight.PlanarCircleLosStrategy(ColliderRadius);
                VisualPerception.Register(registry, perception.Grid, sight);
                AcousticPerception.Register(registry, perception.Grid);
                registry.Register(P1Light, new EqsQueryTemplate { BlueprintId = EqsTemplateRegistry.BlueprintIdOf(P1Light), Generator = new FixedCostGenerator(20), MaxCandidates = 32 }, "P1Light");
                registry.Register(P1Heavy, new EqsQueryTemplate { BlueprintId = EqsTemplateRegistry.BlueprintIdOf(P1Heavy), Generator = new FixedCostGenerator(900), MaxCandidates = 1024 }, "P1Heavy");

                // 40 units of two forces: eyes and ears (implicit sensors), half of them making noise
                var units = new List<Entity>();
                var translator = new PerceptionTkbTranslator();
                for (int i = 0; i < 40; i++)
                {
                    var e = w.CreateEntity();
                    var pos = new Vector3((float)rnd.NextDouble() * 400f, (float)rnd.NextDouble() * 400f, 0f);
                    w.AddComponent(e, new SimTransform { Position = pos, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(rnd.NextDouble() * Math.PI * 2)) });
                    w.AddComponent(e, new EntityInfo { ForceId = i % 2 == 0 ? ForceId.Friend : ForceId.Hostile });
                    w.AddComponent(e, new NetworkIdentity { Value = 7000 + i });
                    var t = new TkbTemplate("P1Unit" + i, 3400 + i);
                    t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 90f + (i % 3) * 40f, FieldOfViewDegrees = (i % 3) switch { 0 => 360f, 1 => 120f, _ => 60f }, HearingRange = 250f });
                    translator.Inject(w, e, t);
                    if (i % 2 == 1)
                        w.AddComponent(e, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter
                            { FiringAudibleRange = 600f, FiringClass = (byte)SoundSourceClass.SmallArms, CurrentMovingRange = 40f });
                    grid.Add(e, new Vector2(pos.X, pos.Y));
                    units.Add(e);
                }
                for (int i = 0; i < 20; i++)   // blockers for the sight test
                {
                    var c = w.CreateEntity();
                    w.AddComponent(c, new SimTransform { Position = new Vector3((float)rnd.NextDouble() * 400f, (float)rnd.NextDouble() * 400f, 0f), Rotation = Quaternion.Identity });
                    w.AddComponent(c, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 4f });
                }
                w.SetSingleton(new SpatialGridData { Grid = grid });

                // twins: three areas, each asked twice for the same unit (CE-3056 shares them)
                var queries = new List<Entity>();
                for (int a = 0; a < 3; a++)
                {
                    var area = w.CreateEntity();
                    w.AddComponent(area, new SimTransform { Position = new Vector3(100f + a * 100f, 200f, 0f), Rotation = Quaternion.Identity });
                    var ecb = (EntityCommandBuffer)view.GetCommandBuffer();
                    ecb.AddManagedComponent(area, new EditablePolyline
                        { Points = new List<Vector2> { new(-60f, -60f), new(60f, -60f), new(60f, 60f), new(-60f, 60f) }, Version = 1 });
                    ecb.Playback(w);
                    for (int k = 0; k < 2; k++)
                    {
                        var s = w.CreateEntity();
                        var cfg = EntitiesOfForceInArea.SensorFor(area, ForceId.Hostile);
                        cfg.ContextSlot0 = units[a * 2];
                        w.AddComponent(s, cfg);
                        w.AddComponent(s, new EqsCognitiveBuffer());
                        queries.Add(s);
                    }
                }
                Entity Local(Guid template, EqsPriorityBand band)
                {
                    var s = w.CreateEntity();
                    w.AddComponent(s, new EqsSensor { BlueprintId = EqsTemplateRegistry.BlueprintIdOf(template), Epoch = 1, SearchRadius = 10f, Priority = (byte)band });
                    w.AddComponent(s, new EqsCognitiveBuffer());
                    queries.Add(s);
                    return s;
                }
                var bands = new[] { EqsPriorityBand.Critical, EqsPriorityBand.Normal, EqsPriorityBand.Low };
                for (int i = 0; i < 24; i++) Local(i % 6 == 0 ? P1Heavy : P1Light, bands[i % 3]);

                var solver = new EqsSolverSystem { BudgetUnits = 4000, Workers = workers };
                var sounds = new Fdp.Toolkit.Perception.Signatures.SoundEmissionSystem();
                var gridBuilder = new Fdp.Toolkit.Perception.Systems.LocalGridBuilderSystem(perception.Grid);
                var ticks = new List<string>();
                for (int t = 0; t < 20; t++)
                {
                    if (t == 6) w.GetComponentRW<EqsSensor>(queries[8]).Epoch++;                    // an epoch bump
                    if (t == 10) for (int i = 0; i < 9; i++) Local(P1Light, bands[i % 3]);           // a never-solved cohort
                    for (int i = 1 + t % 2; i < units.Count; i += 4)                                  // shots this tick
                        w.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification { Shooter = units[i] });
                    w.Bus.SwapBuffers();
                    sounds.Execute(view, 0.1f);
                    gridBuilder.Execute(view, 0.1f);
                    solver.Execute(view, 0.1f);
                    parallelSolves += solver.LastParallelSolves;
                    discarded      += solver.LastDiscardedSolves;
                    ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(w);
                    w.Bus.SwapBuffers();
                    ticks.Add(P1Snapshot(w, solver, units, queries));
                    w.Tick();
                }
                return ticks;
            }
            finally
            {
                DisposeEqsSingletons(w);
                perception.Dispose();
                grid.Dispose();
            }
        }

        private static string P1Snapshot(EntityRepository w, EqsSolverSystem solver, List<Entity> units, List<Entity> queries)
        {
            var view = (ISimulationView)w;
            var sb = new System.Text.StringBuilder();
            sb.Append("run=").Append(string.Join(",", solver.LastSchedule.Select(e => e.Index)))
              .Append(" units=").Append(solver.LastSpentUnits).Append(" copies=").Append(solver.LastSharedCopies);
            var pool = w.GetSingletonUnmanaged<EqsResultPool>();
            foreach (var r in view.ReadEvents<EqsResultEvent>().ToArray())
            {
                sb.Append(" | R ").Append(Fields(r)).Append(" [");
                for (int i = 0; i < r.EntryCount; i++) sb.Append(Fields(pool.Results[r.ResultHandle + i])).Append(';');
                sb.Append(']');
            }
            foreach (var e in view.ReadEvents<Fdp.Toolkit.Perception.Events.SoundContactEvent>().ToArray()) sb.Append(" | S ").Append(Fields(e));
            foreach (var e in view.ReadEvents<Fdp.Toolkit.Perception.Events.SensorTrackStateEvent>().ToArray()) sb.Append(" | T ").Append(Fields(e));
            var sensors = new List<Entity>(queries);
            foreach (var u in units)
            {
                sensors.Add(UnitSensors.Of(w, u, SensorModality.Visual));
                sensors.Add(UnitSensors.Of(w, u, SensorModality.Acoustic));
            }
            foreach (var s in sensors)
            {
                if (s.IsNull) continue;
                sb.Append(" | E").Append(s.Index);
                if (w.HasComponent<SensorEvalState>(s))
                {
                    var ev = w.GetComponentRO<SensorEvalState>(s);
                    sb.Append(' ').Append(ev.LastSolvedTick).Append('/').Append(ev.LastCost).Append('/').Append(ev.Phase)
                      .Append('/').Append(ev.CurrentEpoch).Append('/').Append(ev.PublishedThisEpoch);
                }
                if (w.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticCursor>(s))
                    sb.Append(" c").Append(w.GetComponentRO<Fdp.Toolkit.Perception.Signatures.AcousticCursor>(s).LastPlace);
            }
            return sb.ToString();
        }

        // The public fields of an event / result, primitives and entities only.
        private static string Fields(object o)
            => string.Join(",", o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(Entity))
                .Select(f => f.Name + "=" + f.GetValue(o)));

        /// <summary>
        /// ⭐ CE-3427 P-1 (R-2084) — a warm parallel run allocates nothing on the solver's thread: the window, the slots, the hand-off to
        /// the workers and the twin memo all reuse what the first runs built. 🔴 Red-proof: a per-window <c>new</c> (or the old
        /// per-answer <c>ToArray</c> share memo) ⇒ bytes.
        /// </summary>
        [Fact]
        public void P1_AWarmParallelRun_AllocatesNothingOnTheSolversThread()
        {
            var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            try
            {
                var registry = (EqsTemplateRegistry)EqsTemplateRegistry.InstallDefault(w);
                registry.Register(P1Light, new EqsQueryTemplate { BlueprintId = EqsTemplateRegistry.BlueprintIdOf(P1Light), Generator = new FixedCostGenerator(20), MaxCandidates = 32 }, "P1Light");
                for (int i = 0; i < 200; i++)
                {
                    var s = w.CreateEntity();
                    w.AddComponent(s, new EqsSensor { BlueprintId = EqsTemplateRegistry.BlueprintIdOf(P1Light), Epoch = 1, SearchRadius = 10f });
                    w.AddComponent(s, new EqsCognitiveBuffer());
                }
                var solver = new EqsSolverSystem { BudgetUnits = 6000, Workers = 4 };
                var view = (ISimulationView)w;
                void Tick()
                {
                    solver.Execute(view, 0.1f);
                    ((EntityCommandBuffer)view.GetCommandBuffer()).Playback(w);
                    w.Bus.SwapBuffers();
                    w.Tick();
                }
                for (int i = 0; i < 10; i++) Tick();   // warm: every buffer has reached its size
                long before = GC.GetAllocatedBytesForCurrentThread();
                solver.Execute(view, 0.1f);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(solver.LastParallelSolves > 0, "the run must solve on the window");
                Assert.Equal(0, bytes);
            }
            finally { DisposeEqsSingletons(w); }
        }
    }
}
