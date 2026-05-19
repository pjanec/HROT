using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="TargetMemory"/>.
    ///
    /// The <see cref="Fdp.Toolkit.Scenario.FdpAutoSerializer"/> cannot serialize
    /// <c>fixed long[]</c> / <c>fixed float[]</c> buffers or entity-typed integer
    /// fields: the fixed-buffer backing structs are opaque and their contents are
    /// zeroed on every JSON round-trip.  This translator replaces the auto-generated
    /// stub by serialising each valid target entry as a GUID-resolved entity handle
    /// plus the associated position, score, and metadata so handles survive
    /// serialisation/deserialisation.
    /// </summary>
    public sealed unsafe class TargetMemoryTranslator : IEntityScenarioTranslator
    {
        private const string Key = "TargetMemory";

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(TargetMemory));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<TargetMemory>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            // Value copy ensures a stable stack location for pointer arithmetic.
            TargetMemory tm = repo.GetComponent<TargetMemory>(entity);
            TargetMemory* ptr = &tm;

            var entries = new JsonArray();
            for (int i = 0; i < tm.Count; i++)
            {
                var handle = new Entity((ulong)ptr->EntityIds[i]);
                if (handle.IsNull || !repo.IsAlive(handle))
                    continue;  // skip stale/null entities — not in the save map

                entries.Add(new JsonObject
                {
                    ["Entity"]   = resolver.Resolve(handle),
                    ["PosX"]     = ptr->PositionsX[i],
                    ["PosY"]     = ptr->PositionsY[i],
                    ["Score"]    = ptr->ThreatScores[i],
                    ["Tick"]     = (long)ptr->LastSeenTick[i],
                    ["Modality"] = (int)ptr->Modalities[i],
                });
            }

            return new Dictionary<string, object>
            {
                [Key] = new JsonObject { ["Entries"] = entries }
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw)) return;
            if (raw is not JsonObject obj) return;
            if (obj["Entries"] is not JsonArray entries) return;

            var intent = new InitialTargetsIntent();

            foreach (var item in entries)
            {
                if (intent.Entries.Count >= PerceptionConstants.MaxTrackedTargets) break;
                if (item is not JsonObject entry) continue;

                var guidStr = entry["Entity"]?.GetValue<string>();
                if (string.IsNullOrEmpty(guidStr)) continue;

                Entity resolved = resolver.Resolve(guidStr);
                if (resolved.IsNull || !repo.IsAlive(resolved)) continue;
                if (!repo.HasComponent<NetworkIdentity>(resolved)) continue;
                long networkId = repo.GetComponent<NetworkIdentity>(resolved).Value;

                intent.Entries.Add(new TargetEntry
                {
                    NetworkId    = networkId,
                    PosX         = entry["PosX"]?.GetValue<float>()  ?? 0f,
                    PosY         = entry["PosY"]?.GetValue<float>()  ?? 0f,
                    Score        = entry["Score"]?.GetValue<float>() ?? 0f,
                    LastSeenTick = (uint)(entry["Tick"]?.GetValue<long>()    ?? 0L),
                    Modality     = (byte)(entry["Modality"]?.GetValue<int>() ?? 0),
                });
            }

            repo.SetManagedComponent(entity, intent);
        }

        public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();
    }
}
