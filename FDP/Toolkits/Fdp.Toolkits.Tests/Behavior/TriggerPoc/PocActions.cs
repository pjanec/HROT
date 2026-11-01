using System;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    public enum PocMode : byte { Slow = 0, Fast = 1 }

    public struct WaitParams { public int Ticks; }
    public struct WaitState { public int Elapsed; }
    public struct NoState { public int Pad; }
    public struct FailParams { public int Code; }
    public struct RecordParams { public int Value; }

    /// <summary>The "rich" params: exercises FixedString / enum / float / Entity through the JSON seeding.</summary>
    public struct RichParams
    {
        public FixedString32 Name;
        public PocMode Mode;
        public Entity Target;
        public float Gain;
        public int N;
    }

    /// <summary>A FAT params struct (> 260 B): bigger than the ingress-sized first block's payload, so its step slot must APPEND a block.</summary>
    public struct FatParams { public FixedString128 A; public FixedString128 B; public int N; }

    /// <summary>⭐ SR-98 — the loop's own params: an index into the caller's <see cref="PocActionArgs"/>.</summary>
    public struct RunParams { public int ListRef; }

    /// <summary>⭐ SR-98 — the loop's working state: the current step, and where/why it failed.</summary>
    public struct RunState { public int Index; public int Failed; public int FailedStep; public int FailReason; }

    /// <summary>One step of a list: the action's name and its JSON args.</summary>
    public sealed record PocStepSpec(string Act, string ArgsJson);

    /// <summary>Measurement hook: did the outer working state's address change while the loop attached / detached a step slot?</summary>
    public static unsafe class PocTrap
    {
        [ThreadStatic] public static int Checks, Moved;
        public static nint AddressOf(ref RunState s) => (nint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref s);
        public static void Note(nint before, ref RunState s) { Checks++; if (AddressOf(ref s) != before) Moved++; }
        public static void Reset() { Checks = Moved = 0; }
    }

    /// <summary>The fake <c>[SharedAiAction]</c> vocabulary. Same stateful signature as <c>SharedNodeStatefulAction</c>.</summary>
    public static class PocActions
    {
        /// <summary>The ONE slot key the loop uses for "the current step" (PoC: fixed; a nested list would need a depth-salted key).</summary>
        public const int StepSlotKey = 0x7C000001;

        [SharedAiAction]
        public static NodeStatus PocWaitTicks(ref WaitParams p, ref WaitState s, Entity self, EntityRepository world)
        {
            s.Elapsed++;
            return s.Elapsed >= p.Ticks ? NodeStatus.Success : NodeStatus.Running;
        }

        [SharedAiAction]
        public static NodeStatus PocFail(ref FailParams p, ref NoState s, Entity self, EntityRepository world)
        {
            if (world.HasComponent<PocLastReason>(self)) world.GetComponentRW<PocLastReason>(self).Code = p.Code;
            return NodeStatus.Failure;
        }

        [SharedAiAction]
        public static NodeStatus PocRecord(ref RecordParams p, ref NoState s, Entity self, EntityRepository world)
        {
            if (s.Pad == 0) { world.GetComponentRW<PocLog>(self).Append(p.Value); s.Pad = 1; }   // once per step run
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus PocRich(ref RichParams p, ref NoState s, Entity self, EntityRepository world)
        {
            world.GetComponentRW<PocLog>(self).Append(p.N);
            return NodeStatus.Success;
        }

        [SharedAiAction]
        public static NodeStatus PocFat(ref FatParams p, ref NoState s, Entity self, EntityRepository world)
        {
            world.GetComponentRW<PocLog>(self).Append(p.N);
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐ SR-98 — the loop AS a shared action. Ticks the current step; Running → stay; Success → detach, next (same call);
        /// all done → Success; Failure → detach, stop, remember which step and why.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus PocRunActions(ref RunParams p, ref RunState s, Entity self, EntityRepository world)
        {
            if (s.Failed != 0) return NodeStatus.Failure;
            var json = ((ISimulationView)world).GetManagedComponentRO<PocActionArgs>(self).Items[p.ListRef];
            var steps = PocStepList.Get(json);
            while (true)
            {
                if (s.Index >= steps.Length) return NodeStatus.Success;
                var step = steps[s.Index];
                var runner = PocCatalogue.RunnerFor(step.Act);
                nint before = PocTrap.AddressOf(ref s);
                var st = runner.Tick(world, self, StepSlotKey, step.ArgsJson);   // ← attaches (+seeds) a slot; the outer `ref s` is alive across this call
                if (st == NodeStatus.Running) { PocTrap.Note(before, ref s); return NodeStatus.Running; }
                runner.Detach(world, self, StepSlotKey);                          // ← and detaches it; `ref s` still alive
                PocTrap.Note(before, ref s);
                if (st == NodeStatus.Success) { s.Index++; continue; }
                s.Failed = 1; s.FailedStep = s.Index;
                s.FailReason = world.HasComponent<PocLastReason>(self) ? world.GetComponentRO<PocLastReason>(self).Code : 0;
                return NodeStatus.Failure;
            }
        }
    }
}
