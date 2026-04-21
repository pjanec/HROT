using Hrot.Core.Network;
using System;
using Xunit;

namespace Hrot.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="NullEntityAckSink"/> — the no-op ACK sink used when the
    /// genesis pipeline runs without a live network transport.
    /// </summary>
    public sealed class NullEntityAckSinkTests
    {
        /// <summary>
        /// WriteAck does not throw for any combination of valid inputs.
        /// </summary>
        [Fact]
        public void WriteAck_DoesNotThrow()
        {
            var sink = new NullEntityAckSink();

            // Should complete silently for all status values.
            sink.WriteAck(Guid.NewGuid(), 0L, EntityOperationStatus.Success);
            sink.WriteAck(Guid.Empty,     -1L, EntityOperationStatus.EntityNotFound);
        }

        /// <summary>
        /// NullEntityAckSink implements <see cref="IEntityAckSink"/> so it can substitute
        /// any real ACK sink in the genesis pipeline.
        /// </summary>
        [Fact]
        public void NullEntityAckSink_ImplementsIEntityAckSink()
        {
            Assert.True(typeof(IEntityAckSink).IsAssignableFrom(typeof(NullEntityAckSink)));
        }
    }
}
