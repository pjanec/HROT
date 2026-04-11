using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hrot.SimHost.UI;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Network.Cyclone.Services;
using CycloneDDS.Runtime;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Hrot.SimHost.Integration.Tests;

[Collection("LogCapture")]
public sealed class TraceLoggingTests
{
    private const int DomainId = 11; // Use a different domain from EntityLifecycleIntegrationTests to avoid DDS state interference
    private const float Dt = 1f / 60f;
    private const int TimeoutMs = 2000;
    private const int TickSleepMs = 10;

    [Fact]
    public void SpawnVehicle_EmitsTraceSequence()
    {
        using var logScope = new LogCaptureScope();
        using var idParticipant = new DdsParticipant(DomainId);
        using var idServer = new DdsIdAllocatorServer(idParticipant);
        using var simHost = new SimHostApp();
        simHost.InitializeHeadless(domainIdOverride: DomainId);

        using var trajectoryPool = new TrajectoryPoolManager();
        using var formationTemplates = new FormationTemplateManager();
        var road = new RoadNetworkBlob();
        var scenario = new SimHostScenarioManager(simHost.World, road, trajectoryPool, formationTemplates);

        scenario.SpawnVehicle(new Vector2(100f, 200f), Vector2.UnitX, VehicleClass.Tank);

        var expected = new[]
        {
            "[Node-", // SpawnVehicle: Requesting TkbType=
            "[Node-", // ProcessSpawn: NetworkId=
            "[Node-", // Egress: Writing EntityMaster for NetID=
            "[Node-", // Egress: Writing GeoSpatial for NetID=
            "[Node-", // ELM: Entity
        };

        bool ackPublished = false;
        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalMilliseconds < TimeoutMs)
        {
            idServer.ProcessRequests();
            simHost.Tick(Dt);

            if (!ackPublished && TryGetFirstNetworkEntity(simHost.World, out var entity))
            {
                simHost.World.Bus.Publish(new ConstructionAck
                {
                    Entity = entity,
                    ModuleId = 0,
                    Success = true
                });
                ackPublished = true;
            }

            LogManager.Flush();

            if (ContainsInOrder(logScope.Target.Logs, expected))
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(
            ContainsInOrder(logScope.Target.Logs, expected),
            BuildFailureMessage(logScope.Target.Logs, expected));
    }

    private static bool TryGetFirstNetworkEntity(EntityRepository world, out Entity entity)
    {
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var e in query)
        {
            entity = e;
            return true;
        }

        entity = default;
        return false;
    }

    private static bool ContainsInOrder(IList<string> logs, IReadOnlyList<string> expected)
    {
        int index = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            string needle = expected[i];
            while (index < logs.Count && !logs[index].Contains(needle, StringComparison.Ordinal))
                index++;

            if (index >= logs.Count)
                return false;

            index++;
        }

        return true;
    }

    private static string BuildFailureMessage(IList<string> logs, IReadOnlyList<string> expected)
    {
        var message = string.Join("\n", logs);
        return "Missing expected trace sequence.\n" +
               "Expected fragments:\n" + string.Join("\n", expected) +
               "\nCaptured logs:\n" + message;
    }

    private sealed class LogCaptureScope : IDisposable
    {
        private readonly LoggingConfiguration? _originalConfig;
        public MemoryTarget Target { get; }

        public LogCaptureScope()
        {
            _originalConfig = LogManager.Configuration;
            Target = new MemoryTarget("traceCapture") { Layout = "${message}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, Target);
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public void Dispose()
        {
            LogManager.Flush();
            LogManager.Configuration = _originalConfig;
            LogManager.ReconfigExistingLoggers();
        }
    }
}
