using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        internal static readonly IPredicateCompiler Compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
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
    /// ⭐ CE-4238 (V-2) — the time an Armed trigger's <c>when</c> WAITS ON: the earliest <c>fromSeconds</c> still in the future of its <c>ScenarioTime</c> leaves — through an Or the
    /// smallest of the children's times, through an And the largest; a leaf already past (<c>fromSeconds ≤ now</c>) and every other node contribute nothing; no time ⇒ 0.
    /// The parsed plan is cached per JSON string like <see cref="TriggerWhen"/> (by reference, then by content); unparseable JSON ⇒ 0 (the view path never throws).
    /// </summary>
    public static class TriggerWhenTime
    {
        private sealed class Plan
        {
            public static readonly Plan None = new();
            public double From = double.NaN;      // a ScenarioTime leaf (NaN = not a leaf)
            public bool IsAnd; public Plan[]? Kids;
        }

        private static readonly ConcurrentDictionary<string, Plan> ByContent = new();
        private static readonly ConditionalWeakTable<string, Plan> ByReference = new();

        /// <summary>The next time (sim s, &gt; <paramref name="now"/>) the condition waits on, or 0.</summary>
        public static double ExpectedAt(string json, double now)
        {
            if (string.IsNullOrEmpty(json)) return 0d;
            if (!ByReference.TryGetValue(json, out var plan))
            {
                plan = ByContent.GetOrAdd(json, Parse);
                ByReference.AddOrUpdate(json, plan);
            }
            return Eval(plan, now);
        }

        private static Plan Parse(string json)
        {
            try { return Build(JsonSerializer.Deserialize<SearchPredicateDto>(json, BehaviorParams.JsonOptions)); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return Plan.None; }
        }

        private static Plan Build(SearchPredicateDto? dto)
        {
            switch (dto)
            {
                case ScenarioTimePredicateDto t: return new Plan { From = t.FromSeconds };
                case CompoundPredicateDto c when c.Conditions is { Count: > 0 }:
                    return new Plan { IsAnd = c.Operator == LogicalOperator.And, Kids = c.Conditions.Select(Build).ToArray() };
                default: return Plan.None;
            }
        }

        private static double Eval(Plan p, double now)
        {
            if (!double.IsNaN(p.From)) return p.From > now ? p.From : 0d;
            if (p.Kids is null) return 0d;
            double r = 0d;
            foreach (var k in p.Kids)
            {
                double t = Eval(k, now);
                if (t <= 0d) continue;
                r = r <= 0d ? t : p.IsAnd ? Math.Max(r, t) : Math.Min(r, t);
            }
            return r;
        }
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
        public const int WhenItem = 0, DoItem = 1, FirstExtraItem = 2;   // ⭐ S2 ⑨ — items 2.. are the steps' nested "params"

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
            public SimInstant EdgeAt, WaitUntil, CooldownUntil;
            /// <summary>The <c>do</c> list's runner state: <c>RunActions</c>' own params (list 1, depth 0) and its cursor.</summary>
            public RunActionsParams ListParams; public RunActionsState List;
            /// <summary>⭐ V-1 — the entities the rule names (net ids): its <c>when</c> reads / its <c>do</c> acts on. <c>LinksPending</c> = some ref was not resolved yet (asked again each tick, <c>LinkTries</c> times at most).</summary>
            public byte DependsOnCount, ActsOnCount, LinksPending, LinkTries;
            public NetIds8 DependsOn, ActsOn;
            /// <summary>⭐ S6 G-4 — a repeat fires again when the cooldown ends with the condition STILL true (no fall-and-rise needed).</summary>
            public byte WhileTrue;
            /// <summary>⭐ CE-4238 follow-up — the <c>do</c> list holds an <c>EndExercise</c> step (copied onto the status for every node's HUD).</summary>
            public byte EndsExercise;
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
            // ⭐ CE-2377 — the checks live in ONE place, the validator (side-effect-free, also run by the scenario sweep / load prepare).
            var problems = ValidateParams(in authored, out string whenJson, out string doJson, out string[] extras);
            if (problems.Count > 0) throw new InvalidOperationException("Trigger: " + string.Join("; ", problems));

            var common = authored.Common;
            common.Scored ??= false;   // T8: a trigger is not scored unless it says so
            bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in common),
                HoldFor = Math.Max(0f, authored.HoldFor), Delay = Math.Max(0f, authored.Delay), Cooldown = Math.Max(0f, authored.Cooldown),
                MaxFires = Math.Max(0, authored.MaxFires), Repeat = (byte)(authored.Repeat ? 1 : 0), FireIfTrueWhenArmed = (byte)(authored.FireIfTrueWhenArmed ? 1 : 0), WhileTrue = (byte)(authored.WhileTrue ? 1 : 0),
                ListParams = new RunActionsParams { ListRef = DoItem, Depth = 0 },
            };
            if (world != null && world.IsAlive(self)) world.SetManagedComponent(self, new ActionArgs(new[] { whenJson, doJson }.Concat(extras).ToArray()));
            ComputeLinks(ref bb.Block, world, whenJson, doJson);   // ⭐ V-1
        }

        private const byte MaxLinkTries = 30;

        /// <summary>⭐ V-1 — DependsOn / ActsOn from the JSON; refs not resolved yet leave <c>LinksPending</c> (the tick asks again, a bounded number of times).</summary>
        private static void ComputeLinks(ref Block b, EntityRepository? world, string whenJson, string doJson)
        {
            var r = RuleLinks.Compute(world, whenJson, doJson);
            b.DependsOn = r.DependsOn; b.DependsOnCount = r.DependsOnCount; b.ActsOn = r.ActsOn; b.ActsOnCount = r.ActsOnCount; b.EndsExercise = r.EndsExercise;
            b.LinksPending = (byte)(r.Complete ? 0 : 1);
        }

        /// <summary>⭐ <c>CE-2377</c> — the Trigger's params check, side-effect-free: <c>when</c> is present and compiles as a LEVEL predicate; <c>do</c> is a valid action list.</summary>
        [BehaviorValidator(BehaviorNames.Trigger)]
        public static IEnumerable<string> Validate(string json)
        {
            TriggerParamsJsonDto dto;
            try { dto = JsonSerializer.Deserialize<TriggerParamsJsonDto>(json, BehaviorParams.JsonOptions); }
            catch (JsonException ex) { return new[] { $"params are not valid JSON — {ex.Message}" }; }
            return ValidateParams(in dto, out _, out _, out _);
        }

        private static List<string> ValidateParams(in TriggerParamsJsonDto authored, out string whenJson, out string doJson, out string[] extras)
        {
            var problems = new List<string>();
            whenJson = ""; doJson = "[]"; extras = Array.Empty<string>();
            if (authored.When is null) problems.Add("'when' is required (a condition, e.g. {\"$type\":\"OnEntity\",...}).");
            else
            {
                whenJson = TriggerWhen.ToJson(authored.When);
                try { TriggerWhen.Get(whenJson); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { problems.Add($"'when' does not compile — {ex.GetType().Name}: {ex.Message}"); }
            }
            doJson = ActionStep.ToJson(authored.Do);
            try
            {
                (doJson, extras) = ActionList.ExtractParams(doJson, FirstExtraItem);   // ⭐ S2 ⑨ — a step's nested "params" become extra ActionArgs items, its ArgsRef their index
                ActionList.Validate(doJson);
            }
            catch (ActionListException ex) { problems.Add($"'do' is invalid — {ex.Message}"); }
            return problems;
        }

        private const double Eps = 1e-9;

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            // a trigger closes by its OWN rule below (never the objectives' OnClose: an unfired trigger did not "fail")
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Trigger, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Hold);
            double now = f.Now;
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (b.LinksPending != 0 && b.LinkTries < MaxLinkTries)   // ⭐ V-1: a ref that did not resolve at assignment — ask again (the world fills in)
            {
                b.LinkTries++;
                var items = ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items;
                ComputeLinks(ref b, world, items[WhenItem], items[DoItem]);
            }
            if (f.Control != RuleRequest.NoRequest)
            {
                Control(ref b, ref s, f.Control, self, world, now);   // ⭐ V-4: Fire / Rearm
                s = ref world.GetComponentRW<ObjectiveStatus>(self);  // ⚠ re-acquire: the actions may have added components
            }
            if (f.Reset)   // SR-114: a reset re-arms it from scratch
            {
                b.Phase = TriggerState.Disarmed; b.Prev = 0; b.PrevKnown = 0; b.List = default;
                s.FireCount = 0; s.LastFired = 0; s.TriggerState = TriggerState.Disarmed;
            }
            // SR-116: the exercise is closing — a trigger still running its list (EndExercise among it) finishes it (T7), then it is closed;
            // one that never fired is switched off first (it did not "fail": it is not an objective)
            if (s.Closed == 0 && b.Phase != TriggerState.Acting && b.Phase != TriggerState.Fired && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
            {
                if (!s.IsFinal) { s.State = ObjectiveState.Inactive; s.ChangedAt = (SimInstant)now; s.Reason = new FixedString64("exercise ended"); b.Phase = TriggerState.Disarmed; }
                s.Closed = 1;
            }
            if (s.Closed == 0)
            {
                if (b.Phase == TriggerState.Acting) Act(ref b, ref s, self, world, now);
                else if (s.State == ObjectiveState.Inactive) { if (b.Phase != TriggerState.Disarmed) Disarm(ref b, ref s, "disarmed", now); }
                else if (s.State == ObjectiveState.Active) Step(ref b, ref s, self, world, now);
            }
            s.TriggerState = b.Phase;
            WriteViewData(ref b, ref s, self, world, now);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ V-4 — an instructor's <c>Fire</c> (the actions run NOW, as if the edge rose: holdFor and delay skipped; only an armed / holding / waiting / cooling-down trigger) or
        /// <c>Rearm</c> (Armed again, the condition as it is NOW counted as already seen — no edge, so no firing while it stays true; a final one needs a Reset). The
        /// other requests (activate / disarm / reset) were applied by <see cref="ObjectiveHost"/>.
        /// </summary>
        private static void Control(ref Block b, ref ObjectiveStatus s, RuleRequest request, Entity self, EntityRepository world, double now)
        {
            // ⭐ CE-2380 part 2 — every exit reports the request's verdict (Applied, or Refused with the same text the status carries) under the id it came with
            if (s.Closed != 0 || s.IsFinal) { Refuse(ref s, world, self, $"{request} refused: it is {s.State}"); return; }
            if (request == RuleRequest.Fire)
            {
                if (s.State != ObjectiveState.Active || b.Phase is TriggerState.Disarmed or TriggerState.Acting or TriggerState.Fired or TriggerState.Done)
                { Refuse(ref s, world, self, $"Fire refused: {(s.State != ObjectiveState.Active ? "it is switched off" : b.Phase.ToString())}"); return; }
                b.EdgeAt = (SimInstant)now;
                StartActing(ref b, ref s, self, world, now);
                ObjectiveHost.ReportControl(world, self, null);
                return;
            }
            // Rearm
            if (b.Phase == TriggerState.Acting) { Refuse(ref s, world, self, "Rearm refused: it is acting"); return; }
            if (s.State != ObjectiveState.Active) ObjectiveRules.SetActive(ref s, true, false, now);
            b.Prev = (byte)(TriggerWhen.Get(((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items[WhenItem])(world, self) ? 1 : 0);
            b.PrevKnown = 1;
            if (b.Phase != TriggerState.Armed) Go(ref b, ref s, TriggerState.Armed, "re-armed by request", now);
            ObjectiveHost.ReportControl(world, self, null);
        }

        private static void Refuse(ref ObjectiveStatus s, EntityRepository world, Entity self, string text)
        {
            s.Reason = new FixedString64(text);
            ObjectiveHost.ReportControl(world, self, text);
        }

        /// <summary>⭐ V-1 / V-2 — the rule's links and timing onto the status (the owner writes them every tick; the wire sends a change once). ExpectedAt: Waiting ⇒ the delay's end ·
        /// Holding ⇒ edge + holdFor · Cooldown ⇒ its end · Armed with a deadline ⇒ the deadline · Armed without ⇒ the next time its <c>when</c> waits on (CE-4238, <see cref="TriggerWhenTime"/>) · else 0.</summary>
        private static void WriteViewData(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            RuleLinks.CopyTo(ref s, in b.DependsOn, b.DependsOnCount, in b.ActsOn, b.ActsOnCount);
            s.HoldFor = b.HoldFor; s.Delay = b.Delay; s.Cooldown = b.Cooldown;
            s.Deadline = b.Common.DeadlineSeconds;
            s.EndsExercise = b.EndsExercise;
            if (s.Closed != 0 || s.IsFinal) { s.ExpectedAt = (SimInstant)0d; return; }
            s.ExpectedAt = b.Phase switch
            {
                TriggerState.Waiting => b.WaitUntil,
                TriggerState.Holding => b.EdgeAt + b.HoldFor,
                TriggerState.Cooldown => b.CooldownUntil,
                TriggerState.Armed when b.Common.DeadlineSeconds > 0f => (SimInstant)b.Common.DeadlineSeconds,
                TriggerState.Armed => (SimInstant)(TriggerWhenTime.ExpectedAt(((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items[WhenItem], now)),
                _ => (SimInstant)0d,
            };
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
                    b.EdgeAt = (SimInstant)now;
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
                    else if (now >= b.CooldownUntil - Eps && b.WhileTrue != 0)   // ⭐ S6 G-4: still true after the cooldown ⇒ the periodic rule fires again, as a fresh edge
                    {
                        b.Prev = 1; b.PrevKnown = 1; b.EdgeAt = (SimInstant)now;
                        if (b.HoldFor > 0f) Go(ref b, ref s, TriggerState.Holding, $"condition still true, holding {b.HoldFor:0.##} s", now);
                        else AfterHold(ref b, ref s, self, world, now);
                    }
                    break;
            }
        }

        private static void AfterHold(ref Block b, ref ObjectiveStatus s, Entity self, EntityRepository world, double now)
        {
            if (b.Delay > 0f) { b.WaitUntil = (SimInstant)(now + b.Delay); Go(ref b, ref s, TriggerState.Waiting, $"firing in {b.Delay:0.##} s", now); }
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
            // ⚠ re-acquire: the list may have added components to this entity (a failure record, a step slot block)
            s = ref world.GetComponentRW<ObjectiveStatus>(self);
            // ⭐ rule editor ① — the ONE place the step counter is written: the running (or failed) step, 1-based, of the list's length.
            int count = ActionList.Get(((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items[DoItem]).Length;
            s.StepCount = (byte)Math.Min(count, 255);
            s.StepCur = (byte)Math.Min(Math.Min(b.List.Index + 1, count), 255);
            if (st == NodeStatus.Running) return;
            s.FireCount++; s.LastFired = now;
            if (st == NodeStatus.Success) { s.StepCur = 0; s.StepCount = 0; Go(ref b, ref s, TriggerState.Fired, $"fired ({s.FireCount})", now); return; }
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
                b.CooldownUntil = (SimInstant)(s.LastFired + b.Cooldown);
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
            if (next != TriggerState.Acting && next != TriggerState.Fired) { s.StepCur = 0; s.StepCount = 0; }   // ⭐ rule editor ①: only Acting (and the failed Fired) keeps a step
            Fdp.Core.Logging.FdpLog<Log>.Info($"trigger '{s.Title}': {old} → {next} ({reason}) at {now:0.###} s");
        }

        [Behavior(BehaviorNames.Trigger, Params = typeof(TriggerParamsJsonDto), Categories = BehaviorCategory.Trigger)]
        [BTreeDefinition(BehaviorNames.Trigger, Curated = true, Group = "Library/Objectives", ParamsType = typeof(TriggerBlackboard))]
        public static BTreeBuilder<TriggerBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<TriggerBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
