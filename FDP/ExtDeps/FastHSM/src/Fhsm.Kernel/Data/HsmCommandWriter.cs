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

        /// <summary>
        /// Create writer for a command page.
        /// </summary>
        public unsafe HsmCommandWriter(CommandPage* page, int capacity = 4080, CommandLane lane = CommandLane.Gameplay)
        {
            _currentPage = page;
            _bytesWritten = 0;
            _capacity = capacity;
            _currentLane = lane;
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
