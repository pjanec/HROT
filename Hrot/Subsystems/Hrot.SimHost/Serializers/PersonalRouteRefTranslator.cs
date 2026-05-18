using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
using Hrot.Map.Common.Components;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="PersonalRouteRef"/>.
    ///
    /// <para>On <b>Extract</b> (save), resolves the <see cref="PersonalRouteRef.RouteEntity"/>
    /// handle to a stable GUID string.</para>
    /// <para>On <b>Inject</b> (load), resolves the GUID to a Network ID and writes
    /// <see cref="InitialRouteIntent"/> so that <c>GenesisMaterializationSystem</c>
    /// can resolve it to a live <see cref="Entity"/> handle once the route entity is alive.</para>
    /// </summary>
    public sealed class PersonalRouteRefTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(PersonalRouteRef);

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(PersonalRouteRef));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<PersonalRouteRef>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var routeRef = repo.GetComponent<PersonalRouteRef>(entity);

            string? guidStr = null;
            if (!routeRef.RouteEntity.IsNull && repo.IsAlive(routeRef.RouteEntity))
                guidStr = resolver.Resolve(routeRef.RouteEntity);

            return new Dictionary<string, object>
            {
                [Key] = new JsonObject { ["Route"] = guidStr }
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            var guidStr = obj["Route"]?.GetValue<string?>();
            if (string.IsNullOrEmpty(guidStr)) return;

            Entity resolved = resolver.Resolve(guidStr);
            if (resolved.IsNull || !repo.IsAlive(resolved)) return;

            long networkId = repo.GetComponent<NetworkIdentity>(resolved).Value;
            repo.SetManagedComponent(entity, new InitialRouteIntent { RouteNetworkId = networkId });
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
