using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;
using DdsContextActionsUpdate = Bagira.BDC.SSTM.ContextActionsUpdate;
using IgContextActionsUpdate = Bagira.IG.ContextActionsUpdate;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator that converts DDS ContextActionsUpdate messages into
    /// IG-managed ContextActionsUpdate events.
    /// </summary>
    public sealed class ContextActionsUpdateTranslator
        : CycloneManagedEventTranslator<IgContextActionsUpdate, DdsContextActionsUpdate>
    {
        private readonly GhostCreationSystem _ghostCreationSystem;

        public ContextActionsUpdateTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            IEventBus eventBus,
            GhostCreationSystem ghostCreationSystem)
            : base(participant, "ContextActionsUpdate", entityMap, eventBus)
        {
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
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
                            "[IG] Cannot create ghost for NetID {0}: view is read-only.", entityId);
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

        private static List<ContextAction> ParseActions(string? menuJson)
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
                            actionName = id.ToString(CultureInfo.InvariantCulture);
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
