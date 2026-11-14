using System;
using System.Linq;
using Fdp.Core;
using Xunit;
using A = Fdp.Tests.RecorderDeltaLogicTests.Position;
using B = Fdp.Tests.RecorderDeltaLogicTests.Velocity;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>A-1</c> (CE-4436, the threaded-simulation analysis instruments) — the access tracer (<c>HROT_ACCESS_TRACE=1</c>, <see cref="AccessTrace"/>) is an
    /// instrument: with the switch OFF it must cost nothing. 📐 Every hook is <c>if (AccessTrace.On)</c> on a <c>static readonly bool</c>; the rail below runs
    /// a steady frame through every hooked world path — component read / RW / set / has / TryGet, singleton get / set / has (unmanaged and managed), a managed
    /// component, a query walk, the event bus publish / read / has, and a reader scope — and asserts 0 B allocated (R-2084).
    /// 🔴 Red-proof: an unconditional <c>AccessTrace.Scope</c> name concatenation in <c>Scope()</c> (the <c>On</c> guard removed) allocates a string per call ⇒ this fails.
    /// </summary>
    public class AccessTraceOffTests
    {
        [ComponentId(497)] public sealed record TraceManaged(int V);
        [EventId(9971)] public struct TraceEvent { public int V; }
        [ComponentId(499)] public struct TraceSingleton { public int V; }
        [ComponentId(498)] public sealed class TraceManagedSingleton { public int V; }

        private static bool SwitchOn => Environment.GetEnvironmentVariable("HROT_ACCESS_TRACE") == "1";

        [Fact]
        public void TheTracerIsOffByDefault()
        {
            if (SwitchOn) return;   // someone is tracing the suite on purpose
            Assert.False(AccessTrace.On);
        }

        /// <summary>Runs ONLY with <c>HROT_ACCESS_TRACE=1</c> (the switch is read once per process): counts land under the scope's context and reader, flow into a Parallel.For worker, and the JSON names them.</summary>
        [Fact]
        public void When_the_switch_is_on_accesses_are_counted_under_their_context_and_reader_even_in_a_parallel_worker()
        {
            if (!SwitchOn) return;
            var outFile = Environment.GetEnvironmentVariable("HROT_ACCESS_TRACE_OUT");
            Assert.False(string.IsNullOrEmpty(outFile));
            using var w = new EntityRepository();
            w.RegisterComponent<A>(); w.RegisterComponent<B>();
            var e = w.CreateEntity();
            w.AddComponent(e, new A { X = 1 }); w.AddComponent(e, new B { VX = 1 });
            using (AccessTrace.Scope("rail-reader", TraceContext.Tick))
            {
                _ = w.GetComponentRO<A>(e).X;
                w.GetComponentRW<A>(e).X = 2;
                System.Threading.Tasks.Parallel.For(0, 4, i => { float vx = w.GetComponentRO<B>(e).VX; GC.KeepAlive(vx.ToString()); });   // a worker thread inherits the reader
            }
            AccessTrace.Dump();
            var json = System.IO.File.ReadAllText(outFile!);
            Assert.Contains("\"rail-reader\"", json);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var readers = doc.RootElement.GetProperty("readers").EnumerateArray().Select(x => x.GetString()).ToList();
            int rid = readers.IndexOf("rail-reader");
            var kinds = doc.RootElement.GetProperty("kinds").EnumerateArray().Select(x => x.GetString()).ToList();
            long CountOf(string kind, string typeSuffix) => doc.RootElement.GetProperty("rows").EnumerateArray()
                .Where(r => r[0].GetInt32() == (int)TraceContext.Tick && r[2].GetInt32() == rid && kinds[r[1].GetInt32()] == kind && r[5].GetString()!.EndsWith(typeSuffix))
                .Sum(r => r[6].GetInt64());
            Assert.Equal(1, CountOf("Read", "Position"));   // the read of A (Position)
            Assert.Equal(1, CountOf("Write", "Position"));
            Assert.Equal(4, CountOf("Read", "Velocity"));   // the four parallel reads, under the same reader
        }

        [Fact]
        public void A_steady_frame_through_every_hooked_path_allocates_nothing_when_the_switch_is_off()
        {
            if (SwitchOn) return;   // ON allocates by design
            using var w = new EntityRepository();
            w.RegisterComponent<A>(); w.RegisterComponent<B>(); w.RegisterManagedComponent<TraceManaged>();
            w.RegisterComponent<TraceSingleton>(); w.RegisterManagedComponent<TraceManagedSingleton>();
            w.RegisterEvent<TraceEvent>();
            var e = w.CreateEntity();
            w.AddComponent(e, new A { X = 1 }); w.AddComponent(e, new B { VX = 2 });
            var managed = new TraceManaged(3);
            w.SetManagedComponent(e, managed);
            w.SetSingletonUnmanaged(new TraceSingleton { V = 1 });
            w.SetSingletonManaged(new TraceManagedSingleton { V = 1 });
            var q = w.Query().With<A>().With<B>().Build();
            object reader = managed;

            double Frame()
            {
                double sum = 0;
                for (int i = 0; i < 50; i++)
                {
                    using (AccessTrace.Scope("rail: a reader", TraceContext.Tick))
                    using (AccessTrace.Scope(reader, "rail: "))
                    {
                        sum += w.GetComponentRO<A>(e).X;
                        w.GetComponentRW<B>(e).VX = i;
                        if (w.HasComponent<A>(e) && w.TryGetComponent<B>(e, out var b)) sum += b.VX;
                        w.SetComponent(e, new A { X = i });
                        sum += w.GetSingletonUnmanaged<TraceSingleton>().V;
                        w.SetSingletonUnmanaged(new TraceSingleton { V = i });
                        if (w.HasSingletonManaged<TraceManagedSingleton>()) sum += w.GetSingletonManaged<TraceManagedSingleton>()!.V;
                        sum += w.GetManagedComponent<TraceManaged>(e).V;
                        w.SetManagedComponent(e, managed);
                        foreach (var x in q) sum += x.Index;
                        sum += q.Count();
                        w.Bus.Publish(new TraceEvent { V = i });
                        sum += w.Bus.Read<TraceEvent>().Length;
                        if (w.Bus.HasEvent<TraceEvent>()) sum++;
                    }
                }
                return sum;
            }

            for (int i = 0; i < 3; i++) Frame();   // first-use work (JIT, stream creation) outside the window
            long before = GC.GetAllocatedBytesForCurrentThread();
            double s = Frame();
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            GC.KeepAlive(s);
        }
    }
}
