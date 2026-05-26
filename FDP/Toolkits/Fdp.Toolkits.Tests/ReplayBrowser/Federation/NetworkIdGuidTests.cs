using System;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// Tests for <see cref="NetworkIdGuid"/> (RBF-P3T1).
    /// </summary>
    public sealed class NetworkIdGuidTests
    {
        // ── RBF-P3T1 ─────────────────────────────────────────────────────────

        /// <summary>
        /// Round-trip: <c>ToLong(From(v)) == v</c> for a representative set of
        /// long values including boundary values and a known bit pattern.
        /// </summary>
        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        [InlineData(unchecked((long)0xDEADBEEFCAFEBABEL))]
        public void RBF_P3T1_NetworkIdGuid_RoundTrips(long value)
        {
            var guid = NetworkIdGuid.From(value);
            long result = NetworkIdGuid.ToLong(guid);

            Assert.Equal(value, result);
        }

        /// <summary>
        /// The <see cref="Guid"/> returned by <see cref="NetworkIdGuid.From"/> must always
        /// be parseable by <see cref="Guid.TryParse"/> (well-formed 8-4-4-4-12 hex string).
        /// </summary>
        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        [InlineData(unchecked((long)0xDEADBEEFCAFEBABEL))]
        public void RBF_P3T1_NetworkIdGuid_ProducesValidGuidString(long value)
        {
            var guid = NetworkIdGuid.From(value);
            Assert.True(Guid.TryParse(guid.ToString(), out _),
                $"From({value}) must produce a valid Guid string.");
        }
    }
}
