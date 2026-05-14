using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Template-method base class that locks the 7-phase node initialization order,
/// eliminating duplication across SimHost, IG, and StrideMock.
///
/// <para>
/// Subclasses implement the abstract hooks listed below. The sealed
/// <see cref="BootstrapNode"/> method orchestrates the phases in the correct order;
/// it may not be overridden.
/// </para>
///
/// <para>
/// Phase order (non-negotiable):
/// 1. Build HrotNodeContext via HrotNodeBuilder + configure network factory.
/// 2. RegisterDomainComponents — abstract hook.
/// 3. BuildSerializer — abstract hook.
/// 4a. PopulateSystems + create TogglableGroups + register on kernel.
/// 4b. GetAdditionalModules — virtual hook, register each module.
/// 5. BuildOrchestration — abstract hook, returns ClusterSlave.
/// 6a. RegisterSpawningPipeline — abstract hook.
/// 6a+. Register NedReplicationModule — base class, NOT a hook.
/// 6b. RegisterNetworkTranslators — abstract hook.
/// 6c. Wire time-sync translators — base class, NOT a hook.
/// 7. Kernel.Initialize() — always last.
/// </para>
/// </summary>
public abstract class SharedApplicationBootstrapper
{
    /// <summary>
    /// Gateway for forwarding UI time-control commands (pause/step/play) to the
    /// Cluster Master over DDS. Set by the base class in Phase 6c via the
    /// CONFIGURED factory — not the raw input factory whose event bus is
    /// disconnected from the kernel. Non-null after a successful
    /// <see cref="BootstrapNode"/> call; null only when bootstrapping with a
    /// factory that returns null from <c>CreateTimeControlGateway()</c>.
    /// </summary>
    public ITimeControlGateway? TimeControl { get; private set; }

    /// <summary>
    /// Runs the 7-phase initialization pipeline and returns the fully wired
    /// <see cref="HrotNodeContext"/> with <see cref="ModuleHostKernel"/> initialized.
    /// </summary>
    public HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory? networkFactory)
    {
        // Phase 1 — Build core context via subclass hook to guarantee .WithReplication is called.
        var context = BuildContext(config, role, networkFactory);

        INetworkFactory? configuredFactory = networkFactory?.ConfigureForNode(context, role, GetBehaviorRegistry());

        // Phase 2 — Register domain ECS components BEFORE the serializer is built.
        RegisterDomainComponents(context.World);

        // Phase 3 — Build the scenario serializer (abstract: HrotScenarioSerializerFactory
        // lives in Hrot.SimHost, above Hrot.Common in the dependency graph).
        var serializer = BuildSerializer(GetBehaviorRegistry());

        // Phase 4a — Populate togglable system groups and register them on the kernel.
        var inputSystems   = new List<IEcsModuleSystem>();
        var simSystems     = new List<IEcsModuleSystem>();
        var postSimSystems = new List<IEcsModuleSystem>();
        PopulateSystems(context, inputSystems, simSystems, postSimSystems);

        var inputGroup   = new TogglableInputGroup($"{config.SubsystemName}Input",   inputSystems);
        var simGroup     = new TogglableSimulationGroup($"{config.SubsystemName}Sim", simSystems);
        var postSimGroup = new TogglablePostSimulationGroup($"{config.SubsystemName}PostSim", postSimSystems);

        // TogglableInputGroup and TogglablePostSimulationGroup use [UpdateInPhase] attributes
        // that the kernel resolves automatically via RegisterGlobalSystem.
        context.Kernel.RegisterGlobalSystem(inputGroup);
        // TogglableSimulationGroup targets SystemPhase.Simulation, which RegisterGlobalSystem
        // rejects. Wrap it in a private IEcsModule and register via RegisterModule instead.
        context.Kernel.RegisterModule(new SimulationGroupModule(simGroup));
        context.Kernel.RegisterGlobalSystem(postSimGroup);

        // Phase 4b — Additional modules whose internal phase structure must not be flattened.
        foreach (IEcsModule mod in GetAdditionalModules())
            context.Kernel.RegisterModule(mod);

        // Phase 5 — Build orchestration handlers (abstract: NodeBootstrapper lives in Hrot.SimHost).
        ClusterSlave slave = BuildOrchestration(context, simGroup, postSimGroup, serializer);
        context = context with { ClusterSlave = slave };

        // Phase 6a — Register base modules (EntityLifecycleModule + GeographicModule) and
        // the domain spawning pipeline.
        foreach (IEcsModule m in context.BaseModules)
            context.Kernel.RegisterModule(m);

        RegisterSpawningPipeline(context);

        // Phase 6a+ — Register NedReplicationModule — base class ONLY, NOT a subclass hook.
        // Activates GhostCreationSystem, DeadReckoningSyncSystem, and ownership egress systems.
        // Subclasses must NOT call RegisterModule(context.NedReplication) — double-registration
        // corrupts the system schedule.
        if (context.NedReplication != null)
            context.Kernel.RegisterModule(context.NedReplication);

        // Phase 6b — Domain-specific DDS translators (hook).
        RegisterNetworkTranslators(context, configuredFactory ?? networkFactory);

        // Phase 6c — Wire time-sync translators — base class ONLY, NOT a subclass hook.
        // CreateDescriptorTranslator/CreateSlaveLockstepTranslator/CreateSlaveTimeSyncTranslator
        // all accept a null participant and become safe no-ops in that case (headless / test mode).
        // Registered unconditionally so the SlaveSyncController is always reachable via the event
        // bus even when no DDS participant is present.
        var timeSyncTranslators = new IDescriptorTranslator[]
        {
            TimeNetworkModule.CreateDescriptorTranslator(context.Participant, context.EventBus),
            TimeNetworkModule.CreateSlaveLockstepTranslator(context.Participant, context.EventBus, context.NodeId),
            TimeNetworkModule.CreateSlaveTimeSyncTranslator(context.Participant, context.EventBus, context.NodeId),
        };

        var ingress = new List<IDescriptorTranslator>();
        var egress  = new List<IDescriptorTranslator>();
        foreach (var t in timeSyncTranslators)
        {
            if ((t.Direction & TranslatorDirection.Ingress) != 0) ingress.Add(t);
            if ((t.Direction & TranslatorDirection.Egress)  != 0) egress.Add(t);
        }
        if (ingress.Count > 0)
            context.Kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(ingress.ToArray()));
        if (egress.Count > 0)
            context.Kernel.RegisterGlobalSystem(new CycloneEgressSystem(egress.ToArray()));
        context.Kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(timeSyncTranslators));

        // TimeControl is set from the CONFIGURED factory (not the raw input factory).
        // The raw factory's event bus is an unbound shell — gateways built from it publish
        // ClusterOpRequest messages into the void and the cluster clock ignores all UI commands.
        TimeControl = configuredFactory?.CreateTimeControlGateway();

        // Phase 6d — Application-level systems (virtual, defaults to no-op).
        // Override to register gizmo modules, UI capture systems, or any other systems that
        // must be part of the initialized kernel topology but are not part of the domain core.
        RegisterApplicationSystems(context);

        // Phase 7 — Initialize kernel. Always last.
        context.Kernel.Initialize();

        return context;
    }

    // ── Abstract hooks (must be implemented by subclasses) ───────────────────

    /// <summary>
    /// Phase 1: Constructs the initial HrotNodeContext. Subclasses must chain
    /// .WithReplication(role) before calling .Build() to properly provision network replication.
    ///
    /// <para>
    /// This hook is necessary because Hrot.Common cannot reference Hrot.Network.NED
    /// (circular dependency), but concrete subclasses can. By delegating context
    /// construction to the subclass, we ensure that .WithReplication() is available
    /// and properly applied.
    /// </para>
    /// </summary>
    protected abstract HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory);

    /// <summary>
    /// Phase 2: Register all ECS component types needed by this node.
    /// Called before <see cref="BuildSerializer"/> so that the serializer
    /// can reference the registered component tables.
    /// </summary>
    protected abstract void RegisterDomainComponents(EntityRepository world);

    /// <summary>
    /// Phase 3: Build the scenario serializer.
    /// Abstract because <c>HrotScenarioSerializerFactory</c> lives in
    /// <c>Hrot.SimHost</c>, above <c>Hrot.Common</c> in the dependency
    /// hierarchy — referencing it from this base class would create a
    /// circular dependency.
    /// </summary>
    protected abstract ScenarioSerializer BuildSerializer(BehaviorRegistry? registry);

    /// <summary>
    /// Phase 4a: Populate the three togglable system lists.
    /// Systems added to <paramref name="sim"/> are placed inside a
    /// <see cref="TogglableSimulationGroup"/> that is suspended during replay.
    /// </summary>
    protected abstract void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim);

    /// <summary>
    /// Phase 5: Build orchestration handlers.
    /// Abstract because <c>NodeBootstrapper</c> lives in <c>Hrot.SimHost</c>.
    /// The implementation MUST pass
    /// <c>lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup</c>
    /// to <c>NodeBootstrapper.BuildOrchestration</c> to prevent
    /// <c>GhostDestructionSystem</c> from firing against the flight recorder
    /// during <c>PrepareReplay</c>.
    /// </summary>
    protected abstract ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer);

    /// <summary>
    /// Phase 6a: Register the spawn pipeline (entity lifecycle, sensor feed, etc.).
    /// Called after base modules and before network translators.
    /// </summary>
    protected abstract void RegisterSpawningPipeline(HrotNodeContext context);

    /// <summary>
    /// Phase 6b: Register domain-specific DDS translators (entity state, combat, etc.).
    /// The <paramref name="configuredFactory"/> is the result of
    /// <c>networkFactory.ConfigureForNode(context...)</c> — NOT the raw input factory.
    /// May be <c>null</c> when no factory was provided (headless / offline mode).
    /// </summary>
    protected abstract void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory);

    // ── Virtual hooks (subclasses may override) ──────────────────────────────

    /// <summary>
    /// Phase 4b: Supply additional <see cref="IEcsModule"/> instances whose
    /// internal phase structure must not be flattened into the togglable groups.
    /// Default returns an empty sequence.
    /// </summary>
    protected virtual IEnumerable<IEcsModule> GetAdditionalModules()
        => Array.Empty<IEcsModule>();

    /// <summary>
    /// Returns the behavior registry used by the serializer and the network
    /// factory configuration. Default returns null (no behaviors registered).
    /// </summary>
    protected virtual BehaviorRegistry? GetBehaviorRegistry() => null;

    /// <summary>
    /// Phase 6d: Called after all translator registrations and before Phase 7 (Initialize).
    /// Override to register application-level systems (e.g. gizmo modules, UI capture systems)
    /// that must be part of the initialized kernel topology but are not part of the domain core.
    /// Default is a no-op.
    /// </summary>
    protected virtual void RegisterApplicationSystems(HrotNodeContext context) { }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// IEcsModule wrapper that routes <see cref="TogglableSimulationGroup"/> into
    /// the Simulation phase slot.
    /// <c>RegisterGlobalSystem</c> rejects <see cref="SystemPhase.Simulation"/>; it
    /// must be registered via <c>RegisterModule</c> instead.
    /// </summary>
    private sealed class SimulationGroupModule : IEcsModule
    {
        private readonly TogglableSimulationGroup _group;

        public string Name => _group.Name;
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        public SimulationGroupModule(TogglableSimulationGroup group) => _group = group;

        public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_group);
        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
