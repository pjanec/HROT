using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// ⭐⭐ CE-3293 S4 (R-3036 rules 5, 6, 7, 9) — the event history behind the step windows and the background flush.
    /// 📄 docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §1, §1a, §7 (acceptance ⑤).
    /// </summary>
    public class EventHistoryTests
    {
        [EventId(932901)]
        public struct HistNative { public int Value; public long Pad; }

        public struct HistManagedStruct { public int Value; public string Tag; }

        public sealed class HistManagedClass { public int Value; }

        private static void Frame(FdpEventBus bus, EventAccumulator history, uint key, int value, int natives = 1, int manageds = 1)
        {
            for (int i = 0; i < natives; i++) bus.Publish(new HistNative { Value = value });
            for (int i = 0; i < manageds; i++) bus.PublishManaged(new HistManagedStruct { Value = value, Tag = "t" });
            bus.SwapBuffers();
            history.CaptureFrame(bus, key);
        }

        private static void EmptyFrame(FdpEventBus bus, EventAccumulator history, uint key)
        {
            bus.SwapBuffers();
            history.CaptureFrame(bus, key);
        }

        [Fact]
        public void AStepWindow_DeliversEveryPausedFramesEvents_InOrder_Once()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator();
            Frame(bus, history, 1, 1);
            int reader = history.RegisterReaderAtCurrentFrame();
            Assert.True(history.WindowIsCurrentFrame(reader));
            history.MarkRead(reader);

            for (uint k = 2; k <= 6; k++) Frame(bus, history, k, (int)k);   // five frames the reader does not read

            Assert.False(history.WindowIsCurrentFrame(reader));
            var natives = history.ReadWindow<HistNative>(reader);
            Assert.Equal(new[] { 2, 3, 4, 5, 6 }, natives.ToArray().Select(e => e.Value));
            var manageds = history.ReadManagedWindow<HistManagedStruct>(reader);
            Assert.Equal(new[] { 2, 3, 4, 5, 6 }, manageds.Select(e => e.Value));

            history.MarkRead(reader);
            Frame(bus, history, 7, 7);
            Assert.True(history.WindowIsCurrentFrame(reader));   // only the current frame: the bus itself answers
        }

        [Fact]
        public void TheRepositorysRead_IsTheWindow_OnlyWhileItIsOpen()
        {
            using var world = new EntityRepository();
            world.RegisterEvent<HistNative>();
            var history = new EventAccumulator();
            world.AttachEventHistory(history);
            Frame(world.Bus, history, 1, 1);
            int reader = history.RegisterReaderAtCurrentFrame();
            history.MarkRead(reader);
            Frame(world.Bus, history, 2, 2);
            Frame(world.Bus, history, 3, 3);

            Assert.Equal(new[] { 3 }, world.ReadEvents<HistNative>().ToArray().Select(e => e.Value));   // no window: the frame
            int previous = world.OpenStepWindow(reader);
            Assert.Equal(new[] { 2, 3 }, world.ReadEvents<HistNative>().ToArray().Select(e => e.Value));
            Assert.True(world.HasEventsByTypeId(EventType<HistNative>.Id));
            Assert.True(world.HasEvents(typeof(HistNative)));
            Assert.Equal(2, world.ReadManagedEvents<HistManagedStruct>().Count);
            world.CloseStepWindow(previous);
            Assert.Equal(new[] { 3 }, world.ReadEvents<HistNative>().ToArray().Select(e => e.Value));
        }

        [Fact]
        public void AnEmptyFrame_TakesNoSlot()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator(maxHistoryFrames: 8);
            history.RegisterReader();
            for (uint k = 1; k <= 1000; k++) EmptyFrame(bus, history, k);
            Assert.Equal(0, history.SlotsInUse);
        }

        [Fact]
        public void PausedFrames_ShareASlot_WhileNoReaderReadsInside()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator(maxHistoryFrames: 8);
            int reader = history.RegisterReader();
            Frame(bus, history, 1, 1);
            history.MarkRead(reader);
            for (uint k = 2; k <= 500; k++) Frame(bus, history, k, (int)k);   // a long pause with events every frame

            Assert.True(history.SlotsInUse <= 2, $"{history.SlotsInUse} slots for one pause");
            Assert.Equal(499, history.ReadWindow<HistNative>(reader).Length);
            Assert.Equal(0, history.Overflow);
        }

        [Fact]
        public void ASlot_IsKeptUntilItsLastReaderHasHadIt()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator(maxHistoryFrames: 64);
            int fast = history.RegisterReader();
            int slow = history.RegisterReader();
            for (uint k = 1; k <= 10; k++)
            {
                Frame(bus, history, k, (int)k);
                history.MarkRead(fast);
            }
            using var replica = new FdpEventBus();
            history.FlushUnreadTo(replica, slow);
            Assert.Equal(10, replica.Read<HistNative>().Length);   // nothing released under the slow reader

            Frame(bus, history, 11, 11);
            history.MarkRead(fast);
            Assert.True(history.SlotsInUse <= 1);   // both have had 1..10: freed
        }

        [Fact]
        public void TheCap_CountsWhatAReaderLost_AndExpiresIt()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator(maxHistoryFrames: 4);
            int stuck = history.RegisterReader();
            int live  = history.RegisterReader();
            for (uint k = 1; k <= 10; k++)
            {
                Frame(bus, history, k, (int)k, natives: 1, manageds: 0);
                history.MarkRead(live);   // a live reader stops the merging: one slot per frame
            }
            Assert.True(history.Overflow > 0);
            Assert.Equal(1, history.ExpiredReaders);
            Assert.True(history.SlotsInUse <= 1);   // the expired reader no longer pins anything: the live one frees the rest
        }

        [Fact]
        public void ARestart_ForgetsEverythingBeforeIt()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator();
            int reader = history.RegisterReader();
            Frame(bus, history, 1, 1);
            Frame(bus, history, 2, 2);
            history.Restart();                     // SnapAndPause
            Frame(bus, history, 3, 3);
            Frame(bus, history, 4, 4);
            Assert.Equal(new[] { 3, 4 }, history.ReadWindow<HistNative>(reader).ToArray().Select(e => e.Value));
        }

        [Fact]
        public void KeysThatGoBack_StartANewTimeline()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator();
            int reader = history.RegisterReader();
            Frame(bus, history, 10, 10);
            Frame(bus, history, 11, 11);
            Frame(bus, history, 1, 1);   // a load numbered from scratch
            using var replica = new FdpEventBus();
            history.FlushUnreadTo(replica, reader);
            Assert.Equal(new[] { 1 }, replica.Read<HistNative>().ToArray().Select(e => e.Value));
        }

        [Fact]
        public void AFlush_InjectsManagedClassesAndStructs_Typed()
        {
            using var bus = new FdpEventBus();
            var history = new EventAccumulator();
            bus.PublishManaged(new HistManagedClass { Value = 5 });
            Frame(bus, history, 1, 1);
            using var replica = new FdpEventBus();
            history.FlushToReplica(replica, 0u);
            Assert.Equal(5, Assert.Single(replica.ReadManaged<HistManagedClass>()).Value);
            Assert.Equal(1, Assert.Single(replica.ReadManaged<HistManagedStruct>()).Value);
            Assert.True(replica.HasManagedEvent<HistManagedStruct>());
        }

        /// <summary>
        /// ⭐⭐⭐ Acceptance ⑤ (R-3036 rule 9) — after a warm-up, 1 000 frames of capture + a 30-frame step window read + a flush to a
        /// replica, with native and managed-STRUCT events every frame, allocate NOTHING. 🔴 Red before S4: the old accumulator made
        /// two lists, walked <c>ConcurrentDictionary.Values</c> and boxed every managed struct each frame.
        /// </summary>
        [Fact]
        public void Capture_WindowRead_AndFlush_AllocateNothing_InSteadyState()
        {
            using var bus = new FdpEventBus();
            using var replica = new FdpEventBus();
            var history = new EventAccumulator();
            int stepReader = history.RegisterReaderAtCurrentFrame();
            int moduleReader = history.RegisterReader();
            long sink = 0;

            void Run(uint from, uint to)
            {
                for (uint k = from; k < to; k++)
                {
                    Frame(bus, history, k, (int)k, natives: 3, manageds: 2);
                    if (k % 30 == 0)
                    {
                        var natives = history.ReadWindow<HistNative>(stepReader);
                        var manageds = history.ReadManagedWindow<HistManagedStruct>(stepReader);
                        sink += natives.Length + manageds.Count;
                        history.MarkRead(stepReader);
                    }
                    if (k % 10 == 0)
                    {
                        replica.ClearAll();
                        history.FlushUnreadTo(replica, moduleReader);
                        sink += replica.Read<HistNative>().Length;
                    }
                }
            }

            Run(1, 400);   // warm-up: every buffer reaches its high-water mark
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(400, 1400);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(sink > 0);
            Assert.True(allocated == 0, $"{allocated} bytes allocated over 1 000 frames");
        }
    }
}
