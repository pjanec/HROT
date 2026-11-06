using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Core.Network;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// ⭐ Edit framework F2 (📄 docs/UX/DESIGN_Edit_Framework.md §2.4, R-4126) — the create / delete systems' answer IS the ONE edit outcome: every lifecycle
    /// ack is reported through <see cref="EditOutcomes.Report"/> on the processing node's world, so the requester reads it as <c>EntityEditOutcome</c> under
    /// its own request id (InProgress ⇒ Pending, Success ⇒ Applied with the entity's id, an error ⇒ Refused with the reason). The pack wraps every host's
    /// sink in it, so no host can lack it. <paramref name="inner"/> (a test's observer, or none) still sees every ack.
    /// </summary>
    public sealed class EditOutcomeEntityAckSink : IEntityAckSink
    {
        private readonly EntityRepository _world;
        private readonly IEntityAckSink? _inner;
        private readonly int _localNode;

        public EditOutcomeEntityAckSink(EntityRepository world, int localNode, IEntityAckSink? inner = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _localNode = localNode;
            _inner = inner is NullEntityAckSink ? null : inner;
        }

        public void WriteAck(Guid requestId, int entityId, EntityOperationStatus status)
        {
            _inner?.WriteAck(requestId, entityId, status);
            var (edit, reason) = Map(status);
            EditOutcomes.Report(_world, requestId, entityId, edit, reason, _localNode);
        }

        /// <summary>The lifecycle status as an edit status and its words.</summary>
        public static (EditStatus Status, string Reason) Map(EntityOperationStatus status) => status switch
        {
            EntityOperationStatus.Success => (EditStatus.Applied, "done"),
            EntityOperationStatus.InProgress => (EditStatus.Pending, "accepted; the entity is being set up or torn down"),
            EntityOperationStatus.UnknownDescriptorType => (EditStatus.Refused, "the entity type is not known to the TKB"),
            EntityOperationStatus.EntityNotFound => (EditStatus.Refused, "no such entity on its owner"),
            _ => (EditStatus.Refused, status.ToString()),
        };
    }
}
