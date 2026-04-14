using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.IG;
using Hrot.ScenarioEditor.Adapters;
using Hrot.IG.Components;
using Hrot.Map.Common;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D.Abstractions;
using NLog;
using NLog.Config;
using NLog.Targets;
using Raylib_cs;

namespace Hrot.IG.Tests;

[Collection("LogCapture")]
public sealed class TraceLoggingTests : IDisposable
{
    private const int DomainId = 10;
    private const float Dt = 1f / 60f;
    private const int TimeoutMs = 3000;
    private const int TickSleepMs = 10;

    private readonly IgApplication _ig;

    public TraceLoggingTests()
    {
        _ig = new IgApplication();
        // Factory with a real participant so NedReplicationModule creates DDS readers for EntityMaster/WorldPos.
        _ig.InitializeEmbedded(headless: true, domainIdOverride: DomainId, networkFactory: IgTestFactory.CreateHeadless(DomainId));
    }

    public void Dispose()
    {
        _ig.Shutdown(ownsWindow: false);
    }

    [Fact]
    public void IngressAndRender_EmitsTraceLines()
    {
        using var logScope = new LogCaptureScope();
        using var participant = new DdsParticipant(DomainId);
        using var masterWriter = new DdsWriter<EntityMaster>(participant, "EntityMaster");
        using var geoWriter = new DdsWriter<WorldPos>(participant, "GeoSpatial");

        const long networkId = 9001;

        masterWriter.Write(new EntityMaster
        {
            EntityId = (int)networkId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            DisType = default
        });

        var expected = new[]
        {
            "[Node-", // Ingress: EntityMaster NetID=
            "[Node-", // Ingress: GeoSpatial Entity=
            "[Node-", // Style: Resolved Entity=
            "[Node-", // Render: Drawing Entity=
        };

        bool geoSent = false;
        bool renderAttempted = false;
        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalMilliseconds < TimeoutMs)
        {
            _ig.Update(Dt);
            LogManager.Flush();

            if (!renderAttempted && TryGetEntity(_ig.World, networkId, out var entity))
            {
                if (!geoSent)
                {
                    geoWriter.Write(new WorldPos
                    {
                        EntityId = (int)networkId,
                        Pos = new GeoPoint { Latitude = 52.52, Longitude = 13.405, Altitude = 0 },
                        Ori = new EulerOri()
                    });
                    geoSent = true;
                }

                if (_ig.World.HasComponent<ResolvedStyle>(entity)
                    && _ig.World.HasComponent<SimTransform>(entity))
                {
                    TryRenderOnce(_ig.World, entity);
                    renderAttempted = true;
                }
            }

            if (ContainsAll(logScope.Target.Logs, expected))
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(
            ContainsAll(logScope.Target.Logs, expected),
            BuildFailureMessage(logScope.Target.Logs, expected));
    }

    private static bool TryGetEntity(EntityRepository world, long networkId, out Entity entity)
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

    private static void TryRenderOnce(EntityRepository world, Entity entity)
    {
        var adapter = new NedVisualizerAdapter();
        NedVisualizerAdapter.RenderTraceEntityIdOverride = entity.Index;

        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(1, 1, "TraceRender");

        try
        {
            var position = world.GetComponent<SimTransform>(entity).Position;
            var ctx = new RenderContext
            {
                Camera = new Camera2D
                {
                    Target = new Vector2(position.X, position.Y),
                    Offset = Vector2.Zero,
                    Rotation = 0f,
                    Zoom = 1f
                },
                MouseWorldPos = Vector2.Zero,
                DeltaTime = 0f,
                VisibleLayersMask = uint.MaxValue,
                Resources = new NullResourceProvider()
            };

            Raylib.BeginDrawing();
            Raylib.BeginMode2D(ctx.Camera);
            adapter.Render(world, entity, new Vector2(position.X, position.Y), ctx, isSelected: false, isHovered: false);
            Raylib.EndMode2D();
            Raylib.EndDrawing();
        }
        finally
        {
            Raylib.CloseWindow();
            NedVisualizerAdapter.RenderTraceEntityIdOverride = null;
        }
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

    private sealed class NullResourceProvider : IResourceProvider
    {
        public T? Get<T>() where T : class => null;
        public bool Has<T>() where T : class => false;
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
