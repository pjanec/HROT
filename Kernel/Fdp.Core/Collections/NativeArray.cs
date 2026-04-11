using System;
using System.Runtime.InteropServices;

namespace Fdp.Kernel.Collections
{
    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        Persistent = 4
    }

    public unsafe struct NativeArray<T> : IDisposable where T : unmanaged
    {
        private void* m_Buffer;
        private int m_Length;
        private Allocator m_AllocatorLabel;

        public bool IsCreated => m_Buffer != null;
        public int Length => m_Length;

        public NativeArray(int length, Allocator allocator)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            
            long size = (long)length * Marshal.SizeOf<T>();
            if (size == 0)
            {
                m_Buffer = null;
                m_Length = 0;
                m_AllocatorLabel = Allocator.Invalid;
                return;
            }

            m_Buffer = (void*)Marshal.AllocHGlobal((nint)size);
            m_Length = length;
            m_AllocatorLabel = allocator;
        }

        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= m_Length) throw new IndexOutOfRangeException($"Index {index} out of range (Length: {m_Length})");
                return ref ((T*)m_Buffer)[index];
            }
        }

        public void Dispose()
        {
            if (m_Buffer != null)
            {
                Marshal.FreeHGlobal((nint)m_Buffer);
                m_Buffer = null;
                m_Length = 0;
                m_AllocatorLabel = Allocator.Invalid;
            }
        }
    }
}
