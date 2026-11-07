using System;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ CE-2380 part 1 (📄 docs/DESIGN_Remote_Edit_Outcome.md §2, R-2082) — the outcome of one remote edit: a MANAGED EVENT on the bus (and a row of
    /// <see cref="EditOutcomeLog"/>). Immutable (init-only): built by <see cref="EditOutcomes.Report"/> or by the wire ingress. ⭐ Edit framework F2: also the
    /// answer to a create and a delete (the old CreateUpdateDeleteEntityAck is retired).
    /// </summary>
    public sealed class EditOutcome
    {
        /// <summary>The longest <see cref="Reason"/> kept (lean E).</summary>
        public const int MaxReasonLength = 1000;

        /// <summary>The id <see cref="EditRequests"/> minted for the edit.</summary>
        public Guid RequestId { get; init; }
        /// <summary>The network id of the entity the edit addressed.</summary>
        public int NetworkId { get; init; }
        public EditStatus Status { get; init; }
        /// <summary>Why — for a refusal, what was not applied. Capped at <see cref="MaxReasonLength"/> by <see cref="Create"/>.</summary>
        public string Reason { get; init; } = string.Empty;
        /// <summary>The node that answered. ⭐ Edit framework §2.4 (F2): typed <see cref="Fdp.Core.NodeId"/> in the engine; a plain <c>int</c> on the wire (SV-3).</summary>
        public Fdp.Core.NodeId RespondingNode { get; init; }
        /// <summary>Sim time (s) it was reported on the answering node.</summary>
        public Fdp.Core.SimInstant At { get; init; }
        /// <summary>True when it ARRIVED from another node (the wire ingress); false when this node reported it.</summary>
        public bool FromNetwork { get; init; }

        /// <summary>The factory: caps the reason (null ⇒ empty).</summary>
        public static EditOutcome Create(Guid requestId, int networkId, EditStatus status, string? reason, int respondingNode, double at, bool fromNetwork)
            => new()
            {
                RequestId = requestId, NetworkId = networkId, Status = status,
                Reason = Cap(reason), RespondingNode = (Fdp.Core.NodeId)respondingNode, At = (Fdp.Core.SimInstant)at, FromNetwork = fromNetwork,
            };

        internal static string Cap(string? reason)
            => string.IsNullOrEmpty(reason) ? string.Empty : reason.Length <= MaxReasonLength ? reason : reason.Substring(0, MaxReasonLength);
    }
}
