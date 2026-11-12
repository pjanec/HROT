using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkits.Tests.Utility
{
    /// <summary>
    /// POC 2 / P-2 (A-2 findings) — two unsafe lazy statics, raced by many threads released together. Each round resets the static by reflection.
    /// ⚠ These mutate process-wide statics, so they share one xunit collection with each other.
    /// </summary>
    [Collection("UtilityStaticsRace")]
    public class UtilityStaticsRaceTests
    {
        private const int Rounds = 200;
        private const int Threads = 8;

        private static void RaceAll(Action body)
        {
            using var gate = new Barrier(Threads);
            var tasks = new Task[Threads];
            for (int t = 0; t < Threads; t++)
                tasks[t] = Task.Factory.StartNew(() => { gate.SignalAndWait(); body(); }, TaskCreationOptions.LongRunning);
            Task.WaitAll(tasks);
        }

        [Fact]
        public void UtilityDecisionCatalog_Shared_IsOneInstance_UnderRace()
        {
            var field = typeof(UtilityDecisionCatalog).GetField("_shared", BindingFlags.NonPublic | BindingFlags.Static)!;
            var saved = field.GetValue(null);
            try
            {
                for (int round = 0; round < Rounds; round++)
                {
                    field.SetValue(null, null);
                    var seen = new UtilityRegistry?[Threads];
                    int slot = -1;
                    RaceAll(() => { int i = Interlocked.Increment(ref slot); seen[i] = UtilityDecisionCatalog.Shared; });
                    for (int i = 1; i < Threads; i++)
                        Assert.Same(seen[0], seen[i]);
                    Assert.Same(seen[0], UtilityDecisionCatalog.Shared);
                }
            }
            finally { field.SetValue(null, saved); }
        }

        [Fact]
        public void UtilityInputNames_BuildsTheTableOnce_AndEveryThreadSeesTheSameNames_UnderRace()
        {
            var field = typeof(UtilityInputNames).GetField("_names", BindingFlags.NonPublic | BindingFlags.Static)!;
            var saved = field.GetValue(null);
            // any id the tables hold, so a half-built table would show as "0x…" instead of a name
            ushort known = (ushort)typeof(StandardInputIds).GetFields(BindingFlags.Public | BindingFlags.Static)[0].GetRawConstantValue()!;
            try
            {
                for (int round = 0; round < Rounds; round++)
                {
                    field.SetValue(null, UtilityInputNames.NewTable());
                    int builds0 = UtilityInputNames.BuildCount;
                    var names = new string?[Threads];
                    int slot = -1;
                    RaceAll(() => { int i = Interlocked.Increment(ref slot); names[i] = UtilityInputNames.Of(known); });
                    Assert.Equal(1, UtilityInputNames.BuildCount - builds0);
                    for (int i = 0; i < Threads; i++)
                    {
                        Assert.NotNull(names[i]);
                        Assert.DoesNotContain("0x", names[i]!);
                        Assert.Equal(names[0], names[i]);
                    }
                }
            }
            finally { field.SetValue(null, saved); }
        }
    }
}
