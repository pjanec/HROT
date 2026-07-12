using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common;
using Hrot.Network.Replication;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Lifecycle;
using System.Collections.Generic;

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
    private readonly bool            _skipForNone;
    private BehaviorRegistry?        _behaviorRegistry;
    private IReadOnlyList<ITkbEntityTranslator>? _translators;

    internal HrotNodeBuilderWithReplication(HrotNodeBuilder builder, NodeRole role, bool skipForNone = false)
    {
        _builder     = builder;
        _role        = role;
        _skipForNone = skipForNone;
    }

    /// <summary>
    /// Injects a <see cref="BehaviorRegistry"/> that will be forwarded to
    /// <see cref="CognitiveTranslatorPack"/> so that <c>EntityMissionEgressTranslator</c>
    /// and <c>EntityMissionIngressTranslator</c> can serialise/deserialise mission plans.
    /// </summary>
    public HrotNodeBuilderWithReplication WithBehaviorRegistry(BehaviorRegistry? registry)
    {
        _behaviorRegistry = registry;
        return this;
    }

    /// <summary>
    /// Specifies the translator list forwarded to <see cref="NedReplicationModule"/> so that
    /// <see cref="Fdp.Toolkit.Replication.Systems.GhostPromotionSystem"/> receives the same
    /// translator instances as
    /// <see cref="Fdp.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/> and
    /// <see cref="Fdp.Toolkit.Lifecycle.Systems.BlueprintApplicationSystem"/>.
    /// </summary>
    public HrotNodeBuilderWithReplication WithTranslators(IReadOnlyList<ITkbEntityTranslator>? translators)
    {
        _translators = translators;
        return this;
    }

    /// <summary>
    /// Builds the node context AND constructs <see cref="NedReplicationModule"/>, returning an
    /// <see cref="HrotNodeContext"/> where <see cref="HrotNodeContext.NedReplication"/> is non-null.
    /// When the role is <see cref="NodeRole.None"/>, no <see cref="NedReplicationModule"/> is
    /// constructed and the returned context has <c>NedReplication = null</c>.
    /// </summary>
    public HrotNodeContext Build()
    {
        // Calls the native HrotNodeBuilder.Build() instance method.
        var context = _builder.Build();

        // NodeRole.None = headless/test node with no replication role — skip NedReplicationModule
        // construction (which throws ArgumentException for None). However, if the factory provides
        // a mock/offline IReplicationModule via CreateReplicationModule(), wire that up so that
        // tests using MockNedFactory still get a non-null context.NedReplication.
        if (_skipForNone)
        {
            var factoryModule = _builder.NetworkFactory?.CreateReplicationModule()
                                    as INedReplicationModule;
            if (factoryModule == null)
                return context;

            return context with
            {
                NedReplication      = factoryModule,
                GhostCreationSystem = factoryModule.GhostCreationSystem,
            };
        }

        // Extract the EntityLifecycleModule from BaseModules so GhostPromotionSystem (IG role)
        // uses the same instance that gets registered on the kernel.
        var elm = null as EntityLifecycleModule;
        foreach (var m in context.BaseModules)
        {
            if (m is EntityLifecycleModule e) { elm = e; break; }
        }

        var ned = new NedReplicationModule(
            participant:          context.Participant,
            role:                 _role,
            entityMap:            context.EntityMap,
            geoTransform:         HrotEnvironment.CreateGeoTransform(),
            // Use world.Bus so that events published by EntityMasterIngressTranslator.ProcessDispose()
            // during the Input kernel phase are made visible to GhostDestructionSystem (PostSimulation)
            // via view.ReadManagedEvents<T>() after the kernel's internal Bus.SwapBuffers().
            eventBus:             context.World.Bus,
            localNodeId:          context.NodeId,
            domainId:             0,
            behaviorRegistry:     _behaviorRegistry,
            tkbDb:                HrotEnvironment.CreateTkb(),
            lifecycleModule:      elm,
            tkbEntityTranslators: _translators);

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
    /// <para>
    /// When <paramref name="role"/> is <see cref="NodeRole.None"/>, returns a no-op builder that
    /// calls <see cref="HrotNodeBuilder.Build"/> without constructing a <see cref="NedReplicationModule"/>.
    /// This is the correct behaviour for headless/test nodes that have no replication role.
    /// </para>
    /// </summary>
    public static HrotNodeBuilderWithReplication WithReplication(
        this HrotNodeBuilder builder, NodeRole role)
        => new HrotNodeBuilderWithReplication(builder, role, skipForNone: role == NodeRole.None);

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
        BehaviorRegistry?    behaviorRegistry = null)
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
            behaviorRegistry: behaviorRegistry,
            tkbDb:            HrotEnvironment.CreateTkb(),
            lifecycleModule:  elm);

        return context with
        {
            NedReplication      = ned,
            GhostCreationSystem = ned.GhostCreationSystem,
        };
    }
}
