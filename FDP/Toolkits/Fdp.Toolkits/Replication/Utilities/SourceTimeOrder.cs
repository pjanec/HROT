using System.Collections.Generic;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// ⭐ <b>Destination order by source timestamp, per instance</b> — the application half of DDS
    /// <c>DESTINATION_ORDER = BY_SOURCE_TIMESTAMP</c>, which the CycloneDDS.NET binding does not expose (it sets
    /// reliability, durability, history, partition, resource limits and data representation only). A sample OLDER than the
    /// newest one already accepted for its instance is stale and is dropped.
    /// </summary>
    /// <remarks>
    /// <para>🔴 <b>Why.</b> An instance with TWO writers — an old owner's last sample and a new owner's later one after an
    /// authority move — reaches a late-joining reader with no ordering guarantee between the writers: TransientLocal hands
    /// it each writer's durable sample in discovery order, and on a KeepLast-1 reader the one that arrives LAST wins. Within
    /// one writer DDS already preserves order; across writers only the source time can say which is newer.</para>
    /// <para>⚠ It is only as good as the clocks: a writer whose clock lags another's by more than the gap between their
    /// writes is ordered wrongly. 📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §3a (<c>CE-492</c>).</para>
    /// </remarks>
    public sealed class SourceTimeOrder<TKey> where TKey : notnull
    {
        private readonly Dictionary<TKey, long> _newest = new();

        /// <summary>
        /// True when <paramref name="sourceTimestamp"/> is not older than the newest sample accepted for
        /// <paramref name="key"/> (it then becomes the newest); false for a stale sample. Equal times are accepted.
        /// </summary>
        public bool Accept(TKey key, long sourceTimestamp)
        {
            if (_newest.TryGetValue(key, out long newest) && sourceTimestamp < newest) return false;
            _newest[key] = sourceTimestamp;
            return true;
        }

        /// <summary>The number of instances tracked.</summary>
        public int Count => _newest.Count;
    }
}
