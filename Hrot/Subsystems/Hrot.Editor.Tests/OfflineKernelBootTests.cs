using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time.Controllers;
using Hrot.CGF;
using Hrot.Core.Network;
using Hrot.Editor;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.SimHost;
using Fdp.ModuleHost;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// PACK2-C001 smoke test: verifies the offline composition root
/// can be assembled and ticked headlessly without exception.
/// </summary>
public class OfflineKernelBootTests : IDisposable
{
    private readonly EntityRepository      _world;
    private readonly ModuleHostKernel      _kernel;
    private SteppingTimeController? _stepping;

    public OfflineKernelBootTests()
    {
        _world = new EntityRepository();
        var accumulator    = new EventAccumulator();
        _kernel = new ModuleHostKernel(_world, accumulator);

        var stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
        _stepping = stepping;
        _kernel.SetTimeController(stepping);

        var entityMap        = new NetworkEntityMap();
        var doctrineRegistry = new DoctrineRegistry();
        var clusterSlave     = new ClusterSlave(0, "EditorTest");
        var fileService      = EditorBootstrap.CreateFileService();

        _kernel.RegisterModule(new SimHostCoreLogicPack(entityMap));
        _kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, entityMap, new ScenarioEntityCreationRequestSource(),
            new TacticalIntentMapperRegistry()));
        _kernel.RegisterModule(new OrchestrationLogicPack(clusterSlave));
        _kernel.RegisterModule(new ScenarioEditorModule(fileService));

        _kernel.Initialize();
    }

    public void Dispose()
    {
        _kernel.Dispose();
        _world.Dispose();
    }

    [Fact]
    public void OfflineCompositionRoot_Initializes_WithoutException()
    {
        // If we reach here, Initialize() did not throw.
        Assert.NotNull(_kernel);
    }

    [Fact]
    public void OfflineCompositionRoot_Ticks10Frames_WithoutException()
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < 10; i++)
        {
            _stepping?.Step(dt);
            _kernel.Update();
        }
        Assert.True(true); // reached without exception
    }
}
