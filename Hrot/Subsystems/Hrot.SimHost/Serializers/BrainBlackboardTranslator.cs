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
    /// ⭐⭐ <b>The brain's diagnostic dump.</b> When the entity's active behaviour declares a
    /// <see cref="BehaviorDefinition.BlackboardLayoutType"/>, its ROOT PARAMS SLOT is projected into
    /// that typed DTO and serialised as readable JSON, alongside the <c>BrainInterrupts</c> tail.
    /// Falls back to an empty object when there is no DTO type or no params region.
    ///
    /// <para>🔴🔴 <b>THE NAME IS HISTORICAL — this type has not touched <c>BrainBlackboard</c> since
    /// <c>P3-C</c>, and that component no longer exists (<c>P4</c>, §30.28).</b> It reads
    /// <c>BehaviorState</c>, <c>BrainInterrupts</c> and the root occurrence slot. ⛔ It was NOT deleted
    /// with the component: the dump it produces is live and wanted — ⚠ **a surface whose name went
    /// stale is not a dead surface**, and deleting it would have removed a working diagnostic.
    /// ⭐ Renaming it, and its <c>"BrainBlackboard"</c> DOM key, changes a diagnostic output contract
    /// that scenario files already carry, so it is filed as <c>CE-317</c> rather than bundled here.</para>
    /// </summary>
    /// <remarks>
    /// <see cref="Inject"/> is intentionally a no-op: this is <c>DataPolicy.NoScenario</c> transient
    /// execution state and must never be written back from a scenario file. The translator exists
    /// solely to produce a readable clipboard dump via <see cref="ScenarioSerializer.SerializeEntity"/>.
    /// </remarks>
    public sealed class BrainBlackboardTranslator : IEntityScenarioTranslator
    {
        private const string Key = "BrainBlackboard";

        private readonly BehaviorRegistry _registry;

        public BrainBlackboardTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// ⭐ <c>P4</c>-③ (<c>CE-303</c>): the consumed component is <see cref="BehaviorState"/>.
        /// ⛔ It was <c>BrainBlackboard</c> — the component <see cref="Extract"/> stopped reading at
        /// <c>P3</c>-C, when the params moved to the root occurrence slot. ⚠ A mask naming a component
        /// the translator never opens is not merely stale: it is what the promotion gate arbitrates
        /// on, so it claimed ownership of bytes this translator has no opinion about.
        /// </summary>
        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(BehaviorState));
            if (id >= 0) mask.SetBit(id);
            return mask;
        }

        /// <remarks>
        /// ⭐ <c>P4</c>-③: <see cref="BehaviorState"/> alone. ⛔ Not a tier component — an entity with
        /// a brain but no params region still has a dump worth producing (the <c>BrainInterrupts</c>
        /// tail plus an empty <c>BehaviorParameters</c>), which is exactly what <see cref="Extract"/>
        /// already emits on a root-slot miss.
        /// </remarks>
        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<BehaviorState>(entity);

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

