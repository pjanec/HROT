using Xunit;
using Fdp.Toolkit.Replication.Utilities;

namespace Fdp.Toolkit.Replication.Tests.Utilities
{
    /// <summary>CE-492 — per-instance destination order by source timestamp (<see cref="SourceTimeOrder{TKey}"/>).</summary>
    public class SourceTimeOrderTests
    {
        [Fact]
        public void AnOlderSample_IsStale_ANewerOrEqualOneIsAccepted()
        {
            var order = new SourceTimeOrder<int>();
            Assert.True(order.Accept(1, 200));
            Assert.False(order.Accept(1, 100));   // arrived later, written earlier
            Assert.True(order.Accept(1, 200));    // equal: accepted
            Assert.True(order.Accept(1, 300));
            Assert.False(order.Accept(1, 250));
        }

        [Fact]
        public void Instances_AreOrderedIndependently()
        {
            var order = new SourceTimeOrder<(long, int)>();
            Assert.True(order.Accept((1000, 1), 500));
            Assert.True(order.Accept((1000, 2), 100));   // another instance: its own clock
            Assert.False(order.Accept((1000, 1), 400));
            Assert.Equal(2, order.Count);
        }
    }
}
