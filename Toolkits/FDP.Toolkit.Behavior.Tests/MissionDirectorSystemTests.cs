using System;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionDirectorSystem"/> (BCS-P6-T1).
    /// Tests set up a <see cref="MissionPlanQueue"/> directly and verify that the system
    /// correctly evaluates trigger conditions and advances doctrine state.
    /// </summary>
    public class MissionDirectorSystemTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly MissionDirectorSystem _sys;

        public MissionDirectorSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<DoctrineState>();
            _world.RegisterComponent<MissionPlanQueue>();
            _world.RegisterComponent<NavState>();
            _world.RegisterComponent<HealthData>();

            _sys = new MissionDirectorSystem();
            _sys.Create(_world);
        }

        public void Dispose()
        {
            _sys.Dispose();
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private const float Dt60Hz = 1f / 60f; // ≈ 0.016667 s per tick at 60 Hz

        private void SetDeltaTime(float dt)
            => _world.SetSingleton(new GlobalTime { DeltaTime = dt, TimeScale = 1f });

        /// <summary>
        /// Creates an entity with a two-phase timer-based mission plan.
        /// Phase 0: doctrine=<paramref name="docA"/>, TimerElapsed(<paramref name="phase0Duration"/>s).
        /// Phase 1: doctrine=<paramref name="docB"/>, TimerElapsed(1000s — effectively infinite).
        /// </summary>
        private Entity CreateTimerEntity(int docA, int docB, float phase0Duration)
        {
            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = docA,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = phase0Duration,
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = docB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 1000f,  // effectively never fires in these tests
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = docA, InstanceId = 0 });

            return entity;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After 31 ticks at 60 Hz (≈ 0.517 s &gt; 0.5 s), a TimerElapsed(0.5 s) phase must fire:
        /// <c>ActiveDoctrineHash</c> must switch to the Phase 1 doctrine and
        /// <c>CurrentPhase</c> must equal 1.
        /// </summary>
        [Fact]
        public void MissionDirector_AdvancesPhase_WhenTimerElapses()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 100;
            const int DocB = 200;
            var entity = CreateTimerEntity(DocA, DocB, phase0Duration: 0.5f);

            // Run 31 ticks: 31 × (1/60) ≈ 0.517 s ≥ 0.5 s → phase should advance.
            for (int i = 0; i < 31; i++) _sys.Run();

            ref var queue   = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, queue.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After 10 ticks at 60 Hz (≈ 0.167 s &lt; 0.5 s), the phase must not have advanced.
        /// </summary>
        [Fact]
        public void MissionDirector_DoesNotAdvance_WhenTimerNotElapsed()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 100;
            const int DocB = 200;
            var entity = CreateTimerEntity(DocA, DocB, phase0Duration: 0.5f);

            // Run 10 ticks: 10 × (1/60) ≈ 0.167 s < 0.5 s → no advance.
            for (int i = 0; i < 10; i++) _sys.Run();

            ref var queue   = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(0, queue.CurrentPhase);
            Assert.Equal(DocA, doctrine.ActiveDoctrineHash);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.ReachedDestination"/> phase must not advance while
        /// <c>NavState.HasArrived == 0</c>, and must advance in the next tick after
        /// <c>NavState.HasArrived</c> is set to 1.
        /// </summary>
        [Fact]
        public void MissionDirector_AdvancesPhase_WhenReachedDestination()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 300;
            const int DocB = 400;

            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = DocA,
                Trigger      = MissionTrigger.ReachedDestination,
                TriggerParam = 0f,
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = DocB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 1000f,
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });
            _world.AddComponent(entity, new NavState { HasArrived = 0 });

            // First tick — not arrived yet, phase must stay at 0.
            _sys.Run();

            ref var q1 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, q1.CurrentPhase);

            // Signal arrival.
            ref var nav = ref _world.GetComponentRW<NavState>(entity);
            nav.HasArrived = 1;

            // Second tick — destination reached, phase must advance.
            _sys.Run();

            ref var q2      = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, q2.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After both phases of a 2-phase queue have elapsed, further ticks must not crash
        /// and <c>CurrentPhase</c> must remain at 2 (≥ <c>PhaseCount</c>).
        /// </summary>
        [Fact]
        public void MissionDirector_StopsAtEndOfQueue()
        {
            // Use a tiny timer so each phase fires quickly.
            SetDeltaTime(1.0f);   // 1 second per tick

            const int DocA = 500;
            const int DocB = 600;
            const int DocC = 700;

            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = DocA,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 0.5f,   // fires after 1 tick (dt=1.0 ≥ 0.5)
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = DocB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 0.5f,   // fires after 1 tick
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });

            // Tick 1 — Phase 0 fires, advances to Phase 1.
            _sys.Run();
            ref var q1 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(1, q1.CurrentPhase);

            // Tick 2 — Phase 1 fires, advances CurrentPhase to 2 (== PhaseCount → mission complete).
            _sys.Run();
            ref var q2 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, q2.CurrentPhase);

            // Tick 3 — CurrentPhase (2) >= PhaseCount (2): system must skip silently, no crash.
            var exception = Record.Exception(() => _sys.Run());
            Assert.Null(exception);

            ref var q3 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, q3.CurrentPhase);   // unchanged
        }

        // ── Test 5 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.HealthCritical"/> phase must advance when
        /// <c>HealthData.Fraction</c> &lt;= <c>TriggerParam</c> (5 / 100 = 0.05 &lt;= 0.10).
        /// </summary>
        [Fact]
        public void MissionDirector_AdvancesPhase_WhenHealthCritical()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 800;
            const int DocB = 900;

            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = DocA,
                Trigger      = MissionTrigger.HealthCritical,
                TriggerParam = 0.10f,   // 10 % threshold
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = DocB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 1000f,
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });
            // 5 / 100 = 0.05 ≤ 0.10  →  trigger must fire.
            _world.AddComponent(entity, new HealthData { Current = 5f, Max = 100f });

            _sys.Run();

            ref var q  = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, q.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }

        // ── Test 6 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.HealthCritical"/> phase must NOT advance when
        /// <c>HealthData.Fraction</c> &gt; <c>TriggerParam</c> (50 / 100 = 0.50 &gt; 0.10).
        /// </summary>
        [Fact]
        public void MissionDirector_DoesNotAdvance_WhenHealthAboveThreshold()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 800;
            const int DocB = 900;

            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = DocA,
                Trigger      = MissionTrigger.HealthCritical,
                TriggerParam = 0.10f,
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = DocB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 1000f,
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });
            // 50 / 100 = 0.50 > 0.10  →  trigger must NOT fire.
            _world.AddComponent(entity, new HealthData { Current = 50f, Max = 100f });

            _sys.Run();

            ref var q  = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(0, q.CurrentPhase);
            Assert.Equal(DocA, doctrine.ActiveDoctrineHash);
        }
    }
}
