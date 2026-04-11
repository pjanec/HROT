using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using Hrot.Map.Common;
using Hrot.Map.Common.Replication.Egress;
using Hrot.SimHost.Network;
using Hrot.SimHost.Network.Egress;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.SimHost.Translators;

/// <summary>
/// Composite <see cref="IEcsModule"/> that groups all outbound (IG → SimHost) egress
/// translators into a single hot-plug unit.
///
/// <para>
/// Registers a single <see cref="CycloneEgressSystem"/> carrying all five translators:
/// <list type="bullet">
///   <item><see cref="NavigationIntentEgressTranslator"/></item>
///   <item><see cref="WeaponFireIntentEgressTranslator"/></item>
///   <item><see cref="SpawnEntityCommandEgressTranslator"/></item>
///   <item><see cref="UpdateEntityCommandEgressTranslator"/></item>
///   <item><see cref="DestroyEntityCommandEgressTranslator"/></item>
/// </list>
/// </para>
/// </summary>
public class ActuatorIntentsEgressPack : IEcsModule
{
    public string Name => "ActuatorIntentsEgress";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    private readonly IDescriptorTranslator[] _translators;

    public ActuatorIntentsEgressPack(
        PackRole role,
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform,
        FdpEventBus eventBus,
        long localNodeId = 0)
    {
        if (role != PackRole.Egress)
            throw new ArgumentException(
                $"ActuatorIntentsEgressPack must be constructed with PackRole.Egress, got {role}.",
                nameof(role));
        _translators = new IDescriptorTranslator[]
        {
            new NavigationIntentEgressTranslator(participant, entityMap, geoTransform, localNodeId),
            new WeaponFireIntentEgressTranslator(participant, entityMap),
            new SpawnEntityCommandEgressTranslator(participant, eventBus, geoTransform),
            new UpdateEntityCommandEgressTranslator(participant, eventBus, entityMap, geoTransform),
            new DestroyEntityCommandEgressTranslator(participant, eventBus),
        };
    }

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new CycloneEgressSystem(_translators));
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
