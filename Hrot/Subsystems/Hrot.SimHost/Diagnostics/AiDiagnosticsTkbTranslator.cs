using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common.Components;

namespace Hrot.SimHost.Diagnostics
{
    /// <summary>
    /// TKB observer translator that auto-enables per-entity AI tracing during entity
    /// genesis when the <see cref="GlobalDebugSettings.AutoEnableAiTracing"/> flag is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Observer pattern</b>: <see cref="GetConsumedDescriptors"/> returns an empty array,
    /// so this translator never claims a descriptor; it runs alongside <c>BehaviorTkbTranslator</c>
    /// (which IS the descriptor consumer) and only inspects the brain tier to stamp the
    /// appropriate trace buffer + <see cref="DebugState"/>.
    /// </para>
    /// <para>
    /// <b>Project location</b>: this lives in <c>Hrot.SimHost</c> because it depends on
    /// <see cref="GlobalDebugSettings"/> from <c>Hrot.Common</c>. <c>Fdp.Toolkits</c> does not
    /// reference <c>Hrot.Common</c>, so an FDP-layer translator could not read the flag.
    /// </para>
    /// </remarks>
    public sealed class AiDiagnosticsTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            // Singleton check: skip entirely when the flag is off or absent.
            if (!repo.HasSingletonUnmanaged<GlobalDebugSettings>()) return;
            if (!repo.GetSingletonUnmanaged<GlobalDebugSettings>().AutoEnableAiTracing) return;

            var profile = template.GetDescriptor<BehaviorProfileDto>();
            if (profile == null) return;

            byte tier = profile.BrainTier;
            if (tier != BehaviorConstants.BrainTierBTree && tier != BehaviorConstants.BrainTierHsm)
                return;

            // Stamp the matching trace buffer component.
            if (tier == BehaviorConstants.BrainTierBTree
                && repo.IsComponentTypeRegistered<BTreeTraceWorkingMemory1024>()
                && !repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
            {
                repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());
            }
            else if (tier == BehaviorConstants.BrainTierHsm
                && repo.IsComponentTypeRegistered<HsmTraceWorkingMemory1024>()
                && !repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
            {
                repo.AddComponent(entity, new HsmTraceWorkingMemory1024());
            }

            // Stamp DebugState with EnableTraceBuffer set. Preserve any pre-existing bits.
            if (!repo.IsComponentTypeRegistered<DebugState>()) return;
            if (!repo.HasComponent<DebugState>(entity))
            {
                repo.AddComponent(entity, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
            }
            else
            {
                ref var state = ref repo.GetComponentRW<DebugState>(entity);
                state.Behavior |= BehaviorDebugFlags.EnableTraceBuffer;
            }
        }
    }
}
