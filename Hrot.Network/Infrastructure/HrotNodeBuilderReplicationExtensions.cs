using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.Replication;
using CycloneDDS.Runtime;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;

namespace Hrot.Network.Infrastructure;

/// <summary>
/// Intermediate builder produced by <see cref="HrotNodeBuilderReplicationExtensions.WithReplication"/>.
/// Captures the replication role and exposes <see cref="Build"/> which constructs both the
/// base <see cref="HrotNodeContext"/> and the <see cref="NedReplicationModule"/> in a single call.
///
/// <para>
/// Using a distinct return type avoids the C# rule that instance methods always beat extension
/// methods with the same name: by having <see cref="WithReplication"/> return
/// <see cref="HrotNodeBuilderWithReplication"/> instead of <see cref="HrotNodeBuilder"/>,
/// the subsequent <c>.Build()</c> call resolves to this class's own <see cref="Build"/> method
/// rather than to <see cref="HrotNodeBuilder.Build"/>.
/// </para>
/// </summary>
public sealed class HrotNodeBuilderWithReplication
{
    private readonly HrotNodeBuilder _builder;
    private readonly NodeRole        _role;
    private DoctrineRegistry?        _doctrineRegistry;

    internal HrotNodeBuilderWithReplication(HrotNodeBuilder builder, NodeRole role)
    {
        _builder = builder;
        _role    = role;
    }

    /// <summary>
    /// Injects a <see cref="DoctrineRegistry"/> that will be forwarded to
    /// <see cref="CognitiveTranslatorPack"/> so that <c>EntityMissionEgressTranslator</c>
    /// and <c>EntityMissionIngressTranslator</c> can serialise/deserialise mission plans.
    /// </summary>
    public HrotNodeBuilderWithReplication WithDoctrineRegistry(DoctrineRegistry? registry)
    {
        _doctrineRegistry = registry;
        return this;
    }

    /// <summary>
    /// Builds the node context AND constructs <see cref="NedReplicationModule"/>, returning an
    /// <see cref="HrotNodeContext"/> where <see cref="HrotNodeContext.NedReplication"/> is non-null.
    /// </summary>
    public HrotNodeContext Build()
    {
        // Calls the native HrotNodeBuilder.Build() instance method.
        var context = _builder.Build();

        // Extract the EntityLifecycleModule from BaseModules so GhostPromotionSystem (IG role)
        // uses the same instance that gets registered on the kernel.
        var elm = null as EntityLifecycleModule;
        foreach (var m in context.BaseModules)
        {
            if (m is EntityLifecycleModule e) { elm = e; break; }
        }

        var ned = new NedReplicationModule(
            participant:      context.Participant,
            role:             _role,
            entityMap:        context.EntityMap,
            geoTransform:     HrotEnvironment.CreateGeoTransform(),
            // Use world.Bus so that events published by EntityMasterIngressTranslator.ProcessDispose()
            // during the Input kernel phase are made visible to GhostDestructionSystem (PostSimulation)
            // via view.ConsumeManagedEvents<T>() after the kernel's internal Bus.SwapBuffers().
            eventBus:         context.World.Bus,
            localNodeId:      context.NodeId,
            domainId:         0,
            doctrineRegistry: _doctrineRegistry,
            tkbDb:            HrotEnvironment.CreateTkb(),
            lifecycleModule:  elm);

        return context with
        {
            NedReplication      = ned,
            GhostCreationSystem = ned.GhostCreationSystem,
        };
    }
}

/// <summary>
/// OCP-compliant extension that adds <c>.WithReplication()</c> to <see cref="HrotNodeBuilder"/>
/// without requiring <c>Hrot.Common</c> to reference <c>Hrot.Network</c>.
/// </summary>
public static class HrotNodeBuilderReplicationExtensions
{
    /// <summary>
    /// Configures NED replication for the given node role.
    /// Returns a <see cref="HrotNodeBuilderWithReplication"/> whose <see cref="HrotNodeBuilderWithReplication.Build"/>
    /// constructs both the base context and the <see cref="NedReplicationModule"/>.
    /// </summary>
    public static HrotNodeBuilderWithReplication WithReplication(
        this HrotNodeBuilder builder, NodeRole role)
        => new HrotNodeBuilderWithReplication(builder, role);

    /// <summary>
    /// Upgrades <see cref="HrotNodeContext.NedReplication"/> to use a live
    /// <paramref name="participant"/> when the context was originally built with
    /// <see cref="HrotNodeConfig.Headless"/> = <c>true</c> (null participant).
    ///
    /// <para>
    /// Use in subsystems (e.g. <c>IgApplication</c>) where the DDS participant
    /// is created in a later initialization phase than the ECS context.
    /// </para>
    /// </summary>
    public static HrotNodeContext BindReplicationParticipant(
        this HrotNodeContext context,
        NodeRole             role,
        DdsParticipant       participant,
        DoctrineRegistry?    doctrineRegistry = null)
    {
        var elm = null as EntityLifecycleModule;
        foreach (var m in context.BaseModules)
        {
            if (m is EntityLifecycleModule e) { elm = e; break; }
        }

        var ned = new NedReplicationModule(
            participant:      participant,
            role:             role,
            entityMap:        context.EntityMap,
            geoTransform:     HrotEnvironment.CreateGeoTransform(),
            eventBus:         context.World.Bus,
            localNodeId:      context.NodeId,
            domainId:         0,
            doctrineRegistry: doctrineRegistry,
            tkbDb:            HrotEnvironment.CreateTkb(),
            lifecycleModule:  elm);

        return context with
        {
            NedReplication      = ned,
            GhostCreationSystem = ned.GhostCreationSystem,
        };
    }
}
