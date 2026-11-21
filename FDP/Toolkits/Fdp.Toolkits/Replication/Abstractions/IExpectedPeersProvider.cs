using System.Collections.Generic;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// Resolves the set of peer node ids a creator must collect an <c>Active</c> ack from before
    /// its own reliable-init entity leaves <c>Constructing</c> (the cross-node construction barrier;
    /// DESIGN_Cross_Node_Construction_Barrier.md §3a.4).
    ///
    /// <para>The seam keeps the peer-set POLICY out of the generic spawn/barrier code: a transport
    /// adapter backs it with its membership view (the NED cluster cache), and a test backs it with a
    /// fixed set. ⚠ For barrier slice A the production impl yields <b>all present peers except local</b>
    /// (PROOF-correct, not production-correct — piece C narrows it to the roles that actually register
    /// a participant for the entity type; see the design's §3a.7 A5 decision).</para>
    /// </summary>
    public interface IExpectedPeersProvider
    {
        /// <summary>
        /// The peer node ids the creator must wait for, for an entity of <paramref name="blueprintId"/>
        /// created on <paramref name="localNodeId"/>. Excludes the local node. Empty ⇒ no cross-node wait.
        /// </summary>
        IReadOnlyList<int> GetExpectedPeers(long blueprintId, int localNodeId);
    }
}
