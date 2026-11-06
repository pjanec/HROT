using Hrot.NED.Messages;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Player;
using Hrot.Map.Common.Components;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Hrot.Player.Tests;

[Collection("LogCapture")]
public sealed class TraceLoggingTests : IDisposable
{
    private const int DomainId = 10;
    private const float Dt = 1f / 60f;
    private const int TimeoutMs = 3000;
    private const int TickSleepMs = 10;

    private readonly PlayerApplication _player;

    public TraceLoggingTests()
    {
        _player = new PlayerApplication();
        // Factory with a real participant so NedReplicationModule creates DDS readers for EntityMaster/WorldPos.
        _player.InitializeEmbedded(headless: true, domainIdOverride: DomainId, networkFactory: PlayerTestFactory.CreateHeadless(DomainId));
    }

    public void Dispose()
    {
        _player.Shutdown(ownsWindow: false);
    }

    [Fact]
    public void IngressAndRender_EmitsTraceLines()
    {
        using var logScope = new LogCaptureScope();
        using var participant = new DdsParticipant(DomainId);
        using var masterWriter = new DdsWriter<EntityMaster>(participant);
        using var geoWriter = new DdsWriter<WorldPos>(participant);

        const int networkId = 9001;

        masterWriter.Write(new EntityMaster
        {
            EntityId = networkId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            DisType = default
        });

        var expected = new[]
        {
            "[Node-", // Ingress: EntityMaster NetID=
            "[Node-", // Ingress: GeoSpatial Entity=
            "[Node-", // Style: Resolved Entity=
        };

        bool geoSent = false;
        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalMilliseconds < TimeoutMs)
        {
            _player.Update(Dt);
            LogManager.Flush();

            if (!geoSent && TryGetEntity(_player.World, networkId, out var entity))
            {
                geoWriter.Write(new WorldPos
                {
                    EntityId = networkId,
                    Position = new Vec3d { X = 100, Y = 200, Z = 0 }   // AQ107 B: local ENU
                });
                geoSent = true;
            }

            if (ContainsAll(logScope.Target.Logs, expected))
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(
            ContainsAll(logScope.Target.Logs, expected),
            BuildFailureMessage(logScope.Target.Logs, expected));
    }

    private static bool TryGetEntity(EntityRepository world, int networkId, out Entity entity)
    {
        var query = world.Query().With<NetworkIdentity>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var e in query)
        {
            if (world.GetComponent<NetworkIdentity>(e).Value == networkId)
            {
                entity = e;
                return true;
            }
        }

        entity = default;
        return false;
    }

    private static bool ContainsAll(IList<string> logs, IReadOnlyList<string> expected)
    {
        for (int i = 0; i < expected.Count; i++)
        {
            bool found = false;
            for (int j = 0; j < logs.Count; j++)
            {
                if (logs[j].Contains(expected[i], StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static string BuildFailureMessage(IList<string> logs, IReadOnlyList<string> expected)
    {
        var message = string.Join("\n", logs);
        return "Missing expected trace logs.\n" +
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
