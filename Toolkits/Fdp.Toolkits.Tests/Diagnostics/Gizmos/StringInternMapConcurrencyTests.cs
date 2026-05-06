using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Tests
{
    // ==========================================================================
    // SC-GZ040: StringInternMap concurrency tests
    // ==========================================================================

    public class StringInternMapConcurrencyTests
    {
        // SC-GZ040-2: 32 threads racing to Intern the same hash → no exception,
        // exactly 1 entry in the map.
        [Fact]
        public void SC_GZ040_2_ParallelIntern_SameHash_ProducesExactlyOneEntry()
        {
            var map  = new StringInternMap();
            const string text = "a string longer than 31 characters, used for long-text intern test";
            uint hash = StringInternMap.Fnv1a32(text);

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            Parallel.For(0, 32, _ =>
            {
                try
                {
                    map.Intern(hash, text);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
            Assert.Equal(1, map.Entries.Count);
            Assert.Equal(text, map.Entries[hash]);
        }

        // SC-GZ040-3: Concurrent Intern + TryResolve stress → no exception and
        // TryResolve never returns a torn value (only null or the correct string).
        [Fact]
        public void SC_GZ040_3_ConcurrentReadWrite_NoException_NoTornValue()
        {
            var map  = new StringInternMap();
            const string text = "another string longer than thirty-one characters for stress testing";
            uint hash = StringInternMap.Fnv1a32(text);

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Writers: intern the same hash from 8 threads.
            var writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                        map.Intern(hash, text);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));

            // Readers: resolve the hash from 8 threads; the result must be null or the correct string.
            var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        string? resolved = map.TryResolve(hash);
                        if (resolved != null && resolved != text)
                            exceptions.Add(new InvalidOperationException($"Torn value: '{resolved}'"));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));

            Task.WaitAll(writers.Concat(readers).ToArray());

            Assert.Empty(exceptions);
        }

        // SC-GZ040-5: DrawTextLong called from 16 parallel threads × 625 iterations
        // (10 000 total) → no exception; intern map has exactly 1 entry.
        [Fact]
        public void SC_GZ040_5_DrawTextLong_ParallelStress_NoException()
        {
            var internMap = new StringInternMap();
            var buf       = new DebugPrimitiveBuffer(capacity: 65536, internMap: internMap);

            const string longText = "a string that is definitely longer than thirty-one characters for DrawTextLong stress";
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            Parallel.For(0, 16, _ =>
            {
                try
                {
                    for (int i = 0; i < 625; i++)
                        buf.DrawTextLong(0f, 0f, longText, Rgba32.White);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
            // All 16 threads intern the same string → exactly one entry.
            Assert.Equal(1, internMap.Entries.Count);
        }
    }
}
