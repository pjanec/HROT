using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using Bagira.Orchestrator;
using Bagira.SimHost;
using CycloneDDS.Runtime;
using ModuleHost.Network.Cyclone.Services;
using Xunit;

namespace Bagira.SimHost.Integration.Tests;

/// <summary>CGF1-S0103 — centralized <see cref="DdsIdAllocatorServer"/> on orchestrator.</summary>
[Collection("LogCapture")]
public sealed class DdsIdAllocatorMigrationTests
{
    [Fact]
    public async Task SimHostReceivesIdFromOrchestratorServer()
    {
        using var cancel = new CancellationTokenSource();
        using var orchParticipant = BagiraEnvironment.CreateParticipant(0);
        using var drill = new DrillMaster(orchParticipant);

        var pump = Task.Run(() =>
        {
            while (!cancel.IsCancellationRequested)
            {
                drill.Tick();
                Thread.Sleep(1);
            }
        });

        await Task.Delay(500);

        var cfg = new NodeConfiguration
        {
            DdsDomainId = 0,
            IdAllocatorLocalFallbackEnabled = false,
            IdAllocatorLocalFallbackDelaySeconds = 5
        };

        var app = new SimHostApp(0, NodeRole.AllInOne, cfg);
        try
        {
            app.InitializeHeadless(0);
            var id = app.TestHook_SpawnEntity(1, new GeoPosition { Latitude = 32.0, Longitude = 34.0, Altitude = 0 });
            Assert.True(id > 0);
        }
        finally
        {
            cancel.Cancel();
            await pump;
            app.Shutdown();
        }

        AssertSimHostAppHasNoDdsIdAllocatorServerField();
    }

    private static void AssertSimHostAppHasNoDdsIdAllocatorServerField()
    {
        var fields = typeof(SimHostApp).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            Assert.NotEqual(typeof(DdsIdAllocatorServer), f.FieldType);
        }
    }
}
