using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐⭐ <c>CE-3035</c> (R-189, R-198) — the unit's SOP SLOT, beside <see cref="BehaviorState"/>: its own logic (what to do
    /// when idle, how to react), any tier, ticked by the same runners inside a <see cref="BrainSlotScope"/>.
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §6 · <c>docs/DESIGN_Decision_Layer.md</c> §4.
    /// <para>⭐ An SOP never finishes (on Success / Failure it restarts) · a FAULT stops it and it stays visible
    /// (<see cref="Faulted"/>, R-193) · it commands no channels (a write is reverted and faults it) · it ticks at
    /// <c>SopTickPeriod</c> and at once when woken (R-195) · its run tokens have the high bit set, so they are never a task's.</para>
    /// <para>⚠ <c>NoScenario</c>: the scenario saves the SOP as <c>{Name, Params, Origin}</c> (<c>CE-3042</c>, R-192).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SopState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct SopState
    {
        /// <summary>The SOP behaviour's id; 0 = no SOP (absolute control).</summary>
        public int SopHash;
        /// <summary>The SOP run's token — high bit set (<see cref="SopTokens"/>).</summary>
        public uint SopInstanceId;
        /// <summary>The SOP's tier (BTree / HSM / blueprint).</summary>
        public byte SopBrainTier;
        /// <summary>Who assigned the SOP; a lower origin cannot replace it.</summary>
        public BehaviorOrigin SopOrigin;
        /// <summary>1 once the SOP faulted: it stays assigned and visible, and is not ticked (R-193).</summary>
        public byte SopFaulted;
        /// <summary>1 when something it should react to happened — it ticks at once.</summary>
        public byte SopWake;
        /// <summary>Sim time (s) of its next scheduled tick.</summary>
        public double SopNextTick;
    }

    /// <summary>⭐ <c>CE-3035</c> — the SOP slot's run tokens live in a space DISJOINT from a task's (high bit set), so a
    /// channel command carrying one is never the running task's and is cancelled by construction.</summary>
    public static class SopTokens
    {
        /// <summary>The next SOP run token (never 0, high bit always set) — from the one <see cref="RunTokens"/> allocator.</summary>
        public static uint Next() => RunTokens.Next();

        /// <summary>Is <paramref name="instanceId"/> a SIDE-space token (an SOP run's, or a task-slot run started while a task
        /// was paused — <see cref="RunTokens"/>)?</summary>
        public static bool IsSop(uint instanceId) => RunTokens.IsSide(instanceId);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2081</c> — THE allocator of SIDE-space run tokens (high bit set, process-unique, never 0): the SOP slot's runs
    /// (<c>CE-3035</c>) and every task-slot run started while a task is PAUSED. ⇒ the paused task's own token is never reused
    /// by the reaction that paused it, so restoring it on resume is safe (a reused token would make
    /// <c>BrainTickSystem</c>'s finish de-dup swallow the next run's end). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.9a.
    /// </summary>
    public static class RunTokens
    {
        private static uint _next;

        /// <summary>The next side-space token.</summary>
        public static uint Next() => 0x8000_0000u | (System.Threading.Interlocked.Increment(ref _next) & 0x7FFF_FFFFu);

        /// <summary>Is <paramref name="instanceId"/> a side-space token?</summary>
        public static bool IsSide(uint instanceId) => (instanceId & 0x8000_0000u) != 0;
    }

    /// <summary>⭐ <c>CE-3035</c> — what the SOP slot was started with (the twin of <see cref="BehaviorStartRecord"/>).</summary>
    [ComponentId(GlobalComponentIds.SopStartRecord)]
    [DataPolicy(DataPolicy.Transient)]
    public sealed class SopStartRecord
    {
        /// <summary>The SOP behaviour's name.</summary>
        public required string BehaviorName { get; init; }
        /// <summary>The params JSON it was started with.</summary>
        public required string JsonParams { get; init; }
        /// <summary>The run it belongs to.</summary>
        public required uint InstanceId { get; init; }

        /// <summary>
        /// ⭐ <c>CE-2085</c> — the hosted occurrences this SOP run attached LAZILY (a subtree its tree started later). They are
        /// keyed under the SOP's run but named by no manifest, so the task's sweep could not tell them from its own. Noted
        /// by <see cref="OccurrenceWorkingState"/> when it attaches inside the SOP's <see cref="BrainSlotScope"/>; protected
        /// from the task's sweep; detached when the SOP run ends. 📄 docs/DESIGN_Sensors_And_Doctrine.md §6.
        /// </summary>
        public System.Collections.Generic.List<int> HostedKeys { get; } = new();

        /// <summary>Notes <paramref name="key"/> as attached by the SOP run currently in scope for <paramref name="self"/>.</summary>
        internal static void NoteHosted(Fdp.Core.EntityRepository world, Fdp.Core.Entity self, int key)
        {
            if (!BrainSlotScope.TryGetInstanceId(self, out uint run)) return;
            if (!world.TryGetTable(typeof(SopStartRecord), out _) || !world.HasManagedComponent<SopStartRecord>(self)) return;
            var record = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<SopStartRecord>(self);
            if (record == null || record.InstanceId != run || record.HostedKeys.Contains(key)) return;
            record.HostedKeys.Add(key);
        }
    }
}
