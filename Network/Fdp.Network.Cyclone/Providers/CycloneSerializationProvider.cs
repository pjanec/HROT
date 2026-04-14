using System;
using System.Runtime.CompilerServices; // For Unsafe
using System.Runtime.InteropServices;
using Fdp.Interfaces;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace ModuleHost.Network.Cyclone.Providers
{
    public class CycloneSerializationProvider<T> : ISerializationProvider where T : unmanaged
    {
        public int GetSize(object descriptor)
        {
            return Unsafe.SizeOf<T>();
        }

        public void Encode(object descriptor, Span<byte> buffer)
        {
            if (descriptor is T val)
            {
                // Unsafe copy to span
                // buffer is Span<byte>, we need to cast to ref T location or write bytes
                if (buffer.Length < Unsafe.SizeOf<T>())
                     throw new ArgumentException("Buffer too small");

                Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), val);
            }
            else
            {
                throw new ArgumentException($"Expected type {typeof(T).Name}, got {descriptor?.GetType().Name}");
            }
        }

        public void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd)
        {
            if (buffer.Length < Unsafe.SizeOf<T>())
                throw new ArgumentException("Buffer too small");
                
            var val = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));
            cmd.SetComponent(entity, val); 
        }
    }
}
