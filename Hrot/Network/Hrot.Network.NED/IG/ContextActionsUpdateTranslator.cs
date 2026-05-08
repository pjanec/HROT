using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using Hrot.Common.Events;
using DdsContextActionsUpdate = Hrot.NED.Messages.ContextActionsUpdate;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// Ingress translator that converts DDS ContextActionsUpdate messages into
    /// <see cref="Hrot.Common.Events.ContextActionsUpdate"/> managed events.
    ///
    /// The raw <c>MenuDefinitionJson</c> string is forwarded directly without any
    /// object-level parsing, preserving the zero-allocation hot path.
    /// </summary>
    public sealed class ContextActionsUpdateTranslator
        : CycloneManagedEventTranslator<ContextActionsUpdate, DdsContextActionsUpdate>
    {
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public override TranslatorDirection Direction => TranslatorDirection.Ingress;

        public ContextActionsUpdateTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IEventBus eventBus,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId = 0)
            : base(participant, "ContextActionsUpdate", entityMap, eventBus)
        {
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        public override void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = Reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                var input = sample.Data;
                int entityId = 0;
                if (input.ForSelection != null && input.ForSelection.Count > 0)
                    entityId = input.ForSelection[0];

                if (entityId != 0 && !EntityMap.TryGetEntity(entityId, out _))
                {
                    var repo = view as EntityRepository;
                    if (repo == null)
                    {
                        FdpLog<ContextActionsUpdateTranslator>.Warn(
                            "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, entityId);
                        continue;
                    }

                    _ghostCreationSystem.CreateGhost(repo, entityId);
                }

                if (TryDecode(input, out ContextActionsUpdate output))
                {
                    EventBus.PublishManaged(output);
                }
            }
        }

        protected override bool TryDecode(in DdsContextActionsUpdate input, out ContextActionsUpdate output)
        {
            int entityId = 0;
            if (input.ForSelection != null && input.ForSelection.Count > 0)
                entityId = input.ForSelection[0];

            // Pass the raw JSON string directly — no object-level parsing.
            output = new ContextActionsUpdate
            {
                EntityNetworkId = entityId,
                MenuJson        = input.MenuDefinitionJson ?? string.Empty,
            };

            return true;
        }

        protected override bool TryEncode(ContextActionsUpdate input, out DdsContextActionsUpdate output)
        {
            output = default;
            return false;
        }
    }
}
