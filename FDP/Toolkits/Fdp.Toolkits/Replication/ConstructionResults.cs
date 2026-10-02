using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication
{
    /// <summary>The outcome of a reliable-init construction, as read from the local poll-store.</summary>
    public enum ConstructionOutcome
    {
        /// <summary>Still waiting for the wait-set (or never registered).</summary>
        Pending = 0,
        /// <summary>Every listed peer reported Active — the entity is fully live.</summary>
        Success = 1,
        /// <summary>The construction was aborted (see <see cref="ConstructionFailReason"/>).</summary>
        Failed  = 2,
    }

    /// <summary>Why a reliable-init construction failed.</summary>
    public enum ConstructionFailReason
    {
        None        = 0,
        /// <summary>The creator's authoritative timeout expired with the wait-set unsatisfied (§3b.3).</summary>
        Timeout     = 1,
        /// <summary>A peer explicitly rejected the construction.</summary>
        PeerRejected = 2,
    }

    /// <summary>The value returned by <see cref="ConstructionResults.Get(EntityRepository, long)"/>.</summary>
    public readonly struct ConstructionResult
    {
        public readonly long NetworkId;
        public readonly ConstructionOutcome Outcome;
        public readonly ConstructionFailReason Reason;
        public ConstructionResult(long networkId, ConstructionOutcome outcome, ConstructionFailReason reason)
        {
            NetworkId = networkId; Outcome = outcome; Reason = reason;
        }
        public bool IsResolved => Outcome != ConstructionOutcome.Pending;
    }

    /// <summary>
    /// CE-290 (C4) — the creator-LOCAL poll-store for reliable-init outcomes.
    ///
    /// <para>⭐ The LOCAL sibling of the remote <c>CreateEntityAck</c> (owned by DESIGN-SIMHOST/IG): a code call
    /// on the owning node creates a reliable entity and later polls <see cref="Get(EntityRepository, long)"/> at
    /// its OWN cadence, keyed by the <c>NetworkId</c> it holds from the create call. Mirrors
    /// <c>PathfindingBatchData</c> (an async result read from a ticked system) — NOT the remote ack path
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3b.4).</para>
    ///
    /// <para>⭐ A <b>dictionary keyed by <c>NetworkId</c> with evict-on-read of terminal results</b> (user
    /// choice): a lost creation-FAILURE must never be silently overwritten, and creation volume is low, so a dict
    /// is affordable (unlike Pathfinding's collision-tolerant ring). A TTL sweep
    /// (<see cref="EvictOlderThan"/>) reclaims entries a requestor never reads. A managed singleton component
    /// (id 153) on the creator's world; written by the gateway, read by the requestor.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.ConstructionResults)]
    public sealed class ConstructionResults
    {
        private struct Entry
        {
            public ConstructionOutcome Outcome;
            public ConstructionFailReason Reason;
            public uint Frame;   // frame the entry was last written — for TTL eviction.
        }

        private readonly Dictionary<long, Entry> _byId = new();

        /// <summary>Default TTL — 60 s @ 60 Hz. An entry a requestor never polls is reclaimed after this.</summary>
        public const uint DefaultTtlFrames = 3600;

        /// <summary>Mark a reliable construction as in-progress (written by the gateway when it defers).</summary>
        public void SetPending(long networkId, uint frame)
            => _byId[networkId] = new Entry { Outcome = ConstructionOutcome.Pending, Reason = ConstructionFailReason.None, Frame = frame };

        /// <summary>Record a terminal outcome (written by the gateway on wait-set completion or abort).</summary>
        public void Resolve(long networkId, ConstructionOutcome outcome, ConstructionFailReason reason, uint frame)
            => _byId[networkId] = new Entry { Outcome = outcome, Reason = reason, Frame = frame };

        /// <summary>Poll one result. A TERMINAL result is <b>evicted on read</b> so it is delivered exactly once;
        /// an unknown id (never registered, or already read) reads <see cref="ConstructionOutcome.Pending"/>.</summary>
        public ConstructionResult Get(long networkId)
        {
            if (!_byId.TryGetValue(networkId, out var e))
                return new ConstructionResult(networkId, ConstructionOutcome.Pending, ConstructionFailReason.None);
            if (e.Outcome != ConstructionOutcome.Pending)
                _byId.Remove(networkId);   // evict-on-read: a terminal result is delivered once.
            return new ConstructionResult(networkId, e.Outcome, e.Reason);
        }

        /// <summary>Reclaim entries older than <paramref name="ttlFrames"/> (a requestor that never polled).</summary>
        public void EvictOlderThan(uint currentFrame, uint ttlFrames)
        {
            if (_byId.Count == 0) return;
            List<long>? stale = null;
            foreach (var kv in _byId)
                // Guard the uint subtraction: an entry written at a frame AHEAD of currentFrame (a replay
                // GlobalVersion reset, or clock skew) is not stale — never underflow it into a huge age.
                if (currentFrame >= kv.Value.Frame && currentFrame - kv.Value.Frame > ttlFrames)
                    (stale ??= new List<long>()).Add(kv.Key);
            if (stale != null)
                foreach (var id in stale) _byId.Remove(id);
        }

        /// <summary>Count of retained entries (for tests/diagnostics).</summary>
        public int Count => _byId.Count;

        // ── Static world accessors ────────────────────────────────────────────────

        /// <summary>Fetch the creator world's store, creating it on first use.
        /// ⚠ <c>GetSingletonManaged</c> THROWS when the singleton is unset, so guard with
        /// <c>HasSingletonManaged</c> — do not rely on a null return.</summary>
        public static ConstructionResults GetOrCreate(EntityRepository world)
        {
            if (world.HasSingletonManaged<ConstructionResults>())
                return world.GetSingletonManaged<ConstructionResults>()!;
            var store = new ConstructionResults();
            world.SetSingletonManaged(store);
            return store;
        }

        /// <summary>The requestor's poll entry point (§3b.5): <c>ConstructionResults.Get(world, networkId)</c>.
        /// Returns <see cref="ConstructionOutcome.Pending"/> until the gateway resolves it (or if the store does
        /// not exist yet).</summary>
        public static ConstructionResult Get(EntityRepository world, long networkId)
        {
            if (!world.HasSingletonManaged<ConstructionResults>())
                return new ConstructionResult(networkId, ConstructionOutcome.Pending, ConstructionFailReason.None);
            return world.GetSingletonManaged<ConstructionResults>()!.Get(networkId);
        }
    }
}
