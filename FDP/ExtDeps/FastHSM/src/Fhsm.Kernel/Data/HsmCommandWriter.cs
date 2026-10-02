using System;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Zero-allocation command writer (ref struct).
    /// Uses stack-only semantics to emit commands to paged buffers.
    /// CANNOT be stored in fields or returned - stack lifetime only.
    /// </summary>
    public ref struct HsmCommandWriter
    {
        private unsafe CommandPage* _currentPage;
        private int _bytesWritten;
        private readonly int _capacity;
        private CommandLane _currentLane;

        // ── O6 — THE OCCURRENCE STAMP ─────────────────────────────────────────────────
        //  The kernel supplies IDENTITY; the thunk does the LOOKUP.
        //  DESIGN_Occurrence_Scoped_Storage.md §4.2, ruling Q35-A/Q35-B.
        //
        //  Why HERE and not on the context/bridge: the writer is PER-DISPATCH, the bridge is
        //  PER-ENTITY-TICK — and the occurrence changes between two actions inside one tick, so
        //  only the writer can carry it. (Q35 option C was rejected for exactly this.)
        //
        //  Why a PAIR and not a pre-hashed key: both halves are already in scope at the kernel's
        //  dispatch sites, and the key algorithm stays in ONE home outside ExtDeps
        //  (ComputeStatefulSlotKey). The kernel must not learn about the partition allocator.
        private int _occurrenceRegionSlotIndex;
        private ushort _occurrenceStateId;

        /// <summary>Sentinel region for "the kernel has not stamped a dispatch".</summary>
        public const int NoRegionSlot = -1;

        /// <summary>Sentinel state for "the kernel has not stamped a dispatch" — the kernel's own
        /// "no active leaf" value, so it reads the same as everywhere else in the instance.</summary>
        public const ushort NoStateId = 0xFFFF;

        /// <summary>
        /// Create writer for a command page.
        /// </summary>
        public unsafe HsmCommandWriter(CommandPage* page, int capacity = 4080, CommandLane lane = CommandLane.Gameplay)
        {
            _currentPage = page;
            _bytesWritten = 0;
            _capacity = capacity;
            _currentLane = lane;
            _occurrenceRegionSlotIndex = NoRegionSlot;
            _occurrenceStateId = NoStateId;
        }

        /// <summary>
        /// O6 — the region slot the action or guard now being dispatched belongs to, or
        /// <see cref="NoRegionSlot"/> outside a dispatch.
        /// </summary>
        public int OccurrenceRegionSlotIndex => _occurrenceRegionSlotIndex;

        /// <summary>
        /// O6 — the state the action or guard now being dispatched is declared on, or
        /// <see cref="NoStateId"/> outside a dispatch.
        /// </summary>
        public ushort OccurrenceStateId => _occurrenceStateId;

        /// <summary>
        /// O6 — stamp the occurrence for the dispatch about to happen.
        /// <para>
        /// Deliberately <c>internal</c>: the kernel is the only thing entitled to say which
        /// occurrence is running. A thunk READS the pair and looks its own storage up; it must not
        /// be able to forge one, because a forged identity is a silent cross-occurrence alias —
        /// the exact failure the occurrence model exists to remove.
        /// </para>
        /// </summary>
        internal void StampOccurrence(int regionSlotIndex, ushort stateId)
        {
            _occurrenceRegionSlotIndex = regionSlotIndex;
            _occurrenceStateId = stateId;
        }

        public void SetLane(CommandLane lane)
        {
            _currentLane = lane;
        }
        
        public CommandLane CurrentLane => _currentLane;

        /// <summary>
        /// Bytes written to current page.
        /// </summary>
        public int BytesWritten => _bytesWritten;

        /// <summary>
        /// Remaining capacity in current page.
        /// </summary>
        public int RemainingCapacity => _capacity - _bytesWritten;

        /// <summary>
        /// Try to write a command. Returns false if insufficient space.
        /// </summary>
        public unsafe bool TryWriteCommand(ReadOnlySpan<byte> command)
        {
            if (command.Length > RemainingCapacity)
                return false;

            // Write command bytes
            fixed (byte* src = command)
            {
                for (int i = 0; i < command.Length; i++)
                {
                    _currentPage->Data[_bytesWritten + i] = src[i];
                }
            }

            _bytesWritten += command.Length;
            _currentPage->BytesUsed = (ushort)_bytesWritten;
            return true;
        }

        /// <summary>
        /// Reset writer to beginning of page.
        /// </summary>
        public unsafe void Reset()
        {
            _bytesWritten = 0;
            if (_currentPage != null)
                _currentPage->BytesUsed = 0;
        }
    }
}
