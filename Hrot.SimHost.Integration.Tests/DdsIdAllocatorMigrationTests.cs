using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Core.Mission;
using Hrot.Map.Common;
using Hrot.SimHost;
using CycloneDDS.Runtime;
using ModuleHost.Network.Cyclone.Services;
using Xunit;

namespace Hrot.SimHost.Integration.Tests;

/// <summary>CGF1-S0103 — centralized <see cref="DdsIdAllocatorServer"/> on orchestrator.</summary>
[Collection("LogCapture")]
public sealed class DdsIdAllocatorMigrationTests
{
    [Fact]
    public async Task SimHostReceivesIdFromOrchestratorServer()
    {
        using var cancel = new CancellationTokenSource();
        using var orchParticipant = HrotEnvironment.CreateParticipant(0);
        using var exercise = new DdsIdAllocatorServer(orchParticipant);

        var pump = Task.Run(() =>
        {
            while (!cancel.IsCancellationRequested)
            {
                exercise.ProcessRequests();
                Thread.Sleep(1);
            }
        });

        await Task.Delay(500);

        var cfg = new NodeConfiguration
        {
            DdsDomainId = 0,
        };

        var app = new SimHostApp(0, NodeRole.MuscleGround | NodeRole.Perception, cfg);
        try
        {
            app.InitializeHeadless(0);
            var id = app.TestHook_SpawnEntity(1, new GeoPoint { Latitude = 32.0, Longitude = 34.0, Altitude = 0 });
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
