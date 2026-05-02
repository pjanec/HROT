using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
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
    /// designation as an integer.  On inject, resolves the GUID to a network ID and
    /// attaches <see cref="InitialUnitSubordinateIntent"/> for deferred materialisation
    /// by <c>GenesisMaterializationSystem</c>.
    /// </summary>
    public sealed class UnitSubordinateTranslator : IEntityScenarioTranslator
    {
        private const string Key = "UnitSubordinate";

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

            string commanderGuid = resolver.Resolve(sub.Commander);
            int designation = (int)sub.Designation;

            return new Dictionary<string, object>
            {
                [Key] = new JsonObject
                {
                    ["commanderGuid"] = commanderGuid,
                    ["designation"]   = designation,
                }
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            string? commanderGuidStr = obj["commanderGuid"]?.GetValue<string>();
            int designation = obj["designation"]?.GetValue<int>() ?? 0;

            if (string.IsNullOrEmpty(commanderGuidStr)) return;

            Entity resolved = resolver.Resolve(commanderGuidStr);
            long networkId;
            if (resolved.IsNull || !repo.IsAlive(resolved))
            {
                FdpLog<UnitSubordinateTranslator>.Warn(
                    "[UnitSubordinateTranslator] Commander GUID '{0}' could not be resolved; " +
                    "attaching intent with CommanderNetworkId = 0.", commanderGuidStr);
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
                Designation        = (TacticalDesignation)designation,
            });
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
