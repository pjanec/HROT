using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Modules;

namespace Hrot.Map.Common.Translators;

/// <summary>
/// Composite <see cref="IEcsModule"/> that groups all inbound (SimHost → IG) ingress
/// translators required for a complete 2D operational picture into a single hot-plug unit.
///
/// <para>
/// Registers a single <see cref="CycloneNetworkIngressSystem"/> carrying all six translators:
/// <list type="bullet">
///   <item><see cref="EntityMasterIngressTranslator"/></item>
///   <item><see cref="GeoSpatialIngressTranslator"/></item>
///   <item><see cref="EntityInfoIngressTranslator"/></item>
///   <item><see cref="MapVisualOverlayIngressTranslator"/></item>
///   <item><see cref="MapRouteIngressTranslator"/></item>
///   <item><see cref="EntityDamageIngressTranslator"/></item>
/// </list>
/// </para>
/// </summary>
public class EntityStatesIngressPack : IEcsModule
{
    public string Name => "EntityStatesIngress";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly IDescriptorTranslator[] _translators;

    public EntityStatesIngressPack(
        PackRole role,
        DdsParticipant? participant,
        NetworkEntityMap entityMap,
        long localNodeId,
        FdpEventBus eventBus,
        GhostCreationSystem ghostCreationSystem,
        IGeographicTransform geoTransform)
    {
        if (role != PackRole.Ingress)
            throw new ArgumentException(
                $"EntityStatesIngressPack must be constructed with PackRole.Ingress, got {role}.",
                nameof(role));
        _translators = new IDescriptorTranslator[]
        {
            new EntityMasterIngressTranslator(participant, entityMap, localNodeId, eventBus, ghostCreationSystem),
            new GeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem, localNodeId),
            new EntityInfoIngressTranslator(participant, entityMap, eventBus, ghostCreationSystem, localNodeId),
            new MapVisualOverlayIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem, localNodeId),
            new MapRouteIngressTranslator(participant, entityMap, geoTransform),
            new EntityDamageIngressTranslator(participant, entityMap, ghostCreationSystem, localNodeId),
        };
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new CycloneNetworkIngressSystem(_translators));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
