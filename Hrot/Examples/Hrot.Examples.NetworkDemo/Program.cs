using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF;
using Hrot.Common;
using Hrot.Core.Network;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using Hrot.Network.NED.Factory;
using Hrot.SimHost;

// SDK Onboarding QuickStart: Distributed CQRS "Patrol and Engage" Demo.
//
// Usage:
//   Hrot.Examples.NetworkDemo 100 Brain
//   Hrot.Examples.NetworkDemo 200 MuscleGround
//
// Run both processes against the same DDS loopback domain (domainId = 0) to
// observe the Brain/Muscle split-authority pattern described in the SDK guide.

namespace Hrot.Examples.NetworkDemo;

public static class Program
{
    public static void Main(string[] args)
    {
        int      nodeId = args.Length > 0 ? int.Parse(args[0]) : 100;
        NodeRole role   = args.Length > 1 && args[1] == "MuscleGround"
            ? NodeRole.MuscleGround | NodeRole.Perception
            : NodeRole.Brain;

        int domainId = 0; // CycloneDDS loopback domain for local testing.

        Console.WriteLine($"[NetworkDemo] Starting node {nodeId} as {role} on DDS domain {domainId}");

        // ── 1. External Network Adapter (Hexagonal Architecture: Adapter layer) ──
        //
        // The DDS participant is the outermost infrastructure boundary.
        // Only the composition root (this file) may instantiate DdsParticipant.
        using var participant = HrotEnvironment.CreateParticipant(domainId);

        var networkFactory = new NedNetworkFactory(
            participant:  participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  nodeId,
            role:         role);

        // ── 2. Infrastructure Boundary: build the node context via HrotNodeBuilder ──
        //
        // HrotNodeBuilder wires the ECS world, kernel, event bus, ClusterSlave, and
        // base infrastructure modules (EntityLifecycleModule, GeographicModule).
        // .WithReplication(role) appends NedReplicationModule to the context so that
        // NED DDS translator packs are available without touching the domain logic.
        var config = new HrotNodeConfig
        {
            DomainId          = domainId,
            NodeId            = nodeId,
            SubsystemName     = "NetworkDemo",
            Headless          = false,
            ExternalParticipant = participant,
        };

        HrotNodeContext context = new HrotNodeBuilder(config)
            .WithRole("NetworkDemo", role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .Build();

        // ── 3. Domain Logic Registration (Ports) ──
        //
        // Logic packs are pure domain modules. They have zero knowledge of DDS
        // topology, wire formats, or network addresses.

        var doctrineRegistry = new DoctrineRegistry();
        DemoScenarioSetup.RegisterDoctrines(doctrineRegistry);

        if (role.HasFlag(NodeRole.Brain))
        {
            // Brain role: cognitive decision-making (BTree/HSM, MissionControl,
            // ActionDispatch). Never runs kinematics or reads SimTransform directly.
            var cgfPack = new CgfLogicPack(doctrineRegistry, context.EntityMap,
                new ScenarioEntityCreationRequestSource());
            context.Kernel.RegisterModule(cgfPack);
        }

        if (role.HasFlag(NodeRole.MuscleGround))
        {
            // Muscle role: physics and geometry solver (CarKinematics, Combat,
            // DamageAssessment, AutonomousPerception). Never evaluates BTreeContext.
            var simPack = new SimHostCoreLogicPack(context.EntityMap);
            context.Kernel.RegisterModule(simPack);
        }

        // ── 4. Base infrastructure and NED replication module ──
        foreach (var baseModule in context.BaseModules)
            context.Kernel.RegisterModule(baseModule);

        if (context.NedReplication != null)
            context.Kernel.RegisterModule(context.NedReplication);

        // ── 5. Initialize and optionally spawn scenario entities ──
        context.Kernel.Initialize();

        if (role.HasFlag(NodeRole.Brain))
            DemoScenarioSetup.SpawnEntities(context);

        Console.WriteLine("[NetworkDemo] Running. Press Ctrl+C to stop.");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // ── 6. Main loop ──
        //
        // In production each subsystem manages its own tick via SubsystemOrchestrator.
        // This standalone demo drives the kernel directly to keep the composition root
        // as a single self-contained file.
        while (!cts.IsCancellationRequested)
        {
            context.SlaveTranslator?.Tick();
            context.ClusterSlave.Tick();
            context.Kernel.Update(0.016f);
            context.EventBus.SwapBuffers();
            Thread.Sleep(16);
        }

        Console.WriteLine("[NetworkDemo] Shutting down.");
    }
}
