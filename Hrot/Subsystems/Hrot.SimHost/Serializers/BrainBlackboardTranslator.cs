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
    /// When the entity has an active behavior with a <see cref="BehaviorDefinition.ParamsDtoType"/>,
    /// the raw memory block is projected into that typed DTO and serialized as a readable JSON
    /// object.  Falls back to an empty object when no DTO type is available.
    /// </summary>
    /// <remarks>
    /// <see cref="Inject"/> is intentionally a no-op: <see cref="BrainBlackboard"/> is
    /// <c>DataPolicy.NoSave</c> transient execution state and must never be written back
    /// from a scenario file.  This translator exists solely to produce a readable clipboard
    /// dump via <see cref="ScenarioSerializer.SerializeEntity"/>.
    /// </remarks>
    public sealed class BrainBlackboardTranslator : IEntityScenarioTranslator
    {
        private const string Key = nameof(BrainBlackboard);

        private readonly BehaviorRegistry _registry;

        public BrainBlackboardTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
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
            ref readonly var bb    = ref repo.GetComponentRO<BrainBlackboard>(entity);
            ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);

            var root = new JsonObject
            {
                ["ExpectedThreatLevel"] = bb.ExpectedThreatLevel,
                ["Interrupt_MobilityLost"] = bb.Interrupt_MobilityLost,
                ["Interrupt_Reserved"] = bb.Interrupt_Reserved
            };

            if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def)
                && def.ParamsDtoType != null)
            {
                fixed (byte* ptr = &bb.BehaviorParameters[0])
                {
                    object dto = Marshal.PtrToStructure((IntPtr)ptr, def.ParamsDtoType)!;
                    var mapped   = DtoDiagnosticMapper.MapObject(dto, def.ParamsDtoType, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    root["BehaviorParameters"] = JsonSerializer.SerializeToNode(mapped, FdpJsonOptionsRegistry.DefaultRelaxed) ?? new JsonObject();
                }
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
