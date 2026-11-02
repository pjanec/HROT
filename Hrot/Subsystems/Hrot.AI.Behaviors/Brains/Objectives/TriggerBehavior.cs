using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Map.Definitions.Behavior;
using StructEdit.Reflection;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S2-c — the compiled <c>when</c> of a trigger, cached PROCESS-WIDE by its JSON string (the compiled delegate holds no world: it takes the
    /// repository and the entity as arguments). The hot path is a reference lookup of the very string the entity's <see cref="ActionArgs"/> holds; a miss
    /// goes to the content-keyed table, so two triggers authored with the same condition share one delegate.
    /// </summary>
    public static class TriggerWhen
    {
        private static readonly IPredicateCompiler Compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        private static readonly ConcurrentDictionary<string, Func<EntityRepository, Entity, bool>> ByContent = new();
        private static readonly ConditionalWeakTable<string, Func<EntityRepository, Entity, bool>> ByReference = new();

        /// <summary>The LEVEL predicate of <paramref name="json"/> (compiled once). ⛔ Throws when the JSON is not a predicate or does not compile.</summary>
        public static Func<EntityRepository, Entity, bool> Get(string json)
        {
            if (ByReference.TryGetValue(json, out var hit)) return hit;
            var f = ByContent.GetOrAdd(json, Compile);
            ByReference.AddOrUpdate(json, f);
            return f;
        }

        private static Func<EntityRepository, Entity, bool> Compile(string json)
        {
            var dto = JsonSerializer.Deserialize<SearchPredicateDto>(json, BehaviorParams.JsonOptions)
                      ?? throw new InvalidOperationException("the condition is empty");
            return Compiler.CompileLevelPredicate(dto);
        }

        /// <summary>The canonical JSON of an authored condition (what <see cref="ActionArgs"/> item 0 holds).</summary>
        public static string ToJson(SearchPredicateDto when) => JsonSerializer.Serialize(when, BehaviorParams.JsonOptions);
    }

    /// <summary>
    /// ⭐ rules S2-c (📄 docs/DESIGN_Scenario_Rules.md §10.11 / §00.9) — the <c>Trigger</c> behaviour: the one brain of a Trigger entity (TKB 8861). A small
    /// state machine (<see cref="TriggerState"/>) over the blackboard — Disarmed · Armed · Holding · Waiting · Acting · Fired · Cooldown · Done — that
    /// watches the LEVEL of its <c>when</c> and fires on the rising EDGE (T1), after an optional debounce (<c>holdFor</c>) and delay (<c>delay</c>, T3), by running
    /// its <c>do</c> list through <see cref="ScenarioActions.RunActions"/> (T7). It writes <see cref="ObjectiveStatus"/> with <c>Role = Trigger</c> like every
    /// objective kind (through <see cref="ObjectiveHost"/>); <c>State</c> mirrors the machine coarsely (Inactive = Disarmed, Active = armed … cooling down,
    /// Achieved = Done, Failed = an action failed) so aggregators and the HUD read one shape. Time is the scenario simulation clock only (T5).
    /// </summary>
    public static class TriggerBehavior
    {
        public const string Kind = BehaviorNames.Trigger;

        /// <summary>The step lists live in <see cref="ActionArgs"/>: item 0 = the <c>when</c> JSON, item 1 = the <c>do</c> list.</summary>
        public const int WhenItem = 0, DoItem = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float HoldFor, Delay, Cooldown; public int MaxFires; public byte Repeat, FireIfTrueWhenArmed;
            /// <summary>Working state (recorded with the blackboard).</summary>
            public TriggerState Phase;
            /// <summary>The condition last time it was evaluated (0/1), and whether there was a last time (the first armed tick only records it).</summary>
            public byte Prev, PrevKnown;
            /// <summary>The rising edge (sim s); when the delay ends; when the cooldown ends.</summary>
            public double EdgeAt, WaitUntil, CooldownUntil;
            /// <summary>The <c>do</c> list's runner state: <c>RunActions</c>' own params (list 1, depth 0) and its cursor.</summary>
            public RunActionsParams ListParams; public RunActionsState List;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TriggerBlackboard { public Block Block; }

        /// <summary>
        /// ⛔ CE-4163 — VALIDATION AT ASSIGNMENT: the <c>do</c> list (every act exists, its args read strictly) and the <c>when</c> (it compiles) are checked HERE; a
        /// failure throws and the assignment is refused (the ingress logs it with the entity and the reason — <c>BehaviorIngressSystem.ParseIntoShadow</c>), never
        /// at first use, when step 0 may already have run. Writes <see cref="ActionArgs"/> on the entity, replaced whole.
        /// </summary>
        [BehaviorResolver(BehaviorNames.Trigger)]
        public static void Resolve(in TriggerParamsJsonDto authored, ref TriggerBlackboard bb, EntityRepository world, Entity self)
        {
            if (authored.When is null) throw new InvalidOperationException("Trigger: 'when' is required (a condition, e.g. {\"$type\":\"OnEntity\",...}).");
            string whenJson = TriggerWhen.ToJson(authored.When);
            string doJson = authored.Do?.ToJsonString() ?? "[]";
            try { TriggerWhen.Get(whenJson); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { throw new InvalidOperationException($"Trigger: 'when' does not compile — {ex.GetType().Name}: {ex.Message}", ex); }
            try { ActionList.Validate(doJson); }
            catch (ActionListException ex) { throw new InvalidOperationException($"Trigger: 'do' is invalid — {ex.Message}", ex); }

            var common = authored.Common;
            common.Scored ??= false;   // T8: a trigger is not scored unless it says so
            bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in common),
                HoldFor = Math.Max(0f, authored.HoldFor), Delay = Math.Max(0f, authored.Delay), Cooldown = Math.Max(0f, authored.Cooldown),
                MaxFires = Math.Max(0, authored.MaxFires), Repeat = (byte)(authored.Repeat ? 1 : 0), FireIfTrueWhenArmed = (byte)(authored.FireIfTrueWhenArmed ? 1 : 0),
                ListParams = new RunActionsParams { ListRef = DoItem, Depth = 0 },
            };
            if (world != null && world.IsAlive(self)) world.SetManagedComponent(self, new ActionArgs(new[] { whenJson, doJson }));
        }

        private const double Eps = 1e-9;

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            // a trigger closes by its OWN rule below (never the objectives' OnClose: an unfired trigger did not "fail")
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Trigger, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Hold);
            double now = f.Now;
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Reset)   // SR-114: a reset re-arms it from scratch
            {
                b.Phase = TriggerState.Disarmed; b.Prev = 0; b.PrevKnown = 0; b.List = default;
                s.FireCount = 0; s.LastFired = 0; s.TriggerState = TriggerState.Disarmed;
            }
            // SR-116: the exercise is closing — a trigger still running its list (EndExercise among it) finishes it (T7), then it is closed;
            // one that never fired is switched off first (it did not "fail": it is not an objective)
            if (s.Closed == 0 && b.Phase != TriggerState.Acting && b.Phase != TriggerState.Fired && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
            {
                if (!s.IsFinal) { s.State = ObjectiveState.Inactive; s.ChangedAt = now; s.Reason = new FixedString64("exercise ended"); b.Phase = TriggerState.Disarmed; }
                s.Closed = 1;
            }
            if (s.Closed == 0)
            {
                if (b.Phase == TriggerState.Acting) Act(ref b, ref s, self, world, now);
                else if (s.State == ObjectiveState.Inactive) { if (b.Phase != TriggerState.Disarmed) Disarm(ref b, ref s, "disarmed", now); }
                else if (s.State == ObjectiveState.Active) Step(ref b, ref s, self, world, now);
            }
            s.TriggerState = b.Phase;
            return NodeStatus.Running;
        }

        /// <summary>Armed … Cooldown, evaluated while the objective state is Active.</summary>
        private static void Step(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            if (b.Phase == TriggerState.Disarmed) { Go(ref b, ref s, TriggerState.Armed, "armed", now); b.PrevKnown = 0; }
            if (b.Phase == TriggerState.Fired) { AfterFired(ref b, ref s, now); return; }
            if (b.Phase == TriggerState.Done) return;
            if (ObjectiveRules.DeadlinePassed(in b.Common, now))
            {
                ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Inactive, "deadline passed", now);   // T4: its deadline disarms it
                Disarm(ref b, ref s, "deadline passed", now);
                return;
            }

            if (b.Phase == TriggerState.Waiting)
            {
                if (now >= b.WaitUntil - Eps) StartActing(ref b, ref s, self, world, now);   // fires even if the condition dropped meanwhile (T3)
                return;
            }

            bool cond = TriggerWhen.Get(((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items[WhenItem])(world, self);
            switch (b.Phase)
            {
                case TriggerState.Armed:
                {
                    bool edge = b.PrevKnown != 0 ? cond && b.Prev == 0 : cond && b.FireIfTrueWhenArmed != 0;   // CE-4166: already true when armed is NOT an edge, unless opted in
                    b.Prev = (byte)(cond ? 1 : 0); b.PrevKnown = 1;
                    if (!edge) break;
                    b.EdgeAt = now;
                    if (b.HoldFor > 0f) Go(ref b, ref s, TriggerState.Holding, $"condition rose, holding {b.HoldFor:0.##} s", now);
                    else AfterHold(ref b, ref s, self, world, now);
                    break;
                }
                case TriggerState.Holding:
                    if (!cond) { b.Prev = 0; b.PrevKnown = 1; Go(ref b, ref s, TriggerState.Armed, "condition dropped before the hold", now); }
                    else if (now - b.EdgeAt >= b.HoldFor - Eps) AfterHold(ref b, ref s, self, world, now);
                    break;
                case TriggerState.Cooldown:
                    if (now >= b.CooldownUntil - Eps && !cond)   // T2: a repeat needs the cooldown over AND the condition false again (a new edge)
                    {
                        b.Prev = 0; b.PrevKnown = 1;
                        Go(ref b, ref s, TriggerState.Armed, "re-armed", now);
                    }
                    break;
            }
        }

        private static void AfterHold(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            if (b.Delay > 0f) { b.WaitUntil = now + b.Delay; Go(ref b, ref s, TriggerState.Waiting, $"firing in {b.Delay:0.##} s", now); }
            else StartActing(ref b, ref s, self, world, now);
        }

        private static void StartActing(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            b.ListParams = new RunActionsParams { ListRef = DoItem, Depth = 0 };
            b.List = default;
            ActionFailure.Clear(world, self);
            Go(ref b, ref s, TriggerState.Acting, "running its actions", now);
            Act(ref b, ref s, self, world, now);   // instant steps finish in the firing tick (T7)
        }

        private static void Act(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            var st = ScenarioActions.RunActions(ref b.ListParams, ref b.List, self, world);
            if (st == NodeStatus.Running) return;
            // ⚠ re-acquire: the list may have added components to this entity (a failure record, a step slot block)
            s = ref world.GetComponentRW<ObjectiveStatus>(self);
            s.FireCount++; s.LastFired = now;
            if (st == NodeStatus.Success) { Go(ref b, ref s, TriggerState.Fired, $"fired ({s.FireCount})", now); return; }
            string reason = ActionFailure.TryGet(world, self, out var fail) ? $"step {fail.Step}: {fail.Reason}" : "an action failed";
            ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Failed, reason, now);   // final: the machine ends here, in Fired
            Go(ref b, ref s, TriggerState.Fired, reason, now);
        }

        private static void AfterFired(ref Block b, ref ObjectiveStatus s, double now)
        {
            if (b.Repeat == 0 || (b.MaxFires > 0 && s.FireCount >= b.MaxFires))
            {
                ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Achieved, $"done after {s.FireCount} fire(s)", now);
                Go(ref b, ref s, TriggerState.Done, $"done after {s.FireCount} fire(s)", now);
            }
            else
            {
                b.CooldownUntil = s.LastFired + b.Cooldown;
                Go(ref b, ref s, TriggerState.Cooldown, $"cooling down {b.Cooldown:0.##} s", now);
            }
        }

        private static void Disarm(ref Block b, ref ObjectiveStatus s, string reason, double now)
        {
            b.PrevKnown = 0; b.Prev = 0;
            Go(ref b, ref s, TriggerState.Disarmed, reason, now);
        }

        private sealed class Log { }

        /// <summary>One transition: the phase, the status text, and ONE log line (the brain journal records the rest — BV-16).</summary>
        private static void Go(ref Block b, ref ObjectiveStatus s, TriggerState next, string reason, double now)
        {
            var old = b.Phase;
            if (old == next) return;
            b.Phase = next;
            s.TriggerState = next;
            if (s.Closed == 0) s.Reason = new FixedString64(reason);
            Fdp.Core.Logging.FdpLog<Log>.Info($"trigger '{s.Title}': {old} → {next} ({reason}) at {now:0.###} s");
        }

        [Behavior(BehaviorNames.Trigger, Params = typeof(TriggerParamsJsonDto), Categories = BehaviorCategory.Trigger)]
        [BTreeDefinition(BehaviorNames.Trigger, Curated = true, Group = "Library/Objectives", ParamsType = typeof(TriggerBlackboard))]
        public static BTreeBuilder<TriggerBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<TriggerBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
