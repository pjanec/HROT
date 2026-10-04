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
        private static uint _next;

        /// <summary>The next SOP run token (never 0, high bit always set).</summary>
        public static uint Next() => 0x8000_0000u | (System.Threading.Interlocked.Increment(ref _next) & 0x7FFF_FFFFu);

        /// <summary>Is <paramref name="instanceId"/> an SOP run's token?</summary>
        public static bool IsSop(uint instanceId) => (instanceId & 0x8000_0000u) != 0;
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
    }
}
