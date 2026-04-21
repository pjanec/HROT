using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using DdsContextActionsUpdate = Hrot.NED.Messages.ContextActionsUpdate;
using IgContextActionsUpdate = Hrot.IG.ContextActionsUpdate;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// Ingress translator that converts DDS ContextActionsUpdate messages into
    /// IG-managed ContextActionsUpdate events.
    /// </summary>
    public sealed class ContextActionsUpdateTranslator
        : CycloneManagedEventTranslator<IgContextActionsUpdate, DdsContextActionsUpdate>
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

                if (TryDecode(input, out IgContextActionsUpdate output))
                {
                    EventBus.PublishManaged(output);
                }
            }
        }

        protected override bool TryDecode(in DdsContextActionsUpdate input, out IgContextActionsUpdate output)
        {
            int entityId = 0;
            if (input.ForSelection != null && input.ForSelection.Count > 0)
                entityId = input.ForSelection[0];

            output = new IgContextActionsUpdate
            {
                EntityNetworkId = entityId,
                Actions = ParseActions(input.MenuDefinitionJson)
            };

            return true;
        }

        protected override bool TryEncode(IgContextActionsUpdate input, out DdsContextActionsUpdate output)
        {
            output = default;
            return false;
        }

        internal static List<ContextAction> ParseActions(string? menuJson)
        {
            var actions = new List<ContextAction>();
            if (string.IsNullOrWhiteSpace(menuJson))
                return actions;

            try
            {
                using var doc = JsonDocument.Parse(menuJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return actions;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!item.TryGetProperty("label", out var labelProp))
                        continue;

                    var label = labelProp.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    string actionName = label;
                    if (item.TryGetProperty("id", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out int id))
                        {
                            // Map well-known ExCon numeric IDs to IG-local action names so
                            // they are executed on the IG side rather than round-tripped to ExCon.
                            // Hrot.ExCon.Logic.ContextMenuActions.CenterOnEntity = 1.
                            // Hrot.ExCon.Logic.ContextMenuActions.Delete           = 10.
                            actionName = id switch
                            {
                                1  => "IG_CenterOnEntity",
                                10 => "IG_DeleteEntity",
                                _  => id.ToString(CultureInfo.InvariantCulture)
                            };
                        }
                        else
                            actionName = idProp.ToString() ?? label;
                    }

                    actions.Add(new ContextAction
                    {
                        Label = label,
                        ActionName = actionName
                    });
                }
            }
            catch (JsonException)
            {
                // Ignore malformed menu JSON and fall back to an empty action list.
            }

            return actions;
        }
    }
}
