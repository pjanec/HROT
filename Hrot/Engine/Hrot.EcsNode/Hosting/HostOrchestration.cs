using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Map.Common.ClusterLoad;
using Hrot.SimHost;

namespace Hrot.EcsNode.Hosting
{
    /// <summary>
    /// ⭐ <c>CE-4270</c> B11 (<c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1ag): what differs between hosts in the orchestration step, as VALUES.
    /// Everything the node's context already holds (the participant, the bus, the features, the TKB, the lifecycle group, the ghost system, the accumulator,
    /// the seek callback, the ELM, the terrain content, the ONE slave and translator, the entity map) is passed by <see cref="HostOrchestration"/> for every
    /// host and is not here.
    /// </summary>
    public sealed record OrchestrationSpec
    {
        /// <summary>The node's label (the slave's host label, the load log's).</summary>
        public required string SubsystemName { get; init; }
        /// <summary>The staging root the load and save steps read and write.</summary>
        public string? LocalTempRoot { get; init; }
        /// <summary>The node id the handlers use, when it is not the context's (IG's effective instance id).</summary>
        public int? NodeId { get; init; }
        public Fdp.Toolkit.Scenario.ScenarioSerializer? Serializer { get; init; }

        /// <summary>The groups a replay, a preview or a pause toggles. ⚠ Not every host passes every group today (§4.1ag B11, the drift table).</summary>
        public TogglableSimulationGroup? SimGroup { get; init; }
        public TogglableInputGroup? InputGroup { get; init; }
        public TogglablePostSimulationGroup? PostSimGroup { get; init; }

        /// <summary>The cluster diagnostics dump. ⚠ Not on every host today (§4.1ag B11).</summary>
        public Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler? DiagnosticsDump { get; init; }

        /// <summary>A Brain host's scenario capture: the extractor. With it, the scenario is read from the world's bus and the context's id allocator.</summary>
        public IScenarioEntityExtractor? ScenarioExtractor { get; init; }
        public IReadOnlyList<IClusterStateHandler>? ScenarioHandlers { get; init; }
        public Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? BehaviorRemapper { get; init; }

        /// <summary>The offline node (the editor): no network half — no participant, lifecycle group, ghost system, seek callback or slave translator.</summary>
        public bool Offline { get; init; }
        public bool DryRunIncludesTransient { get; init; }
        public IReadOnlyList<LoadPart>? ProfileLoadParts { get; init; }
        public Func<IMapBuildStatus?>? MapBuildStatus { get; init; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4270</c> B11: the ONE place a host builds its orchestration (its cluster slave and the handlers on it). Before it, six hosts each wrote the same
    /// ~20-argument <c>NodeBootstrapper.BuildOrchestration</c> call by hand. The values the context holds were passed by some and forgotten by others (the
    /// silent-default shape): CGF and the editor passed the entity map, the other four did not.
    /// </summary>
    public static class HostOrchestration
    {
        /// <summary>Builds the node's orchestration through <paramref name="bootstrapper"/> for <paramref name="role"/>.</summary>
        public static ClusterSlave Build(NodeBootstrapper bootstrapper, NodeRole role, HrotNodeContext context, OrchestrationSpec spec)
        {
            ArgumentNullException.ThrowIfNull(bootstrapper);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(spec);
            bool networked = !spec.Offline;
            bool capturesScenario = spec.ScenarioExtractor != null;

            return bootstrapper.BuildOrchestration(
                role, context.Kernel, context.World, spec.NodeId ?? context.NodeId,
                participant:             networked ? context.Participant : null,
                subsystemName:           spec.SubsystemName,
                eventBus:                context.EventBus,
                features:                () => context.Features,   // ⭐ P-7 F-28 — the scenario step hands it the header's preset
                scenarioSerializer:      spec.Serializer,
                scenarioExtractor:       spec.ScenarioExtractor,
                scenarioSource:          capturesScenario ? context.World.Bus : null,
                scenarioIdAllocator:     capturesScenario ? context.IdAllocator : null,
                localTempRoot:           spec.LocalTempRoot,
                tkbDb:                   context.TkbDb,
                simGroup:                spec.SimGroup,
                inputGroup:              spec.InputGroup,
                postSimGroup:            spec.PostSimGroup,
                lifecycleGroup:          networked ? context.NedReplication?.NetworkLifecycleGroup : null,
                ghostCreationSystem:     networked ? context.GhostCreationSystem : null,
                eventAccumulator:        context.EventAccumulator,
                afterSeek:               networked ? context.NedReplication?.AfterSeekCallback : null,
                diagnosticsDumpHandler:  spec.DiagnosticsDump,
                elm:                     context.BaseModules?.OfType<EntityLifecycleModule>().FirstOrDefault(),
                terrainContent:          context.TerrainContent,
                clusterSlave:            context.ClusterSlave,   // ⭐ CE-4013 D15 — the node's ONE slave and translator
                slaveTranslator:         networked ? context.SlaveTranslator : null,
                scenarioHandlers:        spec.ScenarioHandlers,
                behaviorRemapper:        spec.BehaviorRemapper,
                offline:                 spec.Offline,
                entityMap:               context.EntityMap,      // CE-2101 — the world boundary clears it (the same map the world's singleton holds)
                dryRunIncludesTransient: spec.DryRunIncludesTransient,
                profileLoadParts:        spec.ProfileLoadParts,
                mapBuildStatus:          spec.MapBuildStatus);
        }
    }
}
