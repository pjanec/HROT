using System.Collections.Generic;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Hrot.Map.Common;
using Hrot.Network.Replication;
using Xunit;

namespace Hrot.SimHost.Tests;

public class NedReplicationModuleTranslatorTests
{
    [Fact]
    public void NedReplicationModule_WithTranslators_ConstructsWithoutThrow()
    {
        var map     = new NetworkEntityMap();
        var bus     = new FdpEventBus();
        var translators = new List<ITkbEntityTranslator>
        {
            new VehicleKinematicsTkbTranslator(),
        }.AsReadOnly();

        var ex = Record.Exception(() =>
            new NedReplicationModule(
                participant:          null,
                role:                 NodeRole.MuscleGround,
                entityMap:            map,
                geoTransform:         HrotEnvironment.CreateGeoTransform(),
                eventBus:             bus,
                localNodeId:          1,
                domainId:             0,
                tkbEntityTranslators: translators));

        Assert.Null(ex);
    }
}
