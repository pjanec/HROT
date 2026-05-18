using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="IsEmbarkedTag"/>.
    ///
    /// <para>On <b>Extract</b> (save), resolves the <see cref="IsEmbarkedTag.VehicleEntity"/>
    /// handle to a stable GUID string.</para>
    /// <para>On <b>Inject</b> (load), resolves the GUID to a Network ID and writes
    /// <see cref="InitialVehicleIntent"/> so that <c>GenesisMaterializationSystem</c>
    /// can resolve it to a live <see cref="Entity"/> handle once the vehicle is alive.</para>
    /// </summary>
    public sealed class IsEmbarkedTagTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(IsEmbarkedTag);

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(IsEmbarkedTag));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<IsEmbarkedTag>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var tag = repo.GetComponent<IsEmbarkedTag>(entity);

            string? guidStr = null;
            if (!tag.VehicleEntity.IsNull && repo.IsAlive(tag.VehicleEntity))
                guidStr = resolver.Resolve(tag.VehicleEntity);

            return new Dictionary<string, object>
            {
                [Key] = new JsonObject { ["Vehicle"] = guidStr }
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            var guidStr = obj["Vehicle"]?.GetValue<string?>();
            if (string.IsNullOrEmpty(guidStr)) return;

            Entity resolved = resolver.Resolve(guidStr);
            if (resolved.IsNull || !repo.IsAlive(resolved)) return;

            long networkId = repo.GetComponent<NetworkIdentity>(resolved).Value;
            repo.SetManagedComponent(entity, new InitialVehicleIntent { VehicleNetworkId = networkId });
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
