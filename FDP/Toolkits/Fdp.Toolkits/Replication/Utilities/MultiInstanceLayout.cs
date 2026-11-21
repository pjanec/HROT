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
        public static readonly bool IsInstanceId32Bit;

        static MultiInstanceLayout()
        {
            var fEntity   = typeof(T).GetField("EntityId",   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var fInstance = typeof(T).GetField("InstanceId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // ⭐ CE-4200 / CE-4209: a network entity id is 32-bit. A 64-bit entity key on a multi-instance descriptor is a defect — fail LOUDLY at
            //   first use (the CLR wraps this in a TypeInitializationException), never read it narrowed. Measured 2026-11-02: no DDS type has one.
            if (fEntity != null && fInstance != null && (fEntity.FieldType == typeof(long) || fEntity.FieldType == typeof(ulong)))
                throw new InvalidOperationException(
                    $"{typeof(T).FullName}.EntityId is {fEntity.FieldType.Name}: a network entity id is 32-bit (int / uint) — a multi-instance descriptor may not carry a 64-bit entity key.");

            if (fEntity != null && fInstance != null &&
                (fEntity.FieldType   == typeof(int)  || fEntity.FieldType   == typeof(uint)) &&
                (fInstance.FieldType == typeof(long) || fInstance.FieldType == typeof(int)))
            {
                EntityIdOffset   = Fdp.Core.TypeLayout.OffsetOf(typeof(T), "EntityId"); // ⭐ CE-2043 — the MANAGED offset (read through a pointer)
                InstanceIdOffset = Fdp.Core.TypeLayout.OffsetOf(typeof(T), "InstanceId");
                IsValid          = true;
                IsInstanceId32Bit = fInstance.FieldType == typeof(int);
            }
            else
            {
                EntityIdOffset   = -1;
                InstanceIdOffset = -1;
                IsValid           = false;
                IsInstanceId32Bit = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int ReadEntityId(T* ptr)
        {
            byte* bytePtr = (byte*)ptr;
            return *(int*)(bytePtr + EntityIdOffset);   // the static ctor guarantees a 32-bit key
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
        public static unsafe void WriteEntityId(T* ptr, int id)
        {
            byte* bytePtr = (byte*)ptr;
            *(int*)(bytePtr + EntityIdOffset) = id;   // the static ctor guarantees a 32-bit key
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
