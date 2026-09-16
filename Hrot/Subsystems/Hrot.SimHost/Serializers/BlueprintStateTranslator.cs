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
            var mask = new BitMask512();
            SetBitIfRegistered(mask, typeof(BlueprintBlackboard1024));
            SetBitIfRegistered(mask, typeof(BlueprintBlackboard4096));
            SetBitIfRegistered(mask, typeof(BlueprintBlackboard16384));
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BlueprintBlackboard1024>(entity)
            || repo.HasComponent<BlueprintBlackboard4096>(entity)
            || repo.HasComponent<BlueprintBlackboard16384>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            var dtos = new List<BlueprintAssignmentDto>();

            ExtractTier1024(repo, entity, dtos);
            ExtractTier4096(repo, entity, dtos);
            ExtractTier16384(repo, entity, dtos);

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

        private unsafe void ExtractTier1024(
            EntityRepository repo, Entity entity,
            List<BlueprintAssignmentDto> dtos)
        {
            if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard1024>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, dtos);
            }
        }

        private unsafe void ExtractTier4096(
            EntityRepository repo, Entity entity,
            List<BlueprintAssignmentDto> dtos)
        {
            if (!repo.HasComponent<BlueprintBlackboard4096>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard4096>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, dtos);
            }
        }

        private unsafe void ExtractTier16384(
            EntityRepository repo, Entity entity,
            List<BlueprintAssignmentDto> dtos)
        {
            if (!repo.HasComponent<BlueprintBlackboard16384>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard16384>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, dtos);
            }
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
