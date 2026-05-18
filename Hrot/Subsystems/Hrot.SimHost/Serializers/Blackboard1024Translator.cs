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
    /// Scenario translator for <see cref="Blackboard1024"/>.
    /// When the entity has an active behavior with a <see cref="BehaviorDefinition.HeavyDtoType"/>,
    /// the raw 1024-byte memory block is projected into that typed DTO and serialized as a
    /// readable JSON object.  Falls back to an empty object when no DTO type is registered.
    /// </summary>
    /// <remarks>
    /// <see cref="Inject"/> is intentionally a no-op: <see cref="Blackboard1024"/> is
    /// <c>DataPolicy.NoSave</c> transient execution state and must never be written back
    /// from a scenario file.  This translator exists solely to produce a readable clipboard
    /// dump via <see cref="ScenarioSerializer.SerializeEntity"/>.
    /// </remarks>
    public sealed class Blackboard1024Translator : IEntityScenarioTranslator
    {
        private const string Key = nameof(Blackboard1024);

        private readonly BehaviorRegistry _registry;

        public Blackboard1024Translator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            int id = ComponentTypeRegistry.GetId(typeof(Blackboard1024));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<Blackboard1024>(entity)
            && repo.HasComponent<BehaviorState>(entity);

        public unsafe Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver resolver)
        {
            ref readonly var bb    = ref repo.GetComponentRO<Blackboard1024>(entity);
            ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);

            if (_registry.TryGetDefinition(state.ActiveBehaviorHash, out var def)
                && def.HeavyDtoType != null)
            {
                fixed (byte* ptr = &bb.Memory[0])
                {
                    object dto = Marshal.PtrToStructure((IntPtr)ptr, def.HeavyDtoType)!;
                    var mapped   = DtoDiagnosticMapper.MapObject(dto, def.HeavyDtoType, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    JsonNode? node = JsonSerializer.SerializeToNode(mapped, FdpJsonOptionsRegistry.DefaultRelaxed);
                    return new Dictionary<string, object> { [Key] = node ?? new JsonObject() };
                }
            }

            return new Dictionary<string, object> { [Key] = new JsonObject() };
        }

        // No-op: Blackboard1024 is transient execution state; never loaded from scenario files.
        public void Inject(EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver resolver) { }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}
