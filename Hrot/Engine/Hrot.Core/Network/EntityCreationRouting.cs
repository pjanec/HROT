namespace Hrot.Core.Network;

/// <summary>
/// ⭐⭐⭐ <b>THE Level-1 routing rule — <i>"is this creation request mine to service?"</i> — in exactly
/// ONE place.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b (<c>D1</c>).</para>
///
/// <para>📐 <b>Why this exists as a type rather than an <c>if</c>.</b> The rule was inline in
/// <c>CreateEntityRequestSystem.ProcessIncomingRequest</c>, which was fine while exactly one caller
/// needed it. <c>D1</c> adds a second — <see cref="ForwardingEntityCreationRequestSource"/> must ask the
/// same question to decide whether to forward. ⛔ Two copies of a routing rule is precisely the
/// "duplicate implementation of one concept" this programme exists to remove, and the failure mode is
/// nasty: if the copies ever disagree, a request is either serviced twice or by nobody.</para>
///
/// <para>⭐ It is a pure function of the request and the node's two composition-time facts, so it is
/// trivially testable and cannot drift with runtime state.</para>
/// </summary>
public static class EntityCreationRouting
{
    /// <summary>
    /// ⭐ True when <paramref name="request"/> is this node's to service.
    ///
    /// <para>The rule, unchanged from the original inline guard: an explicitly targeted request is
    /// serviced only by its target; a request with no target (<c>OwnerAppInstanceId == 0</c>, i.e.
    /// "any default") is serviced only by the designated default processor. Every other node ignores it,
    /// which is what prevents duplicate ID allocation across the cluster.</para>
    ///
    /// <para>⚠ <b>False does NOT mean "drop it".</b> It means "not mine". Whether the caller then
    /// ignores the request (<c>CreateEntityRequestSystem</c>, for one that arrived from the wire) or
    /// forwards it (<see cref="ForwardingEntityCreationRequestSource"/>, for one authored here) is the
    /// caller's decision — and that split is exactly the level mismatch <c>D1</c> fixes.</para>
    /// </summary>
    /// <param name="request">The request being routed.</param>
    /// <param name="localNodeId">This node's id.</param>
    /// <param name="isDefaultProcessor">
    /// Whether this node intercepts untargeted requests. Exactly one node in the cluster sets this.
    /// ⛔ It is a broadcast TIEBREAKER, not an authority gate (<c>Q65</c> §4).
    /// </param>
    public static bool IsHandledLocally(
        EntityCreationRequest request,
        int                   localNodeId,
        bool                  isDefaultProcessor)
    {
        if (request == null) return false;

        int  targetNodeId    = request.OwnerAppInstanceId;
        bool isTargetedAtMe  = targetNodeId == localNodeId;
        bool isDefaultTarget = targetNodeId == 0;

        return isTargetedAtMe || (isDefaultTarget && isDefaultProcessor);
    }
}
