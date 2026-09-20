using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Scenario translator for Instance Blueprint assignments on an entity.
    ///
    /// <para><b>Extract (save):</b> scans all <c>BlueprintBlackboard*</c> tiers, reads the
    /// slot table, maps each <c>BlueprintId</c> → <c>AssetId</c> via the registry, and emits
    /// a <c>"BlueprintAssignments"</c> array of <see cref="BlueprintAssignmentDto"/> objects.</para>
    ///
    /// <para><b>Inject (load):</b> for <c>"BlueprintAssignments"</c>, parses the JSON array
    /// and sets an <see cref="InitialBlueprintsIntent"/> managed component on the entity.
    /// Legacy <c>"BlueprintBlackboard1024"</c>/<c>"4096"</c>/<c>"16384"</c> keys are
    /// claimed via <see cref="GetOutputDomKeys"/> and black-holed (no-op Inject) so
    /// <c>FdpAutoSerializer</c> never sees them on old scenarios.</para>
    /// </summary>
    public sealed class BlueprintStateTranslator : IEntityScenarioTranslator
    {
        private const string OutputKey = "BlueprintAssignments";

        // ⛔⛔ O3a / B3: these are HISTORICAL SCENARIO KEYS, deliberately NOT derived from
        //   BlueprintTierTable. They name what OLD scenario files on disk actually contain, and that
        //   set is frozen by history — a tier added today was never written by an old writer, so it
        //   gets no legacy key. ⚠ Deriving them from the live ladder would be the natural-looking
        //   change and it would be wrong.
        private static readonly string[] LegacyBlackboardKeys =
        {
            "BlueprintBlackboard1024",
            "BlueprintBlackboard4096",
            "BlueprintBlackboard16384",
        };

        private readonly BlueprintRegistry? _registry;

        public BlueprintStateTranslator(BlueprintRegistry? registry)
        {
            _registry = registry;
        }

        public BitMask512 GetConsumedComponentsMask()
        {
            // ⭐ O3a / B3: the CONSUMED mask follows the live ladder — unlike LegacyBlackboardKeys
            //   above, which follows history. The two look alike and must not be merged.
            var mask = new BitMask512();
            var tiers = Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Ascending;
            for (int i = 0; i < tiers.Count; i++)
                SetBitIfRegistered(mask, tiers[i].ComponentType);
            return mask;
        }

        // A2: this predicate IS OccurrenceStoreAccess.HasStore, verbatim.
        public bool CanTranslate(EntityRepository repo, Entity entity)
            => Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.HasStore(repo, entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var dtos = new List<BlueprintAssignmentDto>();

            // A2: an entity carries AT MOST ONE tier, so the three per-tier passes were three
            // probes of which at most one could ever do work. One read-only resolution replaces them.
            // 🔴 READ-ONLY on purpose: GetComponentRW bumps the chunk version and this is a
            //    serialisation pass — see the seam's TryGetStoreReadOnly.
            ExtractFromStore(repo, entity, dtos);

            var node = JsonSerializer.SerializeToNode(dtos, FdpJsonOptionsRegistry.DefaultRelaxed);
            return new Dictionary<string, object>
            {
                [OutputKey] = node!,
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            // ⭐ QA-023 (MX-033) — the assignments value can arrive as a JsonArray (JsonNode DOM, the shape
            //    Extract emits) OR a JsonElement of kind Array (e.g. a scenario loaded through a
            //    System.Text.Json reader). Matching only JsonArray dropped the intent on the JsonElement
            //    path (Test5b, the mixed legacy+new-key case). Accept every JSON-array shape.
            if (scenarioData.TryGetValue(OutputKey, out var rawValue))
            {
                var dtos = DeserializeAssignments(rawValue);
                if (dtos != null && dtos.Count > 0)
                {
                    var intent = new InitialBlueprintsIntent();
                    intent.Blueprints.AddRange(dtos);
                    repo.SetManagedComponent(entity, intent);
                }
            }

            // Legacy keys: no-op — consumed by GetOutputDomKeys black-hole.
        }

        /// <summary>
        /// Deserializes the <c>BlueprintAssignments</c> value into DTOs regardless of the JSON shape it
        /// arrives in — a <see cref="JsonArray"/> (the DOM shape <see cref="Extract"/> emits), a
        /// <see cref="JsonElement"/> of kind Array (a reader-loaded scenario), or a raw JSON string.
        /// Returns <c>null</c> for anything that is not a JSON array (QA-023).
        /// </summary>
        private static List<BlueprintAssignmentDto>? DeserializeAssignments(object? rawValue)
        {
            var opts = FdpJsonOptionsRegistry.DefaultRelaxed;
            return rawValue switch
            {
                JsonArray jsonArray => JsonSerializer.Deserialize<List<BlueprintAssignmentDto>>(jsonArray, opts),
                JsonElement je when je.ValueKind == JsonValueKind.Array
                    => je.Deserialize<List<BlueprintAssignmentDto>>(opts),
                string s when !string.IsNullOrWhiteSpace(s)
                    => JsonSerializer.Deserialize<List<BlueprintAssignmentDto>>(s, opts),
                _ => null,
            };
        }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return OutputKey;
            foreach (var key in LegacyBlackboardKeys)
                yield return key;
        }

        // ── Extract helpers: one per tier ────────────────────────────────────

        /// <summary>
        /// A2: resolves the entity's occurrence store ONCE, read-only, and collects from it.
        /// Replaces ExtractTier1024/4096/16384, which were three copies of one probe.
        /// </summary>
        private unsafe void ExtractFromStore(
            EntityRepository repo, Entity entity,
            List<BlueprintAssignmentDto> dtos)
        {
            byte* memory = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                               .TryGetStoreReadOnly(repo, entity, out _);
            if (memory == null)
                return;

            CollectAssignments(memory, dtos);
        }

        private unsafe void CollectAssignments(
            byte* memory, List<BlueprintAssignmentDto> dtos)
        {
            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            if (header.MagicAndVersion != 0x42504257u)
                return; // Not initialized.

            int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
            for (int i = 0; i < slotCount; i++)
            {
                ref var slot = ref BlueprintBlackboardPartitions.GetSlot(memory, i);
                if (slot.BlueprintId == 0)
                    continue;

                Guid assetId = Guid.Empty;
                BlueprintDefinition? def = null;
                if (_registry != null
                    && _registry.TryGetById(slot.BlueprintId, out var d)
                    && d != null)
                {
                    assetId = d.AssetId;
                    def     = d;
                }

                // ⭐⭐ MX-031 — persist the RESOLVED PARAM BYTES (the resolver shape, not an Overrides dict:
                //    EXPLAINER §"two supply shapes"). Only when they DIFFER from InitDefault, so a
                //    default assignment stays { AssetId } only. The bytes are layout-versioned, so the
                //    def's StructureHash rides along for the load-time guard.
                byte[]? paramsBytes = null;
                ulong?  paramsHash  = null;
                if (def != null && def.ParamsSize > 0)
                {
                    byte* payload = memory + slot.PayloadOffset;
                    var live = BlueprintInstanceService.ReadParamsRegion(payload, def);
                    var dflt = BlueprintInstanceService.GetDefaultParamsRegion(def);
                    if (!live.AsSpan().SequenceEqual(dflt))
                    {
                        paramsBytes = live;
                        paramsHash  = def.StructureHash;
                    }
                }

                dtos.Add(new BlueprintAssignmentDto
                {
                    AssetId             = assetId,
                    Params              = paramsBytes,
                    ParamsStructureHash = paramsHash,
                });
            }
        }

        private static void SetBitIfRegistered(BitMask512 mask, Type componentType)
        {
            int id = ComponentTypeRegistry.GetId(componentType);
            if (id >= 0) mask.SetBit(id);
        }
    }
}
