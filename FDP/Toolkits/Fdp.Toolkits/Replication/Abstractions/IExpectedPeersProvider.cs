using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// Resolves the set of peer node ids a creator must collect an <c>Active</c> ack from before its own
    /// reliable-init entity leaves <c>Constructing</c> (the cross-node construction barrier;
    /// DESIGN_Cross_Node_Construction_Barrier.md §3b).
    ///
    /// <para>The seam keeps the peer-set POLICY out of the generic spawn/barrier code: a transport adapter
    /// backs it with its membership view (the NED cluster cache) and the role→init contract, and a test backs
    /// it with a fixed set. ⭐ Piece C role-filters: expected-peers(X) = present nodes whose role INITIALISES
    /// one of X's <c>[RequiresPeerInit]</c> components (§3b), replacing slice A's proof-only "all peers".</para>
    /// </summary>
    public interface IExpectedPeersProvider
    {
        /// <summary>
        /// The peer node ids the creator must wait for, given the entity's live component mask
        /// <paramref name="entityComponentMask"/> (intersected with the <c>[RequiresPeerInit]</c> set to find
        /// what needs peer init) on <paramref name="localNodeId"/>. Excludes the local node. Empty ⇒ no
        /// cross-node wait (no present peer's role initialises any of the entity's init-requiring components).
        /// </summary>
        IReadOnlyList<int> GetExpectedPeers(in BitMask512 entityComponentMask, int localNodeId);
    }
}
