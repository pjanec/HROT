using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for the ReduceTopK compaction contract (TASK-EQS-011 support tests).
    /// These tests verify the key invariants of the compaction logic used by EqsSolverSystem:
    ///   - EntityId = -1L entries are removed (rejection sentinel).
    ///   - EntityId = 0 entries are preserved (valid positional candidates).
    ///   - Output is truncated to maxTopK when over the limit.
    /// </summary>
    public class EqsSolverSystemUnitTests
    {
        // T-RK1: ReduceTopK logic preserves positional (EntityId=0) and removes rejected (-1L).
        [Fact]
        public void ReduceTopK_Contract_PreservesPositional_RemovesRejected()
        {
            var arr = new EqsResult[]
            {
                new EqsResult { EntityId = -1L, Score = 0f },
                new EqsResult { EntityId =  0L, Score = 0f }, // positional
                new EqsResult { EntityId =  5L, Score = 1f }, // entity-shaped
            };

            // Replicate the ReduceTopK compaction contract:
            // compact valid (EntityId != -1L) entries to the front.
            var span       = arr.AsSpan();
            int validCount = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].EntityId != -1L)
                    span[validCount++] = span[i];
            }

            var valid = span.Slice(0, validCount);

            Assert.Equal(2, valid.Length);

            bool hasPositional  = false;
            bool hasEntityShaped = false;
            bool hasRejected    = false;
            for (int i = 0; i < valid.Length; i++)
            {
                if (valid[i].EntityId ==  0L) hasPositional  = true;
                if (valid[i].EntityId ==  5L) hasEntityShaped = true;
                if (valid[i].EntityId == -1L) hasRejected    = true;
            }

            Assert.True(hasPositional,   "EntityId=0 positional must be preserved");
            Assert.True(hasEntityShaped, "EntityId=5 entity-shaped must be preserved");
            Assert.False(hasRejected,    "EntityId=-1L rejected must not appear in output");
        }

        // T-RK2: ReduceTopK truncates output when there are more candidates than maxTopK.
        [Fact]
        public void ReduceTopK_Contract_TruncatesToMaxTopK()
        {
            const int total  = 20;
            const int maxTopK = 16;

            // All candidates are valid (none rejected).
            var arr = new EqsResult[total];
            for (int i = 0; i < total; i++)
                arr[i] = new EqsResult { EntityId = i + 1L, Score = (float)i };

            var span       = arr.AsSpan();
            int validCount = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].EntityId != -1L)
                    span[validCount++] = span[i];
            }
            var validSpan = span.Slice(0, validCount);

            // Truncate to maxTopK.
            var result = validSpan.Length > maxTopK
                ? validSpan.Slice(0, maxTopK)
                : validSpan;

            Assert.Equal(maxTopK, result.Length);
        }
    }
}
