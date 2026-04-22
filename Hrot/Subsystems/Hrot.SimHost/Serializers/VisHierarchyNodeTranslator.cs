using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="VisHierarchyNode"/>.
    ///
    /// <para>On <b>Extract</b> (save), resolves the three entity handles
    /// (Parent, FirstChild, NextSibling) to stable GUID strings.</para>
    /// <para>On <b>Inject</b> (load), resolves GUID strings to Network IDs and writes
    /// <see cref="InitialHierarchyIntent"/> onto the entity so that
    /// <c>GenesisMaterializationSystem</c> can resolve them to live
    /// <see cref="Entity"/> handles once all entities are alive.</para>
    /// </summary>
    public sealed class VisHierarchyNodeTranslator : IEntityScenarioTranslator
    {
        private const string Key = "VisHierarchyNode";

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(VisHierarchyNode));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<VisHierarchyNode>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var node = repo.GetComponent<VisHierarchyNode>(entity);
            var obj  = new JsonObject();

            AddGuidEntry(obj, "Parent",      node.Parent,      repo, resolver);
            AddGuidEntry(obj, "FirstChild",  node.FirstChild,  repo, resolver);
            AddGuidEntry(obj, "NextSibling", node.NextSibling, repo, resolver);

            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;

            var intent = new InitialHierarchyIntent
            {
                ParentNetworkId      = ReadNetworkId(obj, "Parent",      repo, resolver),
                FirstChildNetworkId  = ReadNetworkId(obj, "FirstChild",  repo, resolver),
                NextSiblingNetworkId = ReadNetworkId(obj, "NextSibling", repo, resolver),
            };

            repo.SetManagedComponent(entity, intent);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void AddGuidEntry(
            JsonObject obj, string fieldName, Entity handle,
            EntityRepository repo, IGuidResolver resolver)
        {
            if (handle.IsNull || !repo.IsAlive(handle))
                obj[fieldName] = JsonValue.Create<string?>(null);
            else
                obj[fieldName] = resolver.Resolve(handle);
        }

        private static long ReadNetworkId(
            JsonObject obj, string fieldName,
            EntityRepository repo, IGuidResolver resolver)
        {
            var guidStr = obj[fieldName]?.GetValue<string?>();
            if (string.IsNullOrEmpty(guidStr)) return 0L;

            Entity resolved = resolver.Resolve(guidStr);
            if (resolved.IsNull || !repo.IsAlive(resolved)) return 0L;

            return repo.GetComponent<NetworkIdentity>(resolved).Value;
        }
    }
}
