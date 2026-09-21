using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Scenario;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Scenario translator for <see cref="BrainBlackboard"/>.
    /// When the entity has an active behavior with a <see cref="BehaviorDefinition.BlackboardLayoutType"/>,
    /// the raw memory block is projected into that typed DTO and serialized as a readable JSON
    /// object.  Falls back to an empty object when no DTO type is available.
    /// </summary>
    /// <remarks>
    /// <see cref="Inject"/> is intentionally a no-op: <see cref="BrainBlackboard"/> is
    /// <c>DataPolicy.NoScenario</c> transient execution state and must never be written back
    /// from a scenario file.  This translator exists solely to produce a readable clipboard
    /// dump via <see cref="ScenarioSerializer.SerializeEntity"/>.
    /// </remarks>
    public sealed class BrainBlackboardTranslator : IEntityScenarioTranslator
    {
        private const string Key = "BrainBlackboard";

        private readonly BehaviorRegistry _registry;

        public BrainBlackboardTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(BrainBlackboard));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BrainBlackboard>(entity)
            && repo.HasComponent<BehaviorState>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);

            // ⭐ O2 (2026-09-20) — the entity-fact tail moved to BrainInterrupts. This dump keeps
            //   reporting it (R-137), now read from its own component; a brain entity always has one,
            //   but the guard keeps the dump honest if a world was built without registering it.
            var root = new JsonObject();
            if (repo.HasComponent<BrainInterrupts>(entity))
            {
                ref readonly var ints = ref repo.GetComponentRO<BrainInterrupts>(entity);
                root["ExpectedThreatLevel"]    = ints.ExpectedThreatLevel;
                root["Interrupt_MobilityLost"] = ints.Interrupt_MobilityLost;
                root["Interrupt_Reserved"]     = ints.Interrupt_Reserved;
            }

            // 🔴 P3-C (2026-09-21): the params come from the entity's ROOT PARAMS OCCURRENCE SLOT.
            //   They used to be `bb.BehaviorParameters`, a per-entity component that is now retired.
            // ⚠ TRY, not Require: this is a DUMP. An entity with no params region reports an empty
            //   object, exactly as one with no BlackboardLayoutType always has — ⛔ a diagnostic that
            //   throws is a diagnostic nobody can use to find out WHY it threw.
            if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def)
                && def.BlackboardLayoutType != null
                && RootParamsAccess.TryGetRootBytes(repo, entity, out byte* ptr))
            {
                object dto = Marshal.PtrToStructure((IntPtr)ptr, def.BlackboardLayoutType)!;
                var mapped   = DtoDiagnosticMapper.MapObject(dto, def.BlackboardLayoutType, new HashSet<object>(ReferenceEqualityComparer.Instance));
                root["BehaviorParameters"] = JsonSerializer.SerializeToNode(mapped, FdpJsonOptionsRegistry.DefaultRelaxed) ?? new JsonObject();
            }
            else
            {
                root["BehaviorParameters"] = new JsonObject();
            }

            return new Dictionary<string, object> { [Key] = root };
        }

        // No-op: BrainBlackboard is transient execution state; never loaded from scenario files.
        public void Inject(EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}

