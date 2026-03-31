using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using FDP.Toolkit.Replication.Patching;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the P3T1 core: <see cref="BinaryInterpreterBuilder"/> and
    /// <see cref="BinaryInterpreter"/>.  Tests use <see cref="ListPatchContext"/> for
    /// assertions and plain lambda installers to avoid domain-layer dependencies.
    /// </summary>
    public class BinaryInterpreterTests
    {
        // ── Helper: build a minimal ListPatchContext (no seed) ───────────────────

        private static ListPatchContext EmptyCtx() => new ListPatchContext(null);

        private static AttributeRecord Float64Record(ushort id, double val) =>
            new AttributeRecord
            {
                AttributeId = id,
                Value       = new AttributeValueUnion { ValueType = AttributeValueType.KindFloat64, DoubleValue = val }
            };

        private static AttributeRecord Int32Record(ushort id, int val) =>
            new AttributeRecord
            {
                AttributeId = id,
                Value       = new AttributeValueUnion { ValueType = AttributeValueType.KindInt32, IntValue = val }
            };

        // ── Test 1: Basic dispatch ────────────────────────────────────────────────

        [Fact]
        public void Apply_KnownId_HandlerInvoked()
        {
            double received = 0;
            var interpreter = new BinaryInterpreterBuilder()
                .RegisterHandler(1, (ctx, rec) => received = rec.Value.DoubleValue)
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            interpreter.Apply(ctx, new[] { Float64Record(1, 3.14) });

            Assert.Equal(3.14, received);
        }

        // ── Test 2: Unknown id silently ignored ───────────────────────────────────

        [Fact]
        public void Apply_UnknownId_NoException()
        {
            int callCount = 0;
            var interpreter = new BinaryInterpreterBuilder()
                .RegisterHandler(1, (ctx, rec) => callCount++)
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            interpreter.Apply(ctx, new[] { Float64Record(99, 0) });

            Assert.Equal(0, callCount);
        }

        // ── Test 3: Flusher called after dirty bit set ────────────────────────────

        [Fact]
        public void Apply_FlusherBitSet_FlusherCalledOnce()
        {
            int flusherCallCount = 0;
            var interpreter = new BinaryInterpreterBuilder()
                .RegisterHandler(1, (ctx, rec) => ctx.MarkSubsystemDirty(0))
                .RegisterSubsystemFlusher(0, ctx => flusherCallCount++)
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            // Two records that both mark bit 0 dirty.
            interpreter.Apply(ctx, new[] { Float64Record(1, 1.0), Float64Record(1, 2.0) });

            Assert.Equal(1, flusherCallCount);
        }

        // ── Test 4: Flusher NOT called when bit not set ───────────────────────────

        [Fact]
        public void Apply_FlusherBitNotSet_FlusherNotCalled()
        {
            int flusherCallCount = 0;
            var interpreter = new BinaryInterpreterBuilder()
                .RegisterHandler(1, (ctx, rec) => { /* does NOT call MarkSubsystemDirty */ })
                .RegisterSubsystemFlusher(0, ctx => flusherCallCount++)
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            interpreter.Apply(ctx, new[] { Float64Record(1, 1.0) });

            Assert.Equal(0, flusherCallCount);
        }

        // ── Test 5: Multiple installers compose without conflict ──────────────────

        [Fact]
        public void Apply_MultipleInstallers_AllHandlersComposed()
        {
            var touched = new List<int>();

            var installer1 = new DelegateInstaller(b =>
                b.RegisterHandler(1, (ctx, rec) => touched.Add(1)));
            var installer2 = new DelegateInstaller(b =>
                b.RegisterHandler(2, (ctx, rec) => touched.Add(2)));

            var interpreter = new BinaryInterpreterBuilder()
                .AddInstaller(installer1)
                .AddInstaller(installer2)
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            interpreter.Apply(ctx, new[] { Float64Record(1, 0), Float64Record(2, 0) });

            Assert.Equal(new[] { 1, 2 }, touched);
        }

        // ── Test 6: Scratchpad offsets are non-overlapping ────────────────────────

        [Fact]
        public void ReserveScratchpad_MultipleReservations_OffsetsDoNotOverlap()
        {
            int offsetA = -1, offsetB = -1;
            const int SizeA = 8, SizeB = 16;

            var installer = new DelegateInstaller(b =>
            {
                offsetA = b.ReserveScratchpad(SizeA);
                offsetB = b.ReserveScratchpad(SizeB);
            });

            new BinaryInterpreterBuilder().AddInstaller(installer).Build();

            Assert.Equal(0, offsetA);
            Assert.Equal(SizeA, offsetB);
        }

        // ── Test 7: DirtyDescriptorMask reset between Apply calls ─────────────────

        [Fact]
        public void Apply_SecondCall_DirtyMasksReset()
        {
            ulong capturedMask = ulong.MaxValue;
            var interpreter = new BinaryInterpreterBuilder()
                .RegisterHandler(1, (ctx, rec) => ctx.MarkDescriptorDirty(5))
                .RegisterHandler(2, (ctx, rec) =>
                {
                    // On second Apply (id=2 handler), dirty mask should only reflect
                    // what's been set in THIS Apply, not a carry-over from first.
                    capturedMask = ctx.DirtyDescriptorMask;
                })
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            // First Apply sets bit 5 from id=1.
            interpreter.Apply(ctx, new[] { Float64Record(1, 0) });
            // Second Apply: only id=2 handler runs — mask should start at 0, then
            // MarkDescriptorDirty is NOT called for id=2, so captured mask is 0.
            interpreter.Apply(ctx, new[] { Float64Record(2, 0) });

            Assert.Equal(0UL, capturedMask);
        }

        // ── Private helper: installer backed by a delegate ────────────────────────

        private sealed class DelegateInstaller : IBinaryAttributeInstaller
        {
            private readonly Action<BinaryInterpreterBuilder> _configure;
            public DelegateInstaller(Action<BinaryInterpreterBuilder> configure) =>
                _configure = configure;
            public void Install(BinaryInterpreterBuilder builder) => _configure(builder);
        }

        // ── Test 8: Scratchpad zeroed between Apply calls (ATTR2-DEBT-04) ─────────

        [Fact]
        public void Apply_ScratchpadClearedBetweenCalls_StaleDataNotCarriedOver()
        {
            const long Sentinel = unchecked((long)0xDEADBEEFCAFEBABEL);
            const int ScratchpadSize = 8; // sizeof(long)

            // Build interpreter whose handler writes a sentinel into the scratchpad,
            // and whose id=2 handler reads the scratchpad value before id=1 writes it.
            long valueAtStartOfSecondCall = long.MaxValue;

            var interpreter = new BinaryInterpreterBuilder()
                .AddInstaller(new DelegateInstaller(b =>
                {
                    int offset = b.ReserveScratchpad(ScratchpadSize);
                    // id=1: writes sentinel
                    b.RegisterHandler(1, (ctx2, rec) =>
                    {
                        ref long slot = ref ctx2.GetScratchpad<long>(offset);
                        slot = Sentinel;
                    });
                    // id=2: reads scratchpad (before id=1 can write it in second Apply)
                    b.RegisterHandler(2, (ctx2, rec) =>
                    {
                        valueAtStartOfSecondCall = ctx2.GetScratchpad<long>(offset);
                    });
                }))
                .Build();

            var ctx = interpreter.CreateContext(EmptyCtx());
            // First Apply: id=1 writes Sentinel into scratchpad.
            interpreter.Apply(ctx, new[] { Float64Record(1, 0) });
            // Second Apply: id=2 reads first (before id=1 sets sentinel again).
            // If scratchpad was NOT zeroed, valueAtStartOfSecondCall == Sentinel.
            interpreter.Apply(ctx, new[] { Float64Record(2, 0), Float64Record(1, 0) });

            Assert.Equal(0L, valueAtStartOfSecondCall);
        }
    }
}
