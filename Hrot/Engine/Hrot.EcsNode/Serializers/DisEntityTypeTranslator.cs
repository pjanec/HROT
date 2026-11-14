using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator that persists the DIS entity type of an entity.
    /// When the entity's DisType is zero but a <see cref="TkbIdentity"/> component is present,
    /// the translator attempts a TKB-database fallback lookup so that scenario files authored
    /// before DIS types were explicitly set still round-trip correctly.
    /// </summary>
    public sealed class DisEntityTypeTranslator : IEntityScenarioTranslator
    {
        private const string Key = "DisEntityType";

        // DisEntityType is plain value data — safe to extract from the staging repository.
        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask() => new BitMask512();

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.IsAlive(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var disType = repo.GetDisType(entity);

            // Fallback: if the entity carries no explicit DIS type, try deriving it from TKB.
            if (disType.Value == 0 && repo.HasComponent<TkbIdentity>(entity))
            {
                ref readonly var tkbIdentity = ref repo.GetComponentRO<TkbIdentity>(entity);
                var tkb = repo.HasSingletonManaged<ITkbDatabase>()
                    ? repo.GetSingletonManaged<ITkbDatabase>()
                    : null;

                if (tkb != null && tkb.TryGetByType(tkbIdentity.TkbType, out var template))
                    disType = template.DisType;
            }

            if (disType.Value == 0)
                return new Dictionary<string, object>();

            var arr = new JsonArray
            {
                (int)disType.Kind,
                (int)disType.Domain,
                (int)disType.Country,
                (int)disType.Category,
                (int)disType.Subcategory,
                (int)disType.Specific,
                (int)disType.Extra,
            };

            return new Dictionary<string, object> { [Key] = arr };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonArray arr || arr.Count < 7)
                return;

            var disType = new DISEntityType
            {
                Kind        = (byte)arr[0]!.GetValue<int>(),
                Domain      = (byte)arr[1]!.GetValue<int>(),
                Country     = (ushort)arr[2]!.GetValue<int>(),
                Category    = (byte)arr[3]!.GetValue<int>(),
                Subcategory = (byte)arr[4]!.GetValue<int>(),
                Specific    = (byte)arr[5]!.GetValue<int>(),
                Extra       = (byte)arr[6]!.GetValue<int>(),
            };

            repo.SetDisType(entity, disType);
        }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}
