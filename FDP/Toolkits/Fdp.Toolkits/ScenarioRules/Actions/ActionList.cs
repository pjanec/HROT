using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;

namespace Fdp.Toolkit.ScenarioRules.Actions
{
    /// <summary>One step of a list: the action's name and its JSON args.</summary>
    public sealed record StepSpec(string Act, string ArgsJson);

    /// <summary>⭐ CE-4163 — a step list refused at assignment: names the step index and its action.</summary>
    public sealed class ActionListException : InvalidOperationException
    {
        public int Step { get; }
        public string? Act { get; }
        public ActionListException(int step, string? act, string message, Exception? inner = null)
            : base($"step {step}{(act == null ? "" : $" ('{act}')")}: {message}", inner) { Step = step; Act = act; }
    }

    /// <summary>
    /// ⭐ rules S2-d (SR-93, SR-97) — a step list: a JSON array of <c>{ "act": "...", "args": {...} }</c>, parsed ONCE per distinct JSON string
    /// (the strings are immutable record items of <see cref="ActionArgs"/>), and validated at ASSIGNMENT (CE-4163).
    /// </summary>
    public static class ActionList
    {
        private static readonly ConcurrentDictionary<string, StepSpec[]> Cache = new();

        /// <summary>The parsed steps (cached). ⛔ A malformed list throws <see cref="ActionListException"/> naming the step.</summary>
        public static StepSpec[] Get(string json) => Cache.GetOrAdd(json, Parse);

        private static StepSpec[] Parse(string json)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException ex) { throw new ActionListException(0, null, "the list is not valid JSON: " + ex.Message, ex); }
            var arr = root as JsonArray ?? throw new ActionListException(0, null, "a step list must be a JSON array");
            var steps = new StepSpec[arr.Count];
            for (int i = 0; i < steps.Length; i++)
            {
                var o = arr[i] as JsonObject ?? throw new ActionListException(i, null, "a step must be an object { \"act\", \"args\" }");
                string act = o["act"] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s : throw new ActionListException(i, null, "a step needs a string 'act'");
                steps[i] = new StepSpec(act, o["args"]?.ToJsonString() ?? "{}");
            }
            return steps;
        }

        /// <summary>
        /// ⭐ CE-4163 — checks a <c>do</c> list WITHOUT running anything: every step's action exists and can run as a step (no <c>Entity</c> param),
        /// and its args read STRICTLY into the action's params (an unknown property is an error). ⛔ Throws <see cref="ActionListException"/> naming the
        /// step index and act. Called when a trigger is ASSIGNED — never "at first use", when step 0 may already have run.
        /// </summary>
        public static StepSpec[] Validate(string doJson)
        {
            var steps = Get(doJson);
            for (int i = 0; i < steps.Length; i++)
            {
                StepRunner runner;
                try { runner = ActionCatalogue.RunnerFor(steps[i].Act); }
                catch (InvalidOperationException ex) { throw new ActionListException(i, steps[i].Act, ex.Message, ex); }
                try { runner.CheckArgs(steps[i].ArgsJson); }
                catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
                { throw new ActionListException(i, steps[i].Act, ex.Message, ex); }
            }
            return steps;
        }
    }

    /// <summary>⭐ <c>RunActions</c>' own params: which list of the entity's <see cref="ActionArgs"/>, and the nesting depth (the step-slot salt).</summary>
    public struct RunActionsParams { public int ListRef; public int Depth; }

    /// <summary>⭐ <c>RunActions</c>' working state: the current step, and where it failed.</summary>
    public struct RunActionsState { public int Index; public int Failed; public int FailedStep; }

    /// <summary>
    /// ⭐ rules S2-d (SR-98) — the list runner AS a shared action, so any BTree / HSM / trigger runs "do these things in order" as one node.
    /// </summary>
    public static class ScenarioActions
    {
        /// <summary>
        /// Ticks the current step of <c>ActionArgs.Items[ListRef]</c>: Running → stay; Success → detach its slot, start the next step IN THE SAME
        /// TICK; all done → Success; Failure → detach, stop, stamp <see cref="ActionFailure.Step"/>. Step slots are keyed by <see cref="StepRunner.SlotKey"/>
        /// of <see cref="RunActionsParams.Depth"/> — a list run from inside a step uses depth + 1 and cannot collide (CE-4161).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus RunActions(ref RunActionsParams p, ref RunActionsState s, Entity self, EntityRepository world)
        {
            if (s.Failed != 0) return NodeStatus.Failure;
            var items = ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items;
            if ((uint)p.ListRef >= (uint)items.Count)
                throw new InvalidOperationException($"RunActions: ListRef {p.ListRef} is outside the entity's ActionArgs ({items.Count} items)");
            var steps = ActionList.Get(items[p.ListRef]);
            int slot = StepRunner.SlotKey(p.Depth);
            while (true)
            {
                if (s.Index >= steps.Length) return NodeStatus.Success;
                var step = steps[s.Index];
                var runner = ActionCatalogue.RunnerFor(step.Act);
                var st = runner.Tick(world, self, slot, step.ArgsJson);
                if (st == NodeStatus.Running) return NodeStatus.Running;
                runner.Detach(world, self, slot);
                if (st == NodeStatus.Success) { s.Index++; continue; }
                s.Failed = 1; s.FailedStep = s.Index;
                ActionFailure.Stamp(world, self, s.Index, step.Act);
                return NodeStatus.Failure;
            }
        }
    }
}
