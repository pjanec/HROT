using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// Provides zero-overhead access to EntityId and InstanceId fields for multi-instance descriptors.
    /// </summary>
    public static class MultiInstanceLayout<T> where T : unmanaged
    {
        public static readonly int EntityIdOffset;
        public static readonly int InstanceIdOffset;
        public static readonly bool IsValid;
        public static readonly bool IsEntityId32Bit;
        public static readonly bool IsInstanceId32Bit;

        static MultiInstanceLayout()
        {
            var fEntity   = typeof(T).GetField("EntityId",   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fInstance = typeof(T).GetField("InstanceId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (fEntity != null && fInstance != null &&
                (fEntity.FieldType   == typeof(long) || fEntity.FieldType   == typeof(ulong)
                                                     || fEntity.FieldType   == typeof(int)  || fEntity.FieldType   == typeof(uint)) &&
                (fInstance.FieldType == typeof(long) || fInstance.FieldType == typeof(int)))
            {
                EntityIdOffset   = (int)Marshal.OffsetOf<T>("EntityId");
                InstanceIdOffset = (int)Marshal.OffsetOf<T>("InstanceId");
                IsValid          = true;
                IsEntityId32Bit  = (fEntity.FieldType   == typeof(int) || fEntity.FieldType   == typeof(uint));
                IsInstanceId32Bit = fInstance.FieldType == typeof(int);
            }
            else
            {
                EntityIdOffset   = -1;
                InstanceIdOffset = -1;
                IsValid           = false;
                IsEntityId32Bit   = false;
                IsInstanceId32Bit = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long ReadEntityId(T* ptr)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsEntityId32Bit)
                return *(int*)(bytePtr + EntityIdOffset);
            return *(long*)(bytePtr + EntityIdOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long ReadInstanceId(T* ptr)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsInstanceId32Bit)
            {
                return *(int*)(bytePtr + InstanceIdOffset);
            }
            else
            {
                return *(long*)(bytePtr + InstanceIdOffset);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteEntityId(T* ptr, long id)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsEntityId32Bit)
                *(int*)(bytePtr + EntityIdOffset) = (int)id;
            else
                *(long*)(bytePtr + EntityIdOffset) = id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteInstanceId(T* ptr, long instanceId)
        {
            byte* bytePtr = (byte*)ptr;
            if (IsInstanceId32Bit)
            {
                *(int*)(bytePtr + InstanceIdOffset) = (int)instanceId;
            }
            else
            {
                *(long*)(bytePtr + InstanceIdOffset) = instanceId;
            }
        }
    }
}
