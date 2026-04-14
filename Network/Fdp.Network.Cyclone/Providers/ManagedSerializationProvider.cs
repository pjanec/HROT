using System;
using System.IO;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using ModuleHost.Core.Abstractions;

namespace ModuleHost.Network.Cyclone.Providers
{
    public class ManagedSerializationProvider<T> : ISerializationProvider where T : class, new()
    {
        // ThreadLocal buffer to avoid allocations would be better, but simpler for now
        
        public ManagedSerializationProvider()
        {
        }

        public int GetSize(object descriptor)
        {
            if (descriptor is not T val) 
                throw new ArgumentException($"Expected type {typeof(T).Name}, got {descriptor?.GetType().Name}");

            // Serialize to temporary buffer to calculate size
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpAutoSerializer.Serialize(val, writer);
            writer.Flush();
            return (int)ms.Length;
        }

        public void Encode(object descriptor, Span<byte> buffer)
        {
            if (descriptor is not T val)
                throw new ArgumentException($"Expected type {typeof(T).Name}, got {descriptor?.GetType().Name}");
            
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            FdpAutoSerializer.Serialize(val, writer);
            writer.Flush();
            
            if (buffer.Length < ms.Length)
                 throw new ArgumentException("Buffer too small");
                 
            if (ms.TryGetBuffer(out ArraySegment<byte> segment))
            {
               segment.AsSpan().Slice(0, (int)ms.Length).CopyTo(buffer);
            }
            else
            {
               ms.ToArray().CopyTo(buffer);
            }
        }

        public void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd)
        {
            // Copying to array is necessary because MemoryStream requires array
            var bytes = buffer.ToArray();
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);
            
            var component = FdpAutoSerializer.Deserialize<T>(reader);
            cmd.SetManagedComponent(entity, component);
        }
    }
}
