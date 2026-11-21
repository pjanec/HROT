using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Jobs;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Perception.Events;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ N-11 (CE-4473; R-4212, docs/DESIGN_Parallel_Simulation.md §18) — THE GATE: the brains as a per-entity parallel phase give the SAME
    /// world (<see cref="EntityRepository.ContentHash"/>) after N steps at 1 / 2 / 4 / 8 threads, and across two runs. The world: two sides of
    /// infantry on the shipped SOP, each man remembering enemies (real units — their health, place and side are read across entities), hit and
    /// shot at on a seeded schedule so reactions, cover fights and stress breaks run; perception's senses, ingress, stress, the brain and channel
    /// arbitration in production order, the last-tick view taken before each tick as the kernel does.
    /// Run with <c>HROT_READ_CHECK=1</c> the repository also THROWS on any cross-entity read of an undeclared type and any direct write to another
    /// unit — the rail then proves the declarations (<see cref="BrainReads"/>) complete for this content.
    /// 🔴 Red-proof: a deferral removed (a child sensor created directly, a publish not deferred) ⇒ the hash differs across thread counts.
    /// </summary>
    [Collection(ComponentTypeRegistryMutatorCollection.Name)]
    public sealed class ParallelBrainsDeterminismTests
    {
        private readonly ITestOutputHelper _out;
        public ParallelBrainsDeterminismTests(ITestOutputHelper output) => _out = output;

        private static int PerSide = 48;
        private static int Steps = 240;
        private static double s_brainMs;   // the brain's wall time over the run (the measuring rail reads it)

        private static unsafe ulong Run(int threads, bool parallel, out int reactions)
            => Run(threads, parallel, out reactions, null);

        private static unsafe ulong Run(int threads, bool parallel, out int reactions, System.Collections.Generic.Dictionary<(int, int), ulong>? rows)
        {
            bool was = BrainTickSystem.Parallel;
            BrainTickSystem.Parallel = parallel;
            FdpWorkerPool.Resize(threads);
            ParallelEntities.Mode = SimulationMode.Deterministic;
            try
            {
                RunTokens.ResumeAt(0);   // every run numbers its side-space runs from the same start, as a fresh process does (the counter is process-wide)
                using var repo = new EntityRepository();
                SimHostComponentRegistry.RegisterAll(repo);
                CognitiveComponentRegistry.RegisterAll(repo);
                BlueprintTierTable.RegisterAll(repo);
                for (int i = 0; i < 5000; i++) repo.Tick();
                if (!repo.Bus.IsRegistered<SensorChangedEvent>()) repo.Bus.Register<SensorChangedEvent>();
                repo.SetSingletonManaged<IGeographicTransform>(UtmTransform.AtOrigin(0.0, 0.0, 0.0));
                var registry = new BehaviorRegistry();
                CgfBehaviorSetup.LoadFromAiAssembly(registry);
                var ingress = new BehaviorIngressSystem(registry);
                var brain = new BrainTickSystem(registry);
                var senses = new RecentSensesSystem();
                var stress = new StressSystem();
                var arbitration = new ChannelArbitrationSystem();

                var men = new Entity[PerSide * 2];
                for (int i = 0; i < men.Length; i++)
                {
                    var e = repo.CreateEntity();
                    bool friend = i < PerSide;
                    repo.AddComponent(e, new BehaviorState());
                    repo.AddComponent(e, new LocomotionChannel());
                    repo.AddComponent(e, new WeaponChannel());
                    repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
                    repo.AddComponent(e, new StanceIntent());
                    repo.AddComponent(e, new SimTransform { Position = new Vector3((i % PerSide) * 4f, friend ? 0f : 60f, 0f), Rotation = Quaternion.Identity });
                    repo.AddComponent(e, new EntityInfo { ForceId = friend ? ForceId.Friend : ForceId.Hostile });
                    repo.AddComponent(e, new CombatTemperament { Nerve = 0.3f + (i % 7) * 0.1f, Set = CombatTemperament.NerveSet });
                    men[i] = e;
                }

                double time = 0; long frame = 0;
                void Tick()
                {
                    time += 0.016;
                    repo.SetSingleton(new GlobalTime { TotalTime = time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++frame });
                    repo.SetSimulationTime((float)time);
                    repo.Bus.SwapBuffers();
                    repo.Tick();
                    if (ParallelReadCheck.HasDeclarations) repo.CaptureLastTick();   // as the kernel does before the Simulation dispatch
                    senses.Execute(repo, 0.016f);
                    ingress.Execute(repo, 0.016f);
                    stress.Execute(repo, 0.016f);
                    long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    brain.Execute(repo, 0.016f);
                    s_brainMs += (System.Diagnostics.Stopwatch.GetTimestamp() - b0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    arbitration.Execute(repo, 0.016f);
                }

                foreach (var m in men)
                {
                    repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = m, BehaviorName = "Idle", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
                    repo.Bus.PublishManaged(new AssignSopEvent { Entity = m, BehaviorName = "BasicInfantrySop", JsonParams = "{}", Origin = BehaviorOrigin.Superior });
                }
                Tick();

                var rng = new Random(1234);
                reactions = 0;
                for (int step = 0; step < Steps; step++)
                {
                    // every man remembers up to three enemies, seen now (the eye re-stamps them in production)
                    for (int i = 0; i < men.Length; i++)
                    {
                        ref var mem = ref repo.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(EnsureMemory(repo, men[i]));
                        int baseEnemy = i < PerSide ? PerSide : 0;
                        mem.Count = 3;
                        for (int k = 0; k < 3; k++)
                        {
                            var enemy = men[baseEnemy + (i + k * 5) % PerSide];
                            mem.EntityIds[k] = (long)enemy.PackedValue;
                            mem.Freshness[k] = 10f;
                            mem.LastSeenTick[k] = Fdp.Toolkit.Perception.PerceptionClock.Now(repo);
                            mem.Modalities[k] = (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual;
                        }
                    }
                    // the fight: a few hits and near misses a step, on a seeded schedule
                    for (int k = 0; k < 4; k++)
                    {
                        var who = men[rng.Next(men.Length)];
                        repo.Bus.Publish(new SensorChangedEvent { Unit = who, What = k == 0 ? SensorChange.Hit : k == 1 ? SensorChange.FirstThreat : SensorChange.NearMiss });
                    }
                    Tick();
                }
                foreach (var m in men)
                    if (repo.GetComponentRO<BehaviorState>(m).Origin == BehaviorOrigin.Reaction) reactions++;
                return rows == null ? repo.ContentHash() : repo.ContentHash((i, id, h) => rows[(i, id)] = h);
            }
            finally
            {
                BrainTickSystem.Parallel = was;
                FdpWorkerPool.Resize(FdpWorkerPool.Budget());
            }
        }

        /// <summary>Names every (entity, component) that differs between 1 thread and <paramref name="threads"/> (the first 20).</summary>
        private static string FirstDifference(int threads)
        {
            var a = new System.Collections.Generic.Dictionary<(int, int), ulong>();
            var b = new System.Collections.Generic.Dictionary<(int, int), ulong>();
            Run(1, parallel: true, out _, a);
            Run(threads, parallel: true, out _, b);
            var sb = new System.Text.StringBuilder($"the world differs at {threads} threads:\n");
            int n = 0;
            foreach (var kv in a)
            {
                b.TryGetValue(kv.Key, out ulong other);
                if (other == kv.Value) continue;
                string type = kv.Key.Item2 < 0 ? "(entity)" : ComponentTypeRegistry.GetType(kv.Key.Item2)?.Name ?? kv.Key.Item2.ToString();
                sb.Append($"  entity {kv.Key.Item1} {type}\n");
                if (++n == 20) break;
            }
            foreach (var key in b.Keys) if (!a.ContainsKey(key) && n++ < 40) sb.Append($"  only at {threads}: entity {key.Item1} type {key.Item2}\n");
            return sb.ToString();
        }

        private static Entity EnsureMemory(EntityRepository repo, Entity m)
        {
            if (!repo.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(m))
                repo.AddComponent(m, new Fdp.Toolkit.Perception.Components.TargetMemory());
            return m;
        }

        private struct MessageJob : IEntityBatchJob
        {
            public EntityRepository Repo;
            public void Execute(Entity e, int index, BatchContext ctx)
            {
                var p = new Hrot.AI.Behaviors.Brains.Objectives.Verbs.MessageParams { Text = new FixedString128("contact") };
                var s = default(Hrot.AI.Behaviors.Brains.Objectives.Verbs.VerbNoState);
                Hrot.AI.Behaviors.Brains.Objectives.Verbs.ScenarioVerbs.Message(ref p, ref s, e, Repo);
            }
            public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
        }

        private static string MessagesAt(int threads)
        {
            FdpWorkerPool.Resize(threads);
            using var repo = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(repo);
            var list = new Entity[300];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = repo.CreateEntity();
                repo.AddComponent(list[i], new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 5000 + i });
            }
            ParallelEntities.Run(repo, list, list.Length, new MessageJob { Repo = repo });
            repo.Bus.SwapBuffers();
            var sb = new System.Text.StringBuilder();
            int first = -1;
            foreach (var m in repo.Bus.ReadManaged<Fdp.Toolkit.ScenarioRules.PlayerMessageEvent>())
            {
                if (first < 0) first = m.MessageId;
                sb.Append(m.Source).Append(':').Append(m.MessageId - first).Append(' ');   // ids relative: the counter is process-wide
            }
            return sb.ToString();
        }

        /// <summary>⭐ N-15 (CE-4477; status §5) — rules sending a player message inside the parallel brain phase: every message is NUMBERED in
        /// batch order by the phase's serial follow-up, so the (sender, id) pairs are the same at 1 / 2 / 4 / 8 threads (receivers de-dup on
        /// (MessageId, origin)). 🔴 Red-proof: drop the in-phase deferral in <c>ScenarioVerbs.Message</c> ⇒ ids follow the threads ⇒ red.</summary>
        [Fact]
        public void N15_RuleMessagesInAPhase_AreNumberedInBatchOrder_AtAnyThreadCount()
        {
            try
            {
                string one = MessagesAt(1);
                Assert.Equal(300, one.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.StartsWith("5000:0 5001:1 5002:2 ", one);   // batch order = the entities' order
                foreach (int t in new[] { 2, 4, 8 }) Assert.Equal(one, MessagesAt(t));
            }
            finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
        }

        /// <summary>⭐ N-16 — the brain's wall time at 1 / 2 / 4 threads on a bigger fight (a measuring run: <c>HROT_MEASURE=1</c>).</summary>
        [Fact]
        public void N16_TheBrainsWallTime_At1_2_4Threads()
        {
            if (System.Environment.GetEnvironmentVariable("HROT_MEASURE") != "1") return;   // a measuring run only (no skippable facts in this suite)
            int ps = PerSide, st = Steps;
            PerSide = 400; Steps = 120;
            try
            {
                var report = new System.Text.StringBuilder();
                Run(1, parallel: false, out _); s_brainMs = 0;   // warm
                Run(1, parallel: false, out _); report.Append($"serial {s_brainMs / Steps:F2} ms/tick; ");
                foreach (int t in new[] { 1, 2, 4 })
                {
                    s_brainMs = 0; Run(t, parallel: true, out _);
                    report.Append($"parallel@{t} {s_brainMs / Steps:F2} ms/tick; ");
                }
                Assert.Fail("MEASURE " + report);   // a measuring run reports through the failure text
            }
            finally { PerSide = ps; Steps = st; }
        }

        [Fact]
        public void N11_TheParallelBrains_GiveTheSameWorld_At1_2_4_8Threads_AndAcrossRuns()
        {
            ulong one = Run(1, parallel: true, out int reactions);
            if (ParallelReadCheck.Survey)
            {   // HROT_READ_CHECK=2 — list every undeclared cross read / cross write the content made, then stop (a measuring run)
                var report = new System.Text.StringBuilder();
                foreach (var kv in ParallelReadCheck.SurveyReport()) report.Append("SURVEY ").Append(kv.Key).Append('\n').Append(kv.Value).Append('\n');
                Assert.Fail(report.Length == 0 ? "SURVEY clean" : report.ToString());   // a measuring run reports through the failure text
            }
            _out.WriteLine($"1 thread: {one:X16}, {reactions} men on a reaction at the end");
            Assert.True(reactions > 0, "the fight never reached a reaction — the rail would not exercise the brains");
            foreach (int t in new[] { 2, 4, 8, 1 })
            {
                ulong at = Run(t, parallel: true, out _);
                _out.WriteLine($"{t} threads: {at:X16}");
                if (at != one) Assert.Fail(FirstDifference(t));
            }
        }
    }
}
