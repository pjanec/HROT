using System;
using System.Collections.Generic;
using System.Linq;
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
/// eliminating duplication across SimHost, IG, and the Stride-hosted node (ST-014: the StrideMock
/// subsystem that was the third consumer is retired; StrideNodeBootstrapper now lives in
/// Hrot.NodeComposition).
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
    /// <summary>The providers this node allocated, in allocation order. Freed by <see cref="DisposeResources"/>.</summary>
    private IReadOnlyList<INodeResourceProvider> _resourceProviders = Array.Empty<INodeResourceProvider>();

    /// <summary>The running plan's values. Null until the <c>node-resources</c> step runs.</summary>
    private NodeBootValues? _bootValues;

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
        // ⭐⭐⭐ THE PHASES ARE DECLARED, NOT JUST SEQUENCED (2026-09-03).
        //
        // The order below is EXACTLY what it has always been — NodeBootPlan verifies, it never
        // sorts, so this is behaviour-preserving by construction. What is new is that each
        // phase now states what it REQUIRES and what it PROVIDES, and the runner checks it.
        //
        // ⛔ Why that matters: §4.1N measured three real dependencies here that travel through
        //    channels no signature shows, and ALL THREE FAIL SILENTLY —
        //      ① a subclass field: SimHost's CoreLogicPack/RoadNetwork, written in 4a and read
        //         in 6a/6b (SimHostNodeBootstrapper:203/:201 -> :350/:358/:385);
        //      ② a context field mutated by a registration side effect: GhostCreationSystem is
        //         null at build (HrotNodeBuilder:215) and populated by registering
        //         NedReplicationModule in 6a+, then read in 6b;
        //      ③ a global static snapshot: FdpAutoSerializer.Build():93 FREEZES its entry table
        //         from ComponentTypeRegistry, so a component registered after phase 3 is
        //         silently absent from serialization.
        //    None of those throws today if the order is broken. Declared, they do.
        //
        // ⚠ The keys are not decoration: moving a phase now fails with the missing key NAMED.
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1P (design + UML), §4.1N (the graph).
        HrotNodeContext           context           = default!;
        INetworkFactory?          configuredFactory = null;
        ScenarioSerializer        serializer        = default!;
        TogglableSimulationGroup  simGroup          = default!;
        TogglablePostSimulationGroup postSimGroup   = default!;
        ClusterSlave              slave             = default!;

        var plan = new NodeBootPlan();

        // Phase 1 — Build core context via subclass hook to guarantee .WithReplication is called.
        plan.Step("context", provides: new[] { "context" }, run: () =>
        {
            context = BuildContext(config, role, networkFactory);
        });

        plan.Step("configured-factory", requires: new[] { "context" }, provides: new[] { "configured-factory" }, run: () =>
        {
            configuredFactory = networkFactory?.ConfigureForNode(context, role, GetBehaviorRegistry());
        });

        // Phase 2 — Register domain ECS components BEFORE the serializer is built.
        plan.Step("domain-components", requires: new[] { "context" }, provides: new[] { "domain-components" }, run: () =>
        {
            RegisterDomainComponents(context.World);
        });

        // Phase 3 — Build the scenario serializer (abstract: HrotScenarioSerializerFactory
        // lives in Hrot.SimHost, above Hrot.Common in the dependency graph).
        // ⛔ REQUIRES domain-components: FdpAutoSerializer.Build() freezes its entry table from
        //    ComponentTypeRegistry, so anything registered later is silently unserializable.
        plan.Step("serializer", requires: new[] { "domain-components" }, provides: new[] { "serializer" }, run: () =>
        {
            serializer = BuildSerializer(GetBehaviorRegistry());
        });

        // Phase 3b — Allocate the node's one-per-world RESOURCES, before any capability registers
        // a system that borrows one.
        //
        // ⭐⭐⭐ CE-199 — THIS STEP IS WHY INodeResourceProvider.Allocate CAN RUN AT ALL.
        //
        // NodeBootValues.Set refuses any write outside a step that declared the key in its
        // `provides`, so a provider allocating into a free-standing bag THROWS AT BOOT — measured on
        // a rail, not guessed (NodeCompositionPlanRails.AllocatingIntoAFreeStandingBagIsRefused).
        // The resource half of the seam therefore could not run until the allocation moved INSIDE a
        // declaring step. This is that step.
        //
        // ⚠ WHY TWO HOOKS AND NOT ONE. `provides` is recorded when the step is DECLARED, which is
        // before BuildContext has run — but a host's providers come from its NodeCompositionPlan,
        // and SimHost cannot build that plan until it has the context and the loaded road network
        // (SimHostNodeBootstrapper's own comment says so). So the KEYS are declared from the role
        // alone (DeclaredResourceKeys), and the INSTANCES are resolved inside the step
        // (ResolveResources). That split is the design's, not a workaround for it: §4.1j axis ①
        // says a capability declares "a list of resource keys, not instances".
        //
        // ⛔ The two are then CHECKED against each other below, so the declaration cannot drift from
        //    what is actually allocated — the same guarantee NodeBootPlan gives for its own graph.
        string[] declaredResourceKeys = DeclaredResourceKeys(role).Distinct(StringComparer.Ordinal).ToArray();

        plan.Step("node-resources",
            requires: new[] { "context" },
            provides: declaredResourceKeys,
            run: values =>
            {
                _bootValues       = values;
                _resourceProviders = ResolveResources(context, role);

                var allocated = new HashSet<string>(StringComparer.Ordinal);
                foreach (INodeResourceProvider provider in _resourceProviders)
                {
                    if (Array.IndexOf(declaredResourceKeys, provider.Key) < 0)
                        throw new InvalidOperationException(
                            $"[{config.SubsystemName}] resolved a provider for resource '{provider.Key}', which " +
                            $"DeclaredResourceKeys(role) did not declare [{string.Join(", ", declaredResourceKeys)}]. " +
                            "The boot step only permits writes to keys it declared, so this allocation would be " +
                            "refused. Declare the key, or stop resolving the provider.");

                    provider.Allocate(context, values);
                    allocated.Add(provider.Key);
                }

                foreach (string declared in declaredResourceKeys)
                {
                    if (!allocated.Contains(declared))
                        throw new InvalidOperationException(
                            $"[{config.SubsystemName}] declared resource '{declared}' but no resolved provider " +
                            "allocated it. A declared key that nobody publishes is how a consumer ends up reading " +
                            "a resource that was never created — declare only what the role really needs.");
                }
            });

        // Phase 4a — Populate togglable system groups and register them on the kernel.
        // ⛔ PROVIDES system-groups, which 6a and 6b require: a subclass may build a capability
        //    pack here and read it back in those phases (SimHost's CoreLogicPack does exactly that).
        // ⭐ REQUIRES the declared resource keys, so PopulateSystems may legally read an allocated
        //    resource out of BootValues — the read is checked against this step's own declaration.
        plan.Step("system-groups",
            requires: new[] { "context" }.Concat(declaredResourceKeys).ToArray(),
            provides: new[] { "system-groups" },
            run: () =>
        {
            var inputSystems   = new List<IEcsModuleSystem>();
            var simSystems     = new List<IEcsModuleSystem>();
            var postSimSystems = new List<IEcsModuleSystem>();
            PopulateSystems(context, inputSystems, simSystems, postSimSystems);

            var inputGroup = new TogglableInputGroup($"{config.SubsystemName}Input", inputSystems);
            simGroup       = new TogglableSimulationGroup($"{config.SubsystemName}Sim", simSystems);
            postSimGroup   = new TogglablePostSimulationGroup($"{config.SubsystemName}PostSim", postSimSystems);

            // TogglableInputGroup and TogglablePostSimulationGroup use [UpdateInPhase] attributes
            // that the kernel resolves automatically via RegisterGlobalSystem.
            context.Kernel.RegisterGlobalSystem(inputGroup);
            // TogglableSimulationGroup targets SystemPhase.Simulation, which RegisterGlobalSystem
            // rejects. Wrap it in a private IEcsModule and register via RegisterModule instead.
            context.Kernel.RegisterModule(new SimulationGroupModule(simGroup));
            context.Kernel.RegisterGlobalSystem(postSimGroup);
        });

        // Phase 4b — Additional modules whose internal phase structure must not be flattened.
        // ⭐ Measured incidental (§4.1N ③): nothing consumes it; its only constraint is "before 7".
        plan.Step("additional-modules", requires: new[] { "context" }, run: () =>
        {
            foreach (IEcsModule mod in GetAdditionalModules())
                context.Kernel.RegisterModule(mod);
        });

        // Phase 5 — Build orchestration handlers (abstract: NodeBootstrapper lives in Hrot.SimHost).
        plan.Step("orchestration",
            requires: new[] { "serializer", "system-groups" },
            provides: new[] { "cluster-slave" },
            run: () =>
            {
                slave   = BuildOrchestration(context, simGroup, postSimGroup, serializer);
                context = context with { ClusterSlave = slave };
            });

        plan.Step("slave-invariant", requires: new[] { "cluster-slave" }, run: () =>
        {
            AssertSlaveComposition(context, slave, networkFactory);
        });

        // Phase 6a — Register base modules (EntityLifecycleModule + GeographicModule) and
        // the domain spawning pipeline.
        // ⛔ REQUIRES system-groups — see the Phase 4a note (the subclass-field channel).
        plan.Step("spawning-pipeline", requires: new[] { "system-groups" }, run: () =>
        {
            foreach (IEcsModule m in context.BaseModules)
                context.Kernel.RegisterModule(m);

            RegisterSpawningPipeline(context);
        });

        // Phase 6a+ — Register NedReplicationModule — base class ONLY, NOT a subclass hook.
        // Activates GhostCreationSystem, DeadReckoningSyncSystem, and ownership egress systems.
        // Subclasses must NOT call RegisterModule(context.NedReplication) — double-registration
        // corrupts the system schedule.
        // ⛔ PROVIDES ned-replication: registering it is what populates context.GhostCreationSystem.
        plan.Step("ned-replication", requires: new[] { "context" }, provides: new[] { "ned-replication" }, run: () =>
        {
            if (context.NedReplication != null)
                context.Kernel.RegisterModule(context.NedReplication);
        });

        // Phase 6b — Domain-specific DDS translators (hook).
        // ⛔ REQUIRES ned-replication: SimHost reads context.GhostCreationSystem, which is null
        //    until 6a+ registered the module that populates it.
        plan.Step("network-translators",
            requires: new[] { "ned-replication", "system-groups", "configured-factory" },
            run: () =>
            {
                RegisterNetworkTranslators(context, configuredFactory ?? networkFactory);
            });

        // Phase 6c — Wire time-sync translators — base class ONLY, NOT a subclass hook.
        // CreateDescriptorTranslator/CreateSlaveLockstepTranslator/CreateSlaveTimeSyncTranslator
        // all accept a null participant and become safe no-ops in that case (headless / test mode).
        // Registered unconditionally so the SlaveSyncController is always reachable via the event
        // bus even when no DDS participant is present.
        // ⭐ Measured incidental (§4.1N ③): needs only Phase-1 values.
        plan.Step("time-sync", requires: new[] { "context", "configured-factory" }, run: () =>
        {
            SlaveTimeTranslatorRegistration.RegisterOn(
                context.Kernel, context.Participant, context.EventBus, context.NodeId);

            // TimeControl is set from the CONFIGURED factory (not the raw input factory).
            // The raw factory's event bus is an unbound shell — gateways built from it publish
            // ClusterOpRequest messages into the void and the cluster clock ignores all UI commands.
            TimeControl = configuredFactory?.CreateTimeControlGateway();
        });

        // Phase 6d — Application-level systems (virtual, defaults to no-op).
        // Override to register gizmo modules, UI capture systems, or any other systems that
        // must be part of the initialized kernel topology but are not part of the domain core.
        // ⭐ Measured incidental (§4.1N ③).
        plan.Step("application-systems", requires: new[] { "context" }, run: () =>
        {
            RegisterApplicationSystems(context);
        });

        // Phase 7 — Initialize kernel. Always last.
        // ⛔ ENFORCED by the kernel itself: ModuleHostKernel:165-166 throws
        //    "Cannot register systems after Initialize() called".
        plan.Step("kernel-initialize", requires: new[] { "context" }, provides: new[] { "kernel-initialized" }, run: () =>
        {
            context.Kernel.Initialize();
        });

        // Phase 7+ — Post-initialize hook (virtual, defaults to no-op).
        // Called after Kernel.Initialize() so that providers that require
        // RegisterSystems to have run (e.g. EngineBackedNavigationModule.RegisterProviders
        // which needs _navmesh/_registry created by RegisterSystems) can be wired here.
        // ⛔ ENFORCED by the module: EngineBackedNavigationModule:63-65 throws
        //    "Call RegisterSystems before RegisterProviders."
        plan.Step("post-initialize", requires: new[] { "kernel-initialized" }, run: () =>
        {
            PostInitialize(context);
        });

        plan.Run(GetType().Name);

        return context;
    }

    /// <summary>
    /// Phase 5-post — the slave-node composition invariant. Extracted verbatim from the inline
    /// body when the phases became a declared plan; the assertions and their reasoning are
    /// unchanged.
    /// </summary>
    private void AssertSlaveComposition(HrotNodeContext context, ClusterSlave slave, INetworkFactory? networkFactory)
    {

        // ⭐⭐⭐ Phase 5-post — CE-164: THE SLAVE-NODE COMPOSITION INVARIANT, checked rather than trusted.
        //
        // BuildOrchestration is `abstract`, so this base mandates THAT each node wires orchestration and
        // shares NONE of the doing. Three subclasses write it three ways; nothing structurally bound them
        // to the ONE bus HrotNodeBuilder Step 8 already put this node's complete
        // ISlaveOrchestrationTranslator on.
        //
        // 📐 Measured 2026-09-03, four-process cluster: IG built its ClusterSlave on a second FdpEventBus
        //    of its own and ticked a bare, ingress-only translator there, while the shared egress-capable
        //    one sat unticked on context.EventBus. `POST /scenario/load/live` on the IG port answered
        //    ok/"cluster-intent" and the cluster never moved. SILENT — the intent was published, swapped,
        //    and read by nothing.
        //
        // ⇒ Both halves are asserted here because they fail in opposite directions and only together do
        //   they mean "this node's control plane is actually connected":
        //     (a) the slave must publish on THE node bus — catches a second bus;
        //     (b) a node with a DDS participant must HAVE a slave translator — catches a node that never
        //         took one, which no bus check can see.
        //
        // ⛔ Deliberately a THROW, not a log: a control plane that is wired but inert is exactly the class
        //    of failure that survives a whole test run looking healthy.
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1b.
        // ⚠ No `!= null` guard: HrotNodeContext.EventBus is `required FdpEventBus`, i.e. non-nullable by
        //   contract — and adding one made the compiler treat it as nullable at its later uses.
        //   ⭐ Unconditional is also the stronger assertion.
        if (!slave.PublishesOn(context.EventBus))
            throw new InvalidOperationException(
                $"[{GetType().Name}] BuildOrchestration returned a ClusterSlave that does not publish on " +
                "HrotNodeContext.EventBus. Every networked slave node must put its ClusterSlave and its " +
                "ISlaveOrchestrationTranslator on the ONE bus HrotNodeBuilder created, or control-plane " +
                "intents (TransitionStateIntent, SetTimeScaleIntent, ...) are published where nothing " +
                "drains them and the node fails silently. Build the slave on context.EventBus (CE-164).");

        // ⚠⚠ The `networkFactory != null` term is LOAD-BEARING, not defensive — it mirrors
        //    HrotNodeBuilder Step 8's OWN condition (`participant != null && _networkFactory != null`).
        //    📐 Measured: without it this threw on 55 Hrot.IG.Tests + 5 Hrot.SimHost.Tests cases, all of
        //    which call InitializeEmbedded(headless: true) with NO factory — a legitimate configuration
        //    that has a participant and correctly has no translator. ⛔ An assertion that fires on a valid
        //    shape gets deleted within a batch; this one has to be exactly as strong as the builder.
        if (context.Participant != null && networkFactory != null && context.SlaveTranslator == null)
            throw new InvalidOperationException(
                $"[{GetType().Name}] This node has a DDS participant and a network factory, but " +
                "HrotNodeContext.SlaveTranslator is null, so nothing drains its orchestration bus to " +
                "DDS. It is built by HrotNodeBuilder Step 8 via " +
                "INetworkFactory.CreateSlaveOrchestratorTranslators — check that BuildContext chains " +
                ".WithNetworkFactory(...), do not hand-build a replacement, and make sure the node ticks " +
                "this one (CE-164).");
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
    /// Phase 3b, part 1 — the resource keys this node's <paramref name="role"/> will allocate.
    /// Default is none.
    /// </summary>
    /// <remarks>
    /// <para>⭐ This is the DECLARATION half. It is read when the boot step is built — before
    /// <c>BuildContext</c> — so it must depend on nothing but the role. Return one of
    /// <see cref="ResourceKeys"/> per resource.</para>
    ///
    /// <para>⛔ It is checked against <see cref="ResolveResources"/>: a provider whose key was not
    /// declared, or a declared key nobody allocates, throws at boot with the key named. So the
    /// declaration cannot quietly drift from what the node really does.</para>
    /// </remarks>
    protected virtual IReadOnlyList<string> DeclaredResourceKeys(NodeRole role)
        => Array.Empty<string>();

    /// <summary>
    /// Phase 3b, part 2 — the provider INSTANCES that satisfy <see cref="DeclaredResourceKeys"/>.
    /// Default is none.
    /// </summary>
    /// <remarks>
    /// Called inside the <c>node-resources</c> step, so the context exists and each provider's
    /// <see cref="INodeResourceProvider.Allocate"/> may publish into the step's
    /// <see cref="NodeBootValues"/>. Typically <c>plan.RequiredResources(role)</c> — the union of the
    /// selected capabilities' declared needs, and nothing else.
    /// </remarks>
    protected virtual IReadOnlyList<INodeResourceProvider> ResolveResources(HrotNodeContext context, NodeRole role)
        => Array.Empty<INodeResourceProvider>();

    /// <summary>
    /// The values published by the boot plan's steps. Available to a subclass hook that runs INSIDE
    /// a step which declared the key it reads.
    /// </summary>
    /// <remarks>
    /// ⭐ <c>system-groups</c> requires every declared resource key, so <c>PopulateSystems</c> may
    /// read an allocated resource from here and the read is checked. ⛔ Not an ambient bag: outside a
    /// step, or for an undeclared key, <see cref="NodeBootValues.Get{T}"/> throws.
    /// </remarks>
    protected NodeBootValues BootValues =>
        _bootValues ?? throw new InvalidOperationException(
            "BootValues is only available once the boot plan is running. A hook that needs a resource " +
            "must run inside a step that declares it — see the 'node-resources' step.");

    /// <summary>
    /// Frees every resource this node allocated, in reverse allocation order.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ ONE implementation, on the base. Both SimHost and IG had grown their own identical copy of
    /// this loop (<c>CE-197</c>) — two implementations of one concept, which is exactly what the
    /// composition work exists to remove. A host now inherits it, and a host that allocates nothing
    /// frees nothing.
    ///
    /// <para>📐 It is not optional book-keeping: before <c>CE-197</c> nothing disposed
    /// <c>TrajectoryPoolProvider</c>, despite that class's own remarks claiming a provider's lifetime
    /// is the node's, so every node leaked its <c>TrajectoryPoolManager</c>.</para>
    /// </remarks>
    public void DisposeResources()
    {
        for (int i = _resourceProviders.Count - 1; i >= 0; i--)
            _resourceProviders[i].Dispose();

        _resourceProviders = Array.Empty<INodeResourceProvider>();
    }

    /// <summary>
    /// Phase 6d: Called after all translator registrations and before Phase 7 (Initialize).
    /// Override to register application-level systems (e.g. gizmo modules, UI capture systems)
    /// that must be part of the initialized kernel topology but are not part of the domain core.
    /// Default is a no-op.
    /// </summary>
    protected virtual void RegisterApplicationSystems(HrotNodeContext context) { }

    /// <summary>
    /// Phase 7+: Called immediately after <see cref="ModuleHostKernel.Initialize()"/> completes.
    /// Override to register providers or perform setup that requires <c>RegisterSystems</c>
    /// to have already run (e.g. <c>EngineBackedNavigationModule.RegisterProviders</c>
    /// which needs <c>_navmesh</c>/<c>_registry</c> created during <c>RegisterSystems</c>).
    /// Default is a no-op.
    /// </summary>
    protected virtual void PostInitialize(HrotNodeContext context) { }

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
