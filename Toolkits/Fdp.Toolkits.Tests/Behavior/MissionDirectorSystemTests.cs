using System;
using CarKinem.Core;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat.Components;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
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
        private readonly DoctrineIngressSystem _ingressSys;

        public MissionDirectorSystemTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<DoctrineState>();
            _world.RegisterComponent<MissionPlanQueue>();
            _world.RegisterComponent<NavState>();
            _world.RegisterComponent<Health>();
            _world.RegisterComponent<BrainBTreeState>();
            _world.RegisterComponent<BrainBlackboard>();

            _sys = new MissionDirectorSystem();

            // DoctrineIngressSystem owns DoctrineState writes; required for CORRECTIVE-2
            // tests that verify AssignDoctrineHashEvent delegation.
            _ingressSys = new DoctrineIngressSystem(new DoctrineRegistry());
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private const float Dt60Hz = 1f / 60f; // ≈ 0.016667 s per tick at 60 Hz

        private void SetDeltaTime(float dt)
            => _world.SetSingleton(new GlobalTime { DeltaTime = dt, TimeScale = 1f });

        /// <summary>
        /// Flushes pending <c>AssignDoctrineHashEvent</c>s into <see cref="DoctrineIngressSystem"/>
        /// so that <see cref="DoctrineState.ActiveDoctrineHash"/> reflects the result of a
        /// phase transition in the same test step.
        /// <para>
        /// Required because CORRECTIVE-2 delegates doctrine writes through the event bus:
        /// <see cref="MissionDirectorSystem"/> publishes the event; <c>DoctrineIngressSystem</c>
        /// applies it on the next logical frame (after <c>SwapBuffers</c>).
        /// </para>
        /// </summary>
        private void FlushDoctrineEvents()
        {
            _world.Bus.SwapBuffers();
            _ingressSys.Execute(_world, Dt60Hz);
        }

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
            for (int i = 0; i < 31; i++) _sys.Execute(_world, Dt60Hz);

            // Flush AssignDoctrineHashEvent so DoctrineIngressSystem applies the new hash.
            FlushDoctrineEvents();

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
            for (int i = 0; i < 10; i++) _sys.Execute(_world, Dt60Hz);

            ref var queue   = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(0, queue.CurrentPhase);
            Assert.Equal(DocA, doctrine.ActiveDoctrineHash);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.ReachedDestination"/> phase must advance when a
        /// <see cref="DoctrineFinishedEvent"/> is published for the entity (BS1-T022).
        /// It must NOT advance while no event has arrived, regardless of any
        /// <c>NavState</c> component present on the entity.
        /// </summary>
        [Fact]
#pragma warning disable CS0618 // ReachedDestination obsolete — backward-compat regression test
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
            // NavState present but HasArrived=1 must no longer be sufficient (BS1-T022).
            _world.AddComponent(entity, new NavState { HasArrived = 1 });

            // First tick — no DoctrineFinishedEvent; phase must stay at 0.
            _sys.Execute(_world, Dt60Hz);

            ref var q1 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, q1.CurrentPhase);

            // Publish DoctrineFinishedEvent so the ReachedDestination path fires.
            PublishDoctrineFinished(entity);

            // Second tick — event arrived; phase must advance.
            _sys.Execute(_world, Dt60Hz);

            // Flush AssignDoctrineHashEvent so DoctrineIngressSystem applies the new hash.
            FlushDoctrineEvents();

            ref var q2      = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, q2.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }
#pragma warning restore CS0618

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
            _sys.Execute(_world, 1.0f);
            ref var q1 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(1, q1.CurrentPhase);

            // Tick 2 — Phase 1 fires, advances CurrentPhase to 2 (== PhaseCount → mission complete).
            _sys.Execute(_world, 1.0f);
            ref var q2 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, q2.CurrentPhase);

            // Tick 3 — CurrentPhase (2) >= PhaseCount (2): system must skip silently, no crash.
            var exception = Record.Exception(() => _sys.Execute(_world, 1.0f));
            Assert.Null(exception);

            ref var q3 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(2, q3.CurrentPhase);   // unchanged
        }

        // ── Test 5 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.HealthCritical"/> phase must advance when
        /// <c>Health.Current / Health.Max</c> &lt;= <c>TriggerParam</c> (5 / 100 = 0.05 &lt;= 0.10).
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
            _world.AddComponent(entity, new Health { Current = 5f, Max = 100f });

            _sys.Execute(_world, Dt60Hz);
            // Flush AssignDoctrineHashEvent so DoctrineIngressSystem applies the new hash.
            FlushDoctrineEvents();
            ref var q  = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, q.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }

        // ── Test 6 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A <see cref="MissionTrigger.HealthCritical"/> phase must NOT advance when
        /// <c>Health.Current / Health.Max</c> &gt; <c>TriggerParam</c> (50 / 100 = 0.50 &gt; 0.10).
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
            _world.AddComponent(entity, new Health { Current = 50f, Max = 100f });

            _sys.Execute(_world, Dt60Hz);

            ref var q  = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(0, q.CurrentPhase);
            Assert.Equal(DocA, doctrine.ActiveDoctrineHash);
        }

        // ── Task-4 Tests: DoctrineFinished Trigger + End-of-Mission Clear ─────────────────

        // Helper: publish DoctrineFinishedEvent and swap so system can consume it.
        private void PublishDoctrineFinished(Entity entity, NodeStatus result = NodeStatus.Success)
        {
            _world.Bus.Publish(new DoctrineFinishedEvent { Entity = entity, Result = result });
            _world.Bus.SwapBuffers();
        }

        // Helper: build a single-phase mission with DoctrineFinished trigger.
        private Entity CreateDoctrineFinishedEntity(int docId)
        {
            var entity = _world.CreateEntity();
            var queue  = new MissionPlanQueue();
            queue.PhaseCount = 1;
            queue.Phases[0]  = new MissionPhase
            {
                DoctrineId   = docId,
                Trigger      = MissionTrigger.DoctrineFinished,
                TriggerParam = 0f,
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = docId, InstanceId = 0 });
            return entity;
        }

        [Fact]
        public void DoctrineFinishedTrigger_AdvancesPhase()
        {
            SetDeltaTime(Dt60Hz);
            const int DocA  = 1100;
            var entity = CreateDoctrineFinishedEntity(DocA);

            PublishDoctrineFinished(entity);
            _sys.Execute(_world, Dt60Hz);

            var q = _world.GetComponent<MissionPlanQueue>(entity);
            Assert.Equal(1, q.CurrentPhase); // advanced past the only phase

            // Plan exhausted → ClearDoctrineEvent must be on write buffer.
            _world.Bus.SwapBuffers();
            bool clearPublished = false;
            foreach (var evt in _world.Bus.Read<ClearDoctrineEvent>())
                if (evt.Entity.Index == entity.Index) clearPublished = true;
            Assert.True(clearPublished);
        }

        [Fact]
        public void DoctrineFinishedTrigger_MultiPhase_SetsNextDoctrine()
        {
            SetDeltaTime(Dt60Hz);
            const int DocA = 1200, DocB = 1201;

            var entity = _world.CreateEntity();
            var queue  = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0]  = new MissionPhase { DoctrineId = DocA, Trigger = MissionTrigger.DoctrineFinished };
            queue.Phases[1]  = new MissionPhase { DoctrineId = DocB, Trigger = MissionTrigger.TimerElapsed, TriggerParam = 1000f };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });

            PublishDoctrineFinished(entity);
            _sys.Execute(_world, Dt60Hz);

            // Phase advance: still in plan, so no ClearDoctrineEvent.
            _world.Bus.SwapBuffers();
            bool clearPublished = false;
            foreach (var evt in _world.Bus.Read<ClearDoctrineEvent>())
                if (evt.Entity.Index == entity.Index) clearPublished = true;
            Assert.False(clearPublished);

            // Flush AssignDoctrineHashEvent so DoctrineIngressSystem applies DocB.
            _ingressSys.Execute(_world, Dt60Hz);

            var doctrine = _world.GetComponent<DoctrineState>(entity);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }

        [Fact]
        public void DoctrineFinishedTrigger_WrongEntity_DoesNotFire()
        {
            SetDeltaTime(Dt60Hz);
            const int DocA = 1300;

            var entityA = CreateDoctrineFinishedEntity(DocA); // phase trigger matches this entity
            var entityB = _world.CreateEntity(); // unrelated entity (no queue component)

            // Publish event for entityB — should NOT trigger phase advance on entityA.
            PublishDoctrineFinished(entityB);
            _sys.Execute(_world, Dt60Hz);

            var q = _world.GetComponent<MissionPlanQueue>(entityA);
            Assert.Equal(0, q.CurrentPhase); // no advance
        }

        [Fact]
        public void MissionComplete_PublishesClearDoctrineEvent()
        {
            SetDeltaTime(Dt60Hz);
            const int DocA = 1400;
            var entity = CreateDoctrineFinishedEntity(DocA);

            PublishDoctrineFinished(entity);
            _sys.Execute(_world, Dt60Hz);

            // ClearDoctrineEvent goes to write buffer; swap to read it.
            _world.Bus.SwapBuffers();
            bool found = false;
            foreach (var evt in _world.Bus.Read<ClearDoctrineEvent>())
                if (evt.Entity.Index == entity.Index) found = true;

            Assert.True(found);
        }

        [Fact]
        public void MissionComplete_ViaDoctrineIngress_SetsDoctrineToNone()
        {
            // Integration: MissionDirectorSystem publishes ClearDoctrineEvent; on the NEXT
            // frame DoctrineIngressSystem consumes it and sets ActiveDoctrineHash = 0.
            SetDeltaTime(Dt60Hz);
            const int DocA = 1500;

            // Build world with BrainBlackboard so DoctrineIngressSystem can process.
            _world.RegisterComponent<BrainBlackboard>();

            var registry   = new DoctrineRegistry();
            var ingressSys = new DoctrineIngressSystem(registry);

            var entity = CreateDoctrineFinishedEntity(DocA);
            _world.AddComponent(entity, new BrainBlackboard());

            // Frame 1: MissionDirector consumes DoctrineFinishedEvent → publishes ClearDoctrineEvent.
            PublishDoctrineFinished(entity);
            _sys.Execute(_world, Dt60Hz);

            // Swap: ClearDoctrineEvent now in read buffer for DoctrineIngressSystem.
            _world.Bus.SwapBuffers();

            // Frame 2: DoctrineIngressSystem consumes ClearDoctrineEvent → sets hash to None.
            ingressSys.Execute(_world, 0.016f);

            var doctrine = _world.GetComponent<DoctrineState>(entity);
            Assert.Equal(DoctrineIds.None, doctrine.ActiveDoctrineHash);
        }

        // ── BUG2-A001: HealthCritical reads Health directly ───────────────────

        /// <summary>
        /// BUG2-A001: With <c>HealthData</c> removed, the <c>HealthCritical</c> trigger must
        /// evaluate by reading the <c>Health</c> component directly.
        /// </summary>
        [Fact]
        public void EvaluateTrigger_HealthCritical_ReadFromHealthComponent()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 810;
            const int DocB = 910;

            var entity = _world.CreateEntity();

            var queue = new MissionPlanQueue();
            queue.PhaseCount = 2;
            queue.Phases[0] = new MissionPhase
            {
                DoctrineId   = DocA,
                Trigger      = MissionTrigger.HealthCritical,
                TriggerParam = 0.25f,   // 25 % threshold
            };
            queue.Phases[1] = new MissionPhase
            {
                DoctrineId   = DocB,
                Trigger      = MissionTrigger.TimerElapsed,
                TriggerParam = 1000f,
            };
            _world.AddComponent(entity, queue);
            _world.AddComponent(entity, new DoctrineState { ActiveDoctrineHash = DocA, InstanceId = 0 });
            // 20 / 100 = 0.20 ≤ 0.25 → trigger must fire with Health component only (no HealthData).
            _world.AddComponent(entity, new Health { Current = 20f, Max = 100f });

            _sys.Execute(_world, Dt60Hz);
            FlushDoctrineEvents();

            ref var q = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(1, q.CurrentPhase);
        }

        // ── BS1-T022: ReachedDestination delegates to DoctrineFinished path ──────────────────

        /// <summary>
        /// BS1-T022 SC1: A <see cref="MissionTrigger.ReachedDestination"/> phase must advance
        /// when a <see cref="DoctrineFinishedEvent"/> is published for the entity.
        /// No <c>NavState</c> component is needed (Brain-only world).
        /// </summary>
        [Fact]
#pragma warning disable CS0618 // ReachedDestination obsolete — intentional backward-compat test
        public void ReachedDestination_AdvancesPhase_ViaDoctrineFinishedEvent()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 2100;
            const int DocB = 2101;

            var entity = _world.CreateEntity();
            var queue  = new MissionPlanQueue();
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
            // No NavState component — Brain-only entity.

            // First tick: no DoctrineFinishedEvent — phase must NOT advance.
            _sys.Execute(_world, Dt60Hz);
            ref var q0 = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            Assert.Equal(0, q0.CurrentPhase);

            // Publish DoctrineFinishedEvent and run — phase must advance.
            PublishDoctrineFinished(entity);
            _sys.Execute(_world, Dt60Hz);
            FlushDoctrineEvents();

            ref var q1      = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(1, q1.CurrentPhase);
            Assert.Equal(DocB, doctrine.ActiveDoctrineHash);
        }
#pragma warning restore CS0618

        /// <summary>
        /// BS1-T022 SC2: <c>NavState.HasArrived == 1</c> alone must NOT advance a
        /// <see cref="MissionTrigger.ReachedDestination"/> phase.  The runtime evaluation now
        /// requires a <see cref="DoctrineFinishedEvent"/>, not a physics-layer flag.
        /// </summary>
        [Fact]
#pragma warning disable CS0618 // ReachedDestination obsolete — intentional backward-compat test
        public void ReachedDestination_DoesNotAdvance_WhenOnlyNavStateHasArrived()
        {
            SetDeltaTime(Dt60Hz);

            const int DocA = 2200;
            const int DocB = 2201;

            var entity = _world.CreateEntity();
            var queue  = new MissionPlanQueue();
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
            // Add NavState with HasArrived=1 — should have no effect after BS1-T022.
            _world.AddComponent(entity, new NavState { HasArrived = 1 });

            // Run one tick: no DoctrineFinishedEvent, so the trigger must NOT fire.
            _sys.Execute(_world, Dt60Hz);

            ref var q       = ref _world.GetComponentRW<MissionPlanQueue>(entity);
            var     doctrine = _world.GetComponent<DoctrineState>(entity);

            Assert.Equal(0, q.CurrentPhase);
            Assert.Equal(DocA, doctrine.ActiveDoctrineHash);
        }
#pragma warning restore CS0618
    }
}
