using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules.Actions;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>
    /// ⭐ <c>InsertMacro</c>'s params (macros S3b B1, 📄 docs/DESIGN_Scenario_Macros.md §16): which macro, where (<c>at</c>: coordinates or an
    /// <c>"@Key"</c> / GUID — the step runner resolves it when the step starts, SR-157), its heading, and — <c>ArgsRef</c> — the index of the
    /// macro's args JSON in the caller's <see cref="ActionArgs.Items"/> (the step's nested <c>"params"</c>, lifted at assignment by
    /// <see cref="ActionList.ExtractParams"/>: the SAME route <c>Order</c> takes a behaviour's params by). -1 = the macro's declared defaults.
    /// </summary>
    public struct InsertMacroParams : IActionArgsCheck
    {
        public FixedString64 Macro;
        public PointLink At;
        public Bearing Heading;
        public int ArgsRef;
        public InsertMacroParams() { Macro = default; At = default; Heading = default; ArgsRef = -1; }

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "macro", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var m))
                {
                    if (string.IsNullOrWhiteSpace(m)) return "macro is empty";
                    if (m.Length > 63) return $"the macro path is {m.Length} characters; at most 63";
                    return null;
                }
            return "macro is required";
        }
    }

    /// <summary>⭐ <c>InsertMacro</c>'s working state: the GUID its instance is created with (two halves — unmanaged), when it asked.</summary>
    public struct InsertMacroState { public byte Began; public ulong GuidLo, GuidHi; public double AskedAt; }

    /// <summary>
    /// ⭐⭐ Macros S3b B1 (SR-93 / SR-96 / SR-157 / SR-167 / SR-171) — <c>InsertMacro</c>: a scenario macro instance created at RUNTIME, by the
    /// SAME creation path a load takes (the node servicing the request expands it — <c>CreateEntityRequestSystem</c> marks it Expanding,
    /// <c>MacroExpansionSystem</c> does the work). The action never counts the parts (SR-96): it reads the instance's replicated
    /// <see cref="MacroExpansionStatus"/> — <c>Expanding</c> ⇒ Running, <c>Done</c> ⇒ Success, <c>Failed</c> ⇒ Failure with its reason.
    /// </summary>
    public static class MacroVerbs
    {
        /// <summary>Sim seconds the created instance may take to appear on this node before the step fails.</summary>
        public const double AppearTimeoutSeconds = 30;

        /// <summary>
        /// ① creates the instance (once): TRANSIENT (SR-171 — the trigger is what is saved, never what it inserted), at <c>At</c> facing
        /// <c>Heading</c>, its GUID DERIVED from the inserter's GUID + the tick (SR-167 — replay and a restore re-insert the same ids).
        /// ② then follows its status until the expansion ends.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus InsertMacro(ref InsertMacroParams p, ref InsertMacroState s, Entity self, EntityRepository world)
        {
            double now = ObjectiveHost.Now(world);
            if (s.Began == 0)
            {
                string macro = p.Macro.ToString();
                if (string.IsNullOrWhiteSpace(macro)) return Fail(world, self, "no macro named");
                string args = "{}";
                if (p.ArgsRef >= 0)
                {
                    var items = world.HasManagedComponent<ActionArgs>(self) ? ((ISimulationView)world).GetManagedComponentRO<ActionArgs>(self).Items : null;
                    if (items == null || p.ArgsRef >= items.Count) return Fail(world, self, $"macro params item {p.ArgsRef} is missing");
                    args = items[p.ArgsRef];
                }
                string? refused = MacroInsertion.Request(world, self, macro, p.At, p.Heading, args, out Guid guid);
                if (refused != null) return Fail(world, self, refused);
                var bytes = guid.ToByteArray();
                s.GuidLo = BitConverter.ToUInt64(bytes, 0); s.GuidHi = BitConverter.ToUInt64(bytes, 8);
                s.Began = 1; s.AskedAt = now;
                return NodeStatus.Running;
            }

            var state = MacroInsertion.State(world, GuidOf(s), out string why);
            if (state is null)
                return now - s.AskedAt >= AppearTimeoutSeconds - 1e-9 ? Fail(world, self, "the macro instance never appeared") : NodeStatus.Running;
            return state switch
            {
                MacroExpansionState.Done => NodeStatus.Success,
                MacroExpansionState.Failed => Fail(world, self, why.Length > 0 ? why : "the macro expansion failed"),
                _ => NodeStatus.Running,
            };
        }

        private static Guid GuidOf(in InsertMacroState s)
        {
            Span<byte> b = stackalloc byte[16];
            BitConverter.TryWriteBytes(b, s.GuidLo);
            BitConverter.TryWriteBytes(b.Slice(8), s.GuidHi);
            return new Guid(b);
        }

        private static NodeStatus Fail(EntityRepository world, Entity self, string reason)
        {
            ActionFailure.Set(world, self, reason);
            return NodeStatus.Failure;
        }
    }
}
