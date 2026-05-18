using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// Provides zero-overhead access to EntityId field via unsafe pointer arithmetic.
    /// Type initializer runs once per generic type instantiation.
    /// </summary>
    /// <typeparam name="T">Struct type containing EntityId field</typeparam>
    public static class UnsafeLayout<T> where T : unmanaged
    {
        /// <summary>
        /// Byte offset from start of struct to EntityId field. -1 if field not found.
        /// </summary>
        public static readonly int EntityIdOffset;
        
        /// <summary>
        /// True if type has valid EntityId field (long, ulong, int, or uint).
        /// </summary>
        public static readonly bool IsValid;

        /// <summary>
        /// True if EntityId field is 32-bit (int or uint). False if 64-bit (long or ulong).
        /// </summary>
        public static readonly bool IsEntityId32Bit;

        static UnsafeLayout()
        {
            // One-time reflection at type initialization
            var field = typeof(T).GetField("EntityId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (field != null && (field.FieldType == typeof(long)  || field.FieldType == typeof(ulong)
                               || field.FieldType == typeof(int)   || field.FieldType == typeof(uint)))
            {
                EntityIdOffset   = (int)Marshal.OffsetOf<T>("EntityId");
                IsValid          = true;
                IsEntityId32Bit  = (field.FieldType == typeof(int) || field.FieldType == typeof(uint));
            }
            else
            {
                EntityIdOffset  = -1;
                IsValid         = false;
                IsEntityId32Bit = false;
            }
        }

        /// <summary>
        /// Reads EntityId from struct via pointer arithmetic (Zero overhead).
        /// Handles both 32-bit (int/uint) and 64-bit (long/ulong) EntityId fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long ReadId(T* ptr)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsEntityId32Bit)
                return *(int*)(bytePtr + EntityIdOffset);
            return *(long*)(bytePtr + EntityIdOffset);
        }

        /// <summary>
        /// Writes EntityId to struct via pointer arithmetic (Zero overhead).
        /// Handles both 32-bit (int/uint) and 64-bit (long/ulong) EntityId fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteId(T* ptr, long id)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsEntityId32Bit)
                *(int*)(bytePtr + EntityIdOffset) = (int)id;
            else
                *(long*)(bytePtr + EntityIdOffset) = id;
        }
    }
}
