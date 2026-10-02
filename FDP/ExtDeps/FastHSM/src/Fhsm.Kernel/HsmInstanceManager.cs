using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    public static class HsmInstanceManager
    {
        /// <summary>
        /// Initialize a new instance. Sets phase to Idle, clears all state.
        /// </summary>
        public static unsafe void Initialize<T>(T* instance, HsmDefinitionBlob definition) 
            where T : unmanaged
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            // Verify size matches tier
            int size = sizeof(T);
            int selectedTier = SelectTier(definition);
            
            // Basic size check - though strictly we might just trust the T provided
            // But good to warn if mismatch? taking "T" allows the caller to alloc the memory.
            
            // ⭐ O7c (2026-09-22): delegates to the size-driven form, so the generic and slot-resident
            //   paths cannot drift. The body moved, not the behaviour.
            Initialize((byte*)instance, size, definition);
        }

        /// <summary>
        /// Reset instance to initial state. Clears active states, history, events.
        /// Preserves DefinitionId.
        /// </summary>
        public static unsafe void Reset<T>(T* instance) where T : unmanaged
        {
             if (instance == null) throw new ArgumentNullException(nameof(instance));
             
             ref InstanceHeader header = ref Unsafe.As<T, InstanceHeader>(ref *instance);
             
             // Preserve specific fields
             uint machineId = header.MachineId;
             ushort generation = header.Generation;
             uint rngState = header.RngState;
             
             // Zero everything
             Unsafe.InitBlock(instance, 0, (uint)sizeof(T));
             
             // Restore/Update fields
             header.MachineId = machineId;
             header.Generation = (ushort)(generation + 1); // Increment generation
             header.RngState = rngState;
             header.Phase = InstancePhase.Entry;

             // Mark as uninitialized
             HsmKernelCore.ResetInstance((byte*)instance, sizeof(T));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Initialize an instance that lives at a POINTER of a known SIZE</b> — the slot-resident
        /// form of <see cref="Initialize{T}"/>. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.8.
        ///
        /// <para>⛔⛔ <b>The deciding argument is MEMORY SAFETY, not convenience</b>, and it is the same one
        /// <see cref="HsmKernel.Update(HsmDefinitionBlob, byte*, int, void*, float, Data.CommandPage*, Data.HsmTraceContext*)"/>
        /// already makes: with occurrence payloads packed adjacently inside one component, a generic
        /// overload whose <c>sizeof(TInstance)</c> exceeds the slot writes past the payload and into the
        /// NEXT OCCURRENCE'S bytes — no compiler check, no runtime check. <b>Sizing from the allocation
        /// cannot disagree with the allocation.</b></para>
        ///
        /// <para>⭐ <b>Exactly the generic body, with <c>sizeof(T)</c> replaced by the caller's size.</b>
        /// ⚠ Deliberately NOT re-expressed: the generic overload now delegates here, so the two cannot
        /// drift — which is the failure mode this repo files as "two producers of one fact".</para>
        /// </summary>
        public static unsafe void Initialize(byte* instance, int instanceSize, HsmDefinitionBlob definition)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (instanceSize <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(instanceSize),
                    "The instance size must come from the slot's own PayloadSize; a non-positive size " +
                    "means the caller does not know how big its occurrence is.");

            Unsafe.InitBlock(instance, 0, (uint)instanceSize);

            ref InstanceHeader header = ref Unsafe.AsRef<InstanceHeader>(instance);
            header.MachineId  = definition.Header.StructureHash;
            header.Generation = 1;
            header.Phase      = InstancePhase.Entry;

            HsmKernelCore.ResetInstance(instance, instanceSize);
        }

        /// <summary>
        /// ⭐⭐ <b>Reset an instance at a POINTER of a known SIZE</b>, preserving <c>MachineId</c>,
        /// <c>RngState</c> and bumping <c>Generation</c> — the slot-resident form of <see cref="Reset{T}"/>.
        /// ⚠ Same sizing rule as <see cref="Initialize(byte*, int, HsmDefinitionBlob)"/>.
        /// </summary>
        public static unsafe void Reset(byte* instance, int instanceSize)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (instanceSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(instanceSize));

            ref InstanceHeader header = ref Unsafe.AsRef<InstanceHeader>(instance);

            uint   machineId  = header.MachineId;
            ushort generation = header.Generation;
            uint   rngState   = header.RngState;

            Unsafe.InitBlock(instance, 0, (uint)instanceSize);

            header.MachineId  = machineId;
            header.Generation = (ushort)(generation + 1);
            header.RngState   = rngState;
            header.Phase      = InstancePhase.Entry;

            HsmKernelCore.ResetInstance(instance, instanceSize);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-325</c> — THE SMALLEST INSTANCE TIER THAT CAN HOLD THIS MACHINE.</b>
        ///
        /// <para>⭐⭐ <b>The LAYOUT limits are no longer spelled here.</b> This method used to carry its
        /// own region/history numbers, and <see cref="HsmValidator.CheckTierBudget"/> carried a second
        /// set — <b>two tables of one fact, and they disagreed.</b> 📐 Measured: this method admitted
        /// <c>regions &lt;= 1 / &lt;= 2</c> and <c>history &lt;= 2 / &lt;= 4</c>, where the structs hold
        /// <c>2 / 4 / 8</c> regions and <c>2 / 8 / 16</c> history slots — so a 3- or 4-region machine
        /// fitted <c>HsmInstance128</c> exactly and was sent to 256 anyway. ⛔ And this method did not
        /// consider TIMER slots at all, which <c>CheckTierBudget</c> does.</para>
        ///
        /// <para>⇒ <b>the budget check is now the only producer of the layout limits</b>, consulted
        /// rather than duplicated. ⚠ <c>CheckTierBudget</c>'s numbers ARE the struct layouts — that is
        /// why it is the one kept.</para>
        ///
        /// <para>⚠ <b>ONE exception, and it is a POLICY rather than a layout fact:</b> tier 1 keeps an
        /// explicit <c>regions &lt;= 1</c>. The 64-byte layout holds two regions, but it is the only
        /// tier with <b>no reserved interrupt slot</b>, so an orthogonal machine must not land there.
        /// See the comment at the tier-1 branch.</para>
        ///
        /// <para>⭐ <b>What stays here is the COMPLEXITY heuristic</b>, and it is deliberately not a
        /// layout fact: <c>StateCount</c> and <c>MaxDepth</c> index nothing in the instance, so they
        /// cannot overflow it. They express <i>"a machine this big deserves a roomier event queue"</i>,
        /// which is a judgement rather than a constraint. ⛔ Removing them would drop a 30-state machine
        /// onto the 64-byte tier with a ONE-event queue.</para>
        ///
        /// <para>⛔⛔ <b>A machine that does not fit even 256 now THROWS instead of being handed 256.</b>
        /// 🔴 It used to fall through to <c>return 256</c> unchecked, and <c>InitializeMachine</c> writes
        /// <c>activeLeafIds[r]</c> for <c>r &lt; Header.RegionCount</c> <b>without a bounds check</b>
        /// (<c>HsmKernelCore:193</c>) — so a 9-region machine wrote past the leaf array. ⚠ History and
        /// timer over-indexing are both bounds-checked and merely lose data; <b>regions were the one
        /// genuine memory hazard</b>, and this closes it at the earliest point with a message naming the
        /// axis. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.24.</para>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// No tier can hold the machine — the message carries <c>CheckTierBudget</c>'s reason.
        /// </exception>
        public static int SelectTier(HsmDefinitionBlob definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            int stateCount = definition.States.Length;

            int maxDepth = 0;
            foreach (var state in definition.States)
                if (state.Depth > maxDepth) maxDepth = state.Depth;

            int regions = definition.Header.RegionCount;

            // Tier 1 (64B) — a small, shallow, SINGLE-REGION machine that fits the 64-byte layout.
            //
            // ⛔⛔ `regions <= 1` IS A POLICY, NOT A LAYOUT LIMIT, AND IT IS WHY THIS IS NOT JUST
            //   `CheckTierBudget(64)`. The 64-byte layout physically holds TWO regions and the budget
            //   check says so — but tier 1 is the one tier with NO RESERVED INTERRUPT SLOT
            //   (HsmInstance64: a single shared 24-byte event slot). ⇒ letting an orthogonal machine
            //   down onto it would take away the reserved slot that MobilityLost-class interrupts
            //   depend on (CE-324), which is a capability regression dressed as a saving.
            //   📐 Measured while making this change: routing tier 1 through the budget check alone
            //   dropped every 2-region machine from 128 to 64 and reddened 9 rails, CE-324's two
            //   included. 📄 §31.24.
            if (stateCount <= 8 && maxDepth <= 3 && regions <= 1
                && HsmValidator.CheckTierBudget(definition, 64, out _))
            {
                return 64;
            }

            // Tier 2 (128B) — the layout limits (4 regions, 4 timers, 8 history) come from the
            //   budget check alone; only the complexity heuristic is spelled here.
            if (stateCount <= 32 && maxDepth <= 6
                && HsmValidator.CheckTierBudget(definition, 128, out _))
            {
                return 128;
            }

            // Tier 3 (256B) — the largest there is, so its budget is a HARD requirement.
            if (!HsmValidator.CheckTierBudget(definition, 256, out string? error))
            {
                throw new ArgumentException(
                    "No HSM instance tier can hold this machine, so it cannot be given an instance " +
                    $"at all: {error}. The largest tier is 256 bytes (8 regions, 8 timer slots, " +
                    "16 history slots). Before CE-325 this returned 256 anyway and the kernel then " +
                    "wrote past the active-leaf array.",
                    nameof(definition));
            }

            return 256;
        }
    }
}
