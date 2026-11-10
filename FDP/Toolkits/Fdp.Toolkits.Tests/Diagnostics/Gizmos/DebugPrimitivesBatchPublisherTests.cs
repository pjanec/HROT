using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Interfaces;
using Xunit;

namespace Fdp.Toolkits.Tests.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — the gizmo batch is built (a fresh byte[] of every primitive) only when a reader is
    /// matched: with none, the publisher writes nothing and allocates nothing; with one, it publishes as before; a writer that cannot tell
    /// (no <see cref="IReaderAwareWriter"/>) still publishes.
    /// </summary>
    public class DebugPrimitivesBatchPublisherTests
    {
        private sealed class Writer : IDdsWriter<DebugPrimitivesBatch>, IReaderAwareWriter
        {
            public bool HasReaders { get; set; }
            public readonly List<DebugPrimitivesBatch> Written = new();
            public void Write(DebugPrimitivesBatch sample) => Written.Add(sample);
        }

        private sealed class PlainWriter : IDdsWriter<DebugPrimitivesBatch>
        {
            public int Count;
            public void Write(DebugPrimitivesBatch sample) => Count++;
        }

        private sealed class View : ISimulationView
        {
            public uint Tick => 7;
            public float Time => 0;
            public IEntityCommandBuffer GetCommandBuffer() => null!;
            public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
            public T GetManagedComponentRO<T>(Entity e) where T : class => throw new NotImplementedException();
            public bool IsAlive(Entity e) => true;
            public bool HasComponent<T>(Entity e) where T : unmanaged => false;
            public bool HasManagedComponent<T>(Entity e) where T : class => false;
            public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
            public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();
            public QueryBuilder Query() => throw new NotImplementedException();
        }

        private static DebugPrimitiveBuffer Filled()
        {
            var b = new DebugPrimitiveBuffer();
            for (int i = 0; i < 500; i++) b.DrawLine(new Vector3(i, 0, 0), new Vector3(i, 1, 0), Rgba32.White);
            return b;
        }

        [Fact]
        public void CE4366_NoReader_NoSample_NoGarbage()
        {
            var w = new Writer { HasReaders = false };
            var sys = new DebugPrimitivesBatchPublisherSystem(Filled(), w, 3);
            var view = new View();
            sys.Execute(view, 0.016f);   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) sys.Execute(view, 0.016f);
            long garbage = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Empty(w.Written);
            Assert.Equal(0, garbage);
        }

        [Fact]
        public void CE4366_AReader_GetsTheFrame()
        {
            var w = new Writer { HasReaders = true };
            var sys = new DebugPrimitivesBatchPublisherSystem(Filled(), w, 3);
            sys.Execute(new View(), 0.016f);
            var batch = Assert.Single(w.Written);
            Assert.Equal(7u, batch.FrameNumber);
            Assert.Equal((byte)3, batch.NodeId);
            Assert.NotEmpty(batch.PrimitivesData);
        }

        [Fact]
        public void CE4366_AWriterThatCannotTell_StillPublishes()
        {
            var w = new PlainWriter();
            new DebugPrimitivesBatchPublisherSystem(Filled(), w, 3).Execute(new View(), 0.016f);
            Assert.Equal(1, w.Count);
        }
    }
}
