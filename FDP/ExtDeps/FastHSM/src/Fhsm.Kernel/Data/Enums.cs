using System;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// State behavior flags (packed into 16 bits).
    /// </summary>
    [Flags]
    public enum StateFlags : ushort
    {
        None = 0,
        IsComposite = 1 << 0,       // Has child states
        IsHistory = 1 << 1,         // Tracks last active child
        IsDeepHistory = 1 << 2,     // Deep history vs shallow
        IsParallel = 1 << 3,        // Has orthogonal regions
        HasOnEntry = 1 << 4,        // Has entry action
        HasOnExit = 1 << 5,         // Has exit action
        HasOnUpdate = 1 << 6,       // Has update/activity
        IsInitial = 1 << 7,         // Initial state of parent
        IsFinal = 1 << 8,           // Final state (terminates)

        /// <summary>
        /// ⭐⭐⭐ <b>CE-381 — DERIVED: this state owns at least one <see cref="TransitionFlags.IsPolled"/>
        /// transition.</b> ⛔ Never authored; <c>HsmFlattener</c> computes it.
        ///
        /// <para>⭐⭐ <b>It exists to make the polled scan FREE for states that do not use it.</b> The
        /// kernel's <c>Idle</c> arm already walks leaf→root per region for activities; this bit turns
        /// the polled check into ONE BIT TEST on that existing walk, so a machine with no polled
        /// transition pays nothing. 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.1.</para>
        ///
        /// <para>⚠ <b>It is also what gives polling any <c>StructureHash</c> coverage at all</b> —
        /// <c>ComputeStructureHash</c> hashes <c>state.Flags</c> and NOT <c>trans.Flags</c>. 🔒 The
        /// residual gap (toggling one of two polled transitions in one state) is ACCEPTED and
        /// documented at §8b, on the ground that polling changes no layout.</para>
        /// </summary>
        HasPolledTransition = 1 << 9,
        Reserved10 = 1 << 10,
        Reserved11 = 1 << 11,
        Reserved12 = 1 << 12,
        Reserved13 = 1 << 13,
        Reserved14 = 1 << 14,
        Reserved15 = 1 << 15,
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Event ids the kernel reserves — never allocated to an authored event.</b>
    /// 📐 Authored ids start at 1 (<c>HsmEmitCore</c> treats <c>0</c> as "unset" and assigns a
    /// fallback), and <c>0</c> itself is the RTC loop's COMPLETION pass.
    /// </summary>
    public static class ReservedEventIds
    {
        /// <summary>The RTC loop's completion pass — see <c>ProcessRTCPhase</c>, which sets
        /// <c>currentEventId = 0</c> after every executed transition.</summary>
        public const ushort Completion = 0;

        /// <summary>
        /// ⭐⭐⭐ <b>CE-381 — a POLLED transition's event id.</b> It exists so a polled transition is
        /// <b>unreachable from the event path BY CONSTRUCTION</b>: it matches no authored event and,
        /// crucially, not the <see cref="Completion"/> pass either.
        ///
        /// <para>🔴 <b>Without it the two concepts collide.</b> Authoring a polled transition as
        /// <c>.On(0)</c> would give it <c>EventId 0</c>, so the RTC completion pass would select it
        /// AS WELL as the polled scan — exactly the conflation §2.3 records and §10 ③ rejected.
        /// ⭐ <c>HsmFlattener</c> NORMALISES every polled transition onto this id, so no authoring
        /// route can produce the collision.</para>
        /// </summary>
        public const ushort Polled = 0xFFFD;

        /// <summary>The timer phase's synthetic event (<c>HsmKernelCore.TimerEventId</c>).</summary>
        public const ushort Timer = 0xFFFE;
    }

    /// <summary>
    /// Transition behavior flags (packed into 16 bits).
    /// Includes priority in high bits (bits 12-15 = 4-bit priority).
    /// </summary>
    [Flags]
    public enum TransitionFlags : ushort
    {
        None = 0,
        
        // Behavior flags (bits 0-11)
        IsExternal = 1 << 0,        // External transition (exit + enter)
        IsInternal = 1 << 1,        // Internal (no exit/entry)
        HasGuard = 1 << 2,          // Has guard condition
        HasEffect = 1 << 3,         // Has effect action
        IsInterrupt = 1 << 4,       // Interrupt-class (high priority)
        IsSynchronized = 1 << 5,    // Part of sync group

        /// <summary>
        /// ⭐⭐⭐ <b>CE-381 — a POLLED transition: evaluated every quiescent tick, not on an event.</b>
        ///
        /// <para>The kernel's <c>Idle</c> arm scans the active configuration for transitions carrying
        /// this bit and evaluates their guards; an unmarked transition is only ever considered in the
        /// event phase. 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.1.</para>
        ///
        /// <para>⛔⛔ <b>NOT the same thing as an eventless (completion) transition.</b> A transition
        /// with <c>EventId == 0</c> is already selected by the RTC loop's completion pass — it fires
        /// ONCE, as a consequence of another transition. A polled transition fires whenever its guard
        /// passes on a quiescent tick. 🔒 One encoding cannot carry both, which is why this is an
        /// explicit flag and not a reuse of event 0 (§2.3).</para>
        /// </summary>
        IsPolled = 1 << 6,

        // Reserved (bits 7-11)
        Reserved7 = 1 << 7,
        Reserved8 = 1 << 8,
        Reserved9 = 1 << 9,
        Reserved10 = 1 << 10,
        Reserved11 = 1 << 11,
        
        // Priority (bits 12-15): 0 = lowest, 15 = highest
        Priority_Mask = 0xF000,     // Bits 12-15
    }

    /// <summary>
    /// Event priority classes.
    /// </summary>
    public enum EventPriority : byte
    {
        Low = 0,
        Normal = 1,
        Interrupt = 2,
    }

    /// <summary>
    /// Instance lifecycle phase (for RTC execution tracking).
    /// </summary>
    public enum InstancePhase : byte
    {
        Idle = 0,           // Not executing
        Entry = 1,          // Phase 1: Entry/Pre-tick processing
        RTC = 2,            // Phase 2: Run-to-completion (transitions)
        Activity = 3,       // Phase 3: Activities (Update)
    }

    /// <summary>
    /// Instance flags (status and error conditions).
    /// </summary>
    [Flags]
    public enum InstanceFlags : byte
    {
        None = 0,
        EventOverflow = 1 << 0,         // Event queue overflow
        CommandOverflow = 1 << 1,       // Command buffer overflow
        CriticalCommandOverflow = 1 << 2, // Critical lane overflow
        BudgetExceeded = 1 << 3,        // Microstep budget exceeded
        Terminated = 1 << 4,            // Reached final state
        Error = 1 << 5,                 // Unrecoverable error
        
        DebugTrace = 1 << 6,            // Enable tracing for this instance
        Paused = 1 << 7,               // Instance is paused; kernel skips it
    }

    /// <summary>
    /// Event flags (8 bits).
    /// </summary>
    [Flags]
    public enum EventFlags : byte
    {
        None = 0,
        IsDeferred = 1 << 0,        // Event is deferred
        IsIndirect = 1 << 1,        // Payload contains ID, not data
        IsConsumed = 1 << 2,        // Event has been consumed
        Reserved3 = 1 << 3,
        Reserved4 = 1 << 4,
        Reserved5 = 1 << 5,
        Reserved6 = 1 << 6,
        Reserved7 = 1 << 7,
    }

    /// <summary>
    /// Command buffer lanes for prioritization.
    /// </summary>
    public enum CommandLane : byte
    {
        Animation = 0,
        Navigation = 1,
        Gameplay = 2,
        Blackboard = 3,
        Audio = 4,
        VFX = 5,
        Message = 6,
        Count = 7,
        /// <summary>Sentinel: no lane specified / inferred by the editor.</summary>
        None = 0xFF
    }
}

