using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;
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
            var assignments = new List<Dictionary<string, object>>();

            ExtractTier1024(repo, entity, assignments);
            ExtractTier4096(repo, entity, assignments);
            ExtractTier16384(repo, entity, assignments);

            return new Dictionary<string, object>
            {
                [OutputKey] = assignments,
            };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver)
        {
            if (scenarioData.TryGetValue(OutputKey, out var assignmentsObj))
            {
                var intent = new InitialBlueprintsIntent();

                if (assignmentsObj is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.TryGetProperty("AssetId", out var assetIdProp)
                            && assetIdProp.ValueKind == JsonValueKind.String
                            && Guid.TryParse(assetIdProp.GetString(), out var assetId))
                        {
                            intent.Blueprints.Add(new BlueprintAssignmentDto
                            {
                                AssetId = assetId,
                            });
                        }
                    }
                }

                repo.SetManagedComponent(entity, intent);
            }

            // Legacy keys: no-op — consumed by GetOutputDomKeys black-hole.
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
            List<Dictionary<string, object>> assignments)
        {
            if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard1024>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, assignments);
            }
        }

        private unsafe void ExtractTier4096(
            EntityRepository repo, Entity entity,
            List<Dictionary<string, object>> assignments)
        {
            if (!repo.HasComponent<BlueprintBlackboard4096>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard4096>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, assignments);
            }
        }

        private unsafe void ExtractTier16384(
            EntityRepository repo, Entity entity,
            List<Dictionary<string, object>> assignments)
        {
            if (!repo.HasComponent<BlueprintBlackboard16384>(entity))
                return;

            ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard16384>(entity);
            fixed (byte* memory = bb.Memory)
            {
                CollectAssignments(memory, assignments);
            }
        }

        private unsafe void CollectAssignments(
            byte* memory, List<Dictionary<string, object>> assignments)
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
                if (_registry != null
                    && _registry.TryGetById(slot.BlueprintId, out var def)
                    && def != null)
                {
                    assetId = def.AssetId;
                }

                assignments.Add(new Dictionary<string, object>
                {
                    ["AssetId"] = assetId.ToString(),
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
