using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.CGF.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionAdapterSystem"/> (TASK-TI004).
    /// Verifies that the system publishes <see cref="AssignTacticalIntentEvent"/> when a valid
    /// BehaviorId is present, and publishes nothing when BehaviorId is empty/whitespace.
    /// </summary>
    public class MissionAdapterSystemTests
    {
        // -- Helpers ----------------------------------------------------------

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<MissionPlanQueue>();
            repo.RegisterComponent<BehaviorState>();
            repo.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();
            repo.RegisterManagedComponent<ActiveMissionPlan>();
            return repo;
        }

        private static Entity CreateMissionEntity(EntityRepository repo, string behaviorId, string behaviorParams = "{}")
        {
            var entity = repo.CreateEntity();

            // Add the struct components
            var queue = new MissionPlanQueue { TaskCount = 1, CurrentTask = 0 };
            Span<MissionTaskSlot> phases = queue.Tasks;
            phases[0] = new MissionTaskSlot { BehaviorId = 0 };
            repo.AddComponent(entity, queue);

            repo.AddComponent(entity, new BehaviorState());

            // Set the managed ActiveMissionPlan
            repo.SetManagedComponent(entity, new ActiveMissionPlan
            {
                Plan = new DomainMissionPlan
                {
                    Tasks = new List<DomainMissionTask>
                    {
                        new DomainMissionTask
                        {
                            BehaviorName     = behaviorId,
                            BehaviorParams = behaviorParams,
                        }
                    }
                }
            });

            return entity;
        }

        // -- SC-1: valid BehaviorId -> AssignTacticalIntentEvent published -----

        /// <summary>
        /// SC-1: Entity with a valid BehaviorId.
        /// After Execute, one <see cref="AssignTacticalIntentEvent"/> is published with the
        /// correct IntentId and Entity; no <see cref="AssignBehaviorEvent"/> is emitted.
        /// </summary>
        [Fact]
        public void Execute_ValidBehaviorId_PublishesAssignTacticalIntentEvent()
        {
            using var repo = CreateWorld();
            var entity = CreateMissionEntity(repo, "WanderMilitary");
            var system = new MissionAdapterSystem();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var intentEvents   = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();

            Assert.Single(intentEvents);
            Assert.Equal("WanderMilitary", intentEvents[0].IntentId);
            Assert.Equal(entity, intentEvents[0].Entity);
            Assert.Empty(behaviorEvents);
        }

        // -- ⭐ CE-3042 — an EMPTY queue is no plan: nothing is cleared; a plan that RAN OUT still clears ----------

        [Theory]
        [InlineData(0, 0, false)]   // every TKB unit's queue — must not wipe its default / restored behaviour
        [InlineData(1, 1, true)]    // exhausted
        public void CE3042_OnlyAnExhaustedPlan_ClearsTheBehaviour(int phaseCount, int currentPhase, bool clears)
        {
            using var repo = CreateWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new MissionPlanQueue { TaskCount = (byte)phaseCount, CurrentTask = (byte)currentPhase });
            repo.AddComponent(entity, new BehaviorState());

            new MissionAdapterSystem().Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            bool cleared = false;
            foreach (var e in repo.Bus.Read<ClearBehaviorEvent>()) cleared |= e.Entity == entity;
            Assert.Equal(clears, cleared);
        }

        // -- SC-3: empty BehaviorId -> no event published ----------------------

        /// <summary>
        /// SC-3: Entity with an empty BehaviorId.
        /// After Execute, no <see cref="AssignTacticalIntentEvent"/> is published.
        /// </summary>
        [Fact]
        public void Execute_EmptyBehaviorId_NoEventPublished()
        {
            using var repo = CreateWorld();
            CreateMissionEntity(repo, string.Empty);
            var system = new MissionAdapterSystem();

            system.Execute(repo, 0.016f);
            repo.Bus.SwapBuffers();

            var intentEvents = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Empty(intentEvents);
        }
    
        /// <summary>
        /// ⭐ CE-3299 D1 — the adapter's dispatch memory is RECORDED: a restored run keeps the phase it last sent instead of re-sending
        /// it (which restarted the phase behaviour, cf. CE-2304). ⛔ It was Transient, so a keyframe dropped it.
        /// </summary>
        [Fact]
        public void CE3299_D1_TheDispatchMemory_SurvivesAKeyframe()
        {
            using var repo = CreateWorld();
            Assert.True(ComponentTypeRegistry.IsRecordable(ComponentTypeRegistry.GetId(typeof(Hrot.CGF.Components.MissionAdapterState))));
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Hrot.CGF.Components.MissionAdapterState { LastPhase = 3, LastPlanVersion = 7 });
            repo.Tick();
            var keyframe = Fdp.Toolkit.Replay.WorldKeyframes.Capture(repo, Fdp.Toolkit.Replay.CaptureOptions.Recording);
            repo.Tick();
            repo.RemoveComponent<Hrot.CGF.Components.MissionAdapterState>(e);
            Fdp.Toolkit.Replay.WorldKeyframes.Apply(repo, keyframe);
            var back = repo.GetComponent<Hrot.CGF.Components.MissionAdapterState>(e);
            Assert.Equal(((byte)3, 7u), (back.LastPhase, back.LastPlanVersion));
        }

        // ── ⭐⭐ CE-4298 — mission plan M1 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.2 D2, §5.9, E9): ONE start path; no condition = when done;
        //    the Wait task. The director and the adapter run together, as the CGF pack composes them. ───────────────────────────────────────────

        private static Entity PlanOf(EntityRepository repo, params (string Name, List<DomainMissionTrigger>? Triggers)[] tasks)
        {
            var entity = repo.CreateEntity();
            var queue = new MissionPlanQueue { TaskCount = (byte)tasks.Length, CurrentTask = 0 };
            Span<MissionTaskSlot> phases = queue.Tasks;
            var domain = new List<DomainMissionTask>();
            for (int i = 0; i < tasks.Length; i++)
            {
                var hrot = tasks[i].Triggers?.ConvertAll(t => new Hrot.Core.Mission.MissionTrigger { Type = t.Type, Params = t.Params });
                var (kind, param) = Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(hrot, tasks[i].Name);
                phases[i] = new MissionTaskSlot { BehaviorId = 0, Trigger = kind, TriggerParam = param };
                domain.Add(new DomainMissionTask { TaskId = System.Guid.NewGuid(), BehaviorName = tasks[i].Name, BehaviorParams = "{}", Triggers = tasks[i].Triggers ?? new() });
            }
            repo.AddComponent(entity, queue);
            repo.AddComponent(entity, new BehaviorState());
            repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = new DomainMissionPlan { Tasks = domain } });
            return entity;
        }

        /// <summary>One frame of the CGF order (director, then adapter); returns what was published.</summary>
        private static (List<string> Intents, int Assigns, int Clears) Frame(EntityRepository repo, Fdp.Toolkit.Behavior.Systems.MissionDirectorSystem director,
            MissionAdapterSystem adapter, float dt = 0.1f, BehaviorFinishedEvent? finished = null)
        {
            if (finished is { } f) { repo.Bus.Publish(f); }
            repo.Bus.SwapBuffers();
            director.Execute(repo, dt);
            adapter.Execute(repo, dt);
            repo.Bus.SwapBuffers();
            var intents = new List<string>();
            foreach (var e in repo.Bus.ReadManaged<AssignTacticalIntentEvent>()) intents.Add(e.IntentId);
            int assigns = 0, clears = 0;
            foreach (var _ in repo.Bus.Read<AssignBehaviorHashEvent>()) assigns++;
            foreach (var _ in repo.Bus.Read<ClearBehaviorEvent>()) clears++;
            return (intents, assigns, clears);
        }

        private static BehaviorFinishedEvent Done(Entity e)
            => new() { Entity = e, Result = Fbt.NodeStatus.Success, Origin = BehaviorOrigin.Superior };

        [Fact]
        public void CE4298_ThreeBareTasks_RunOneByOne_EachStartedOnce_ByName_NeverByTheDirector()
        {
            using var repo = CreateWorld();
            var director = new Fdp.Toolkit.Behavior.Systems.MissionDirectorSystem();
            var adapter = new MissionAdapterSystem();
            var e = PlanOf(repo, ("Order.Move", null), ("Order.Hold", null), ("WanderMilitary", null));

            var f0 = Frame(repo, director, adapter);
            Assert.Equal(new[] { "Order.Move" }, f0.Intents);
            Assert.Equal(0, f0.Assigns);                                   // ⭐ D2 — no Idle frame: the director never assigns a named task
            Assert.Empty(Frame(repo, director, adapter).Intents);         // a bare task does not end by itself (when done)
            Assert.Empty(Frame(repo, director, adapter, dt: 600f).Intents);   // ⭐ §5.9 — no condition is NOT "after some time"

            var f1 = Frame(repo, director, adapter, finished: Done(e));
            Assert.Equal(new[] { "Order.Hold" }, f1.Intents);
            Assert.Equal(0, f1.Assigns);
            var f2 = Frame(repo, director, adapter, finished: Done(e));
            Assert.Equal(new[] { "WanderMilitary" }, f2.Intents);
            var f3 = Frame(repo, director, adapter, finished: Done(e));
            Assert.Empty(f3.Intents);
            Assert.Equal(1, f3.Clears);                                    // the plan ran out: ONE clear (the adapter's), not two
            Assert.Equal(3, repo.GetComponentRO<MissionPlanQueue>(e).CurrentTask);
        }

        [Fact]
        public void CE4298_AWaitFirstPlan_StartsNothing_UntilItsThen_AndAMidPlanWaitClearsThePlansBehaviour()
        {
            using var repo = CreateWorld();
            var director = new Fdp.Toolkit.Behavior.Systems.MissionDirectorSystem();
            var adapter = new MissionAdapterSystem();
            var after1s = new List<DomainMissionTrigger> { new() { Type = "TimerElapsed", Params = "1" } };
            var e = PlanOf(repo, ("", after1s), ("Order.Move", null), ("", after1s), ("Order.Hold", null));

            var w = Frame(repo, director, adapter, dt: 0.5f);
            Assert.Empty(w.Intents);
            Assert.Equal(0, w.Clears);                                     // at the start nothing was ordered: the SOP keeps running, nothing cleared
            Assert.Equal(new[] { "Order.Move" }, Frame(repo, director, adapter, dt: 0.6f).Intents);   // its then (after 1 s) holds

            var mid = Frame(repo, director, adapter, finished: Done(e));  // Move done -> the second Wait
            Assert.Empty(mid.Intents);
            Assert.Equal(1, mid.Clears);                                   // ⭐ E9 — the plan's previous behaviour is cleared: the SOP takes over
            Assert.Equal(new[] { "Order.Hold" }, Frame(repo, director, adapter, dt: 1.1f).Intents);
        }

        [Fact]
        public void CE4298_AThenCondition_AndAGiveUp_AreConditions_GiveUpEndsTheTaskFailed()
        {
            using var repo = CreateWorld();
            var director = new Fdp.Toolkit.Behavior.Systems.MissionDirectorSystem();
            var adapter = new MissionAdapterSystem();
            string after(double s) => Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.ToJson(
                Fdp.Toolkit.Behavior.Systems.PlanConditionCache.Translate(MissionTrigger.TimerElapsed, (float)s));
            var e = PlanOf(repo,
                ("Order.Move", new List<DomainMissionTrigger> { new() { Type = "Then", Params = after(100) }, new() { Type = "GiveUp", Params = after(2) } }),
                ("Order.Hold", new List<DomainMissionTrigger> { new() { Type = "Then", Params = after(1) } }));

            Frame(repo, director, adapter, dt: 1f);
            Assert.Equal(new[] { "Order.Hold" }, Frame(repo, director, adapter, dt: 1.5f).Intents);   // gave up at 2 s, long before its then
            var q = repo.GetComponentRO<MissionPlanQueue>(e);
            Assert.Equal(MissionPhaseOutcome.Failed, q.Outcomes[0]);
            Assert.Equal(0f, (float)q.TaskElapsedSeconds, 3);
        }

        [Fact]
        public void CE4298_ASteadyFrame_AllocatesNothing()
        {
            using var repo = CreateWorld();
            var director = new Fdp.Toolkit.Behavior.Systems.MissionDirectorSystem();
            var adapter = new MissionAdapterSystem();
            PlanOf(repo, ("Order.Move", new List<DomainMissionTrigger> { new() { Type = "TimerElapsed", Params = "1000" } }), ("Order.Hold", null));
            for (int i = 0; i < 3; i++) { repo.Bus.SwapBuffers(); director.Execute(repo, 0.1f); adapter.Execute(repo, 0.1f); }   // warm: compiled, started
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) { repo.Bus.SwapBuffers(); director.Execute(repo, 0.1f); adapter.Execute(repo, 0.1f); }
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
