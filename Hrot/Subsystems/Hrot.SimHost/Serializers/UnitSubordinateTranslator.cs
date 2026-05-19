using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="UnitSubordinate"/>.
    ///
    /// Serialises the commander entity as a stable GUID reference and the tactical
    /// designation as a string.  On inject, resolves the GUID to a network ID and
    /// attaches <see cref="InitialUnitSubordinateIntent"/> for deferred materialisation
    /// by <c>GenesisMaterializationSystem</c>.
    /// </summary>
    public sealed class UnitSubordinateTranslator : IEntityScenarioTranslator
    {
        private const string Key = "UnitSubordinate";

        private sealed class UnitSubordinateDto
        {
            public string? CommanderGuid { get; set; }
            public string? Designation { get; set; }
        }

        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(UnitSubordinate));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<UnitSubordinate>(entity)
            && !repo.GetComponent<UnitSubordinate>(entity).Commander.IsNull;

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var sub = repo.GetComponent<UnitSubordinate>(entity);
            if (sub.Commander.IsNull)
                return new Dictionary<string, object>();

            var dto = new UnitSubordinateDto
            {
                CommanderGuid = resolver.Resolve(sub.Commander),
                Designation   = sub.Designation.ToString()
            };

            return new Dictionary<string, object>
            {
                [Key] = JsonSerializer.SerializeToNode(dto, s_jsonOptions)!
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj) return;

            var dto = obj.Deserialize<UnitSubordinateDto>(s_jsonOptions);
            if (dto == null || string.IsNullOrEmpty(dto.CommanderGuid)) return;
            Enum.TryParse<TacticalDesignation>(dto.Designation, out var designation);

            Entity resolved = resolver.Resolve(dto.CommanderGuid);
            long networkId;
            if (resolved.IsNull || !repo.IsAlive(resolved))
            {
                FdpLog<UnitSubordinateTranslator>.Warn(
                    "[UnitSubordinateTranslator] Commander GUID '{0}' could not be resolved; " +
                    "attaching intent with CommanderNetworkId = 0.", dto.CommanderGuid);
                networkId = 0;
            }
            else if (!repo.HasComponent<NetworkIdentity>(resolved))
            {
                FdpLog<UnitSubordinateTranslator>.Warn(
                    "[UnitSubordinateTranslator] Resolved commander entity has no NetworkIdentity; " +
                    "attaching intent with CommanderNetworkId = 0.");
                networkId = 0;
            }
            else
            {
                networkId = repo.GetComponent<NetworkIdentity>(resolved).Value;
            }

            repo.SetManagedComponent(entity, new InitialUnitSubordinateIntent
            {
                CommanderNetworkId = networkId,
                Designation        = designation,
            });
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
