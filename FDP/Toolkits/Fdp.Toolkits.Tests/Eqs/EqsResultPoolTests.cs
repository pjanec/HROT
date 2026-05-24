using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EqsResultPool"/> ring-buffer logic and
    /// <see cref="EqsResultEvent"/> unmanaged-type invariant.
    /// </summary>
    public class EqsResultPoolTests : IDisposable
    {
        private readonly NativeArray<EqsResult> _backingArray;
        private EqsResultPool _pool;

        public EqsResultPoolTests()
        {
            _backingArray = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent);
            _pool = new EqsResultPool { NextFreeIndex = 0, Results = _backingArray };
        }

        public void Dispose()
        {
            _backingArray.Dispose();
        }

        // ── Test 1: EqsResultEvent is an unmanaged type ───────────────────────────

        [Fact]
        public void EqsResultEvent_IsUnmanaged()
        {
            // Unsafe.SizeOf only compiles when T : unmanaged; a non-zero size also confirms
            // that the struct has fields (not degenerate empty).
            Assert.True(Unsafe.SizeOf<EqsResultEvent>() > 0,
                "EqsResultEvent must be an unmanaged struct with at least one field.");
        }

        // ── Test 2: Write wraps when cursor + count exceeds capacity ─────────────

        [Fact]
        public void EqsResultPool_WrapWriteAt16382_WrapsCorrectly()
        {
            // Arrange: position cursor near the end so a 3-entry write would overflow
            _pool.NextFreeIndex = 16382;

            var batch = new EqsResult[]
            {
                new EqsResult { EntityId = 10L, Score = 1f },
                new EqsResult { EntityId = 20L, Score = 2f },
                new EqsResult { EntityId = 30L, Score = 3f },
            };

            // Act
            int handle = _pool.WriteAndWrap(batch);

            // Assert: cursor wrapped — handle must be 0 (wrote from beginning)
            Assert.Equal(0, handle);
            // After writing 3 from index 0, cursor advances to 3
            Assert.Equal(3, _pool.NextFreeIndex);
            // Verify the data landed at the wrapped indices
            Assert.Equal(10L, _pool.Results[0].EntityId);
            Assert.Equal(20L, _pool.Results[1].EntityId);
            Assert.Equal(30L, _pool.Results[2].EntityId);
        }

        // ── Test 3: Write that lands exactly at capacity resets cursor to 0 ───────

        [Fact]
        public void EqsResultPool_WrapWriteExactlyAtEnd_NoWrap()
        {
            // Arrange: position cursor so writing 4 entries lands exactly at PoolCapacity (16384)
            _pool.NextFreeIndex = EqsResultPool.PoolCapacity - 4; // 16380

            var batch = new EqsResult[]
            {
                new EqsResult { EntityId = 1L },
                new EqsResult { EntityId = 2L },
                new EqsResult { EntityId = 3L },
                new EqsResult { EntityId = 4L },
            };

            // Act
            int handle = _pool.WriteAndWrap(batch);

            // Assert: no wrap — the batch was written starting at 16380 (no reset before write)
            Assert.Equal(EqsResultPool.PoolCapacity - 4, handle);
            // Cursor lands exactly at PoolCapacity, which resets to 0
            Assert.Equal(0, _pool.NextFreeIndex);
            // Verify the data at positions 16380–16383
            Assert.Equal(1L, _pool.Results[EqsResultPool.PoolCapacity - 4].EntityId);
            Assert.Equal(4L, _pool.Results[EqsResultPool.PoolCapacity - 1].EntityId);
        }
    }
}
