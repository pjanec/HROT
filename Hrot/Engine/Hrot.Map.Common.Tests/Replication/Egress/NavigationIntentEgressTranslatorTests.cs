using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Descriptors;
using Xunit;

using EcsNavigationIntent = Fdp.Toolkit.Navigation.NavigationIntent;
using EcsNavMode          = Fdp.Toolkit.Navigation.NavigationMode;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// End-to-end correctness and performance guard for
/// <see cref="NavigationIntentEgressTranslator"/>.
///
/// Key contract under test:
///   1. A new navigation command (incremented IntentId) is published exactly once.
///   2. Identical frames with no command change produce zero DDS publishes.
///   3. An entity adjacent in memory to the changed one (chunk-level false positive)
///      is NOT published unless its own IntentId changed.
///   4. QueryDelta provides the coarse filter: a totally-unchanged entity is never
///      yielded, so zero dictionary lookups occur for it.
/// </summary>
public sealed class NavigationIntentEgressTranslatorTests
{
    // ── Test infrastructure ────────────────────────────────────────────────────

    private sealed class CapturingWriter : IDdsWriter<NavigationIntent>
    {
        public List<NavigationIntent> Publishes { get; } = new();
        public void Write(NavigationIntent sample) => Publishes.Add(sample);
        public void DisposeInstance(NavigationIntent key) { }
    }

    private sealed class IdentityGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }
        public Vector3 ToCartesian(double lat, double lon, double alt) => new Vector3((float)lat, (float)lon, (float)alt);
        public (double lat, double lon, double alt) ToGeodetic(Vector3 pos) => (pos.X, pos.Y, pos.Z);
    }

    private static EntityRepository CreateWorld()
    {
        ComponentTypeRegistry.Clear();
        var world = new EntityRepository();
        world.RegisterComponent<EcsNavigationIntent>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkAuthority>();
        return world;
    }

    private static (NavigationIntentEgressTranslator translator, CapturingWriter writer) CreateTranslator()
    {
        var writer     = new CapturingWriter();
        var entityMap  = new NetworkEntityMap();
        var geoXform   = new IdentityGeoTransform();
        var translator = new NavigationIntentEgressTranslator(writer, entityMap, geoXform, localNodeId: 1);
        return (translator, writer);
    }

    private static Entity SpawnAuthoritativeEntity(EntityRepository world, uint netId)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new NetworkIdentity(netId));
        world.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        world.AddComponent(e, new EcsNavigationIntent());
        return e;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A new MoveToExecutor command (IntentId 0 -> 1, Mode = DirectPoint) must
    /// be published exactly once.
    /// </summary>
    [Fact]
    public void NewCommand_PublishedExactlyOnce()
    {
        using var world = CreateWorld();
        var (translator, writer) = CreateTranslator();

        var entity = SpawnAuthoritativeEntity(world, netId: 42);

        // Frame 1: entity spawned, Mode still None -- nothing to publish.
        world.Tick();
        translator.ScanAndPublish(world);
        Assert.Empty(writer.Publishes);

        // Frame 2: executor writes new command.
        world.Tick();
        world.SetComponent(entity, new EcsNavigationIntent
        {
            IntentId         = 1,
            Mode             = EcsNavMode.DirectPoint,
            FinalDestination = new Vector2(100f, 200f),
            TargetSpeed      = 15f,
            ArrivalRadius    = 5f,
        });
        translator.ScanAndPublish(world);

        Assert.Single(writer.Publishes);
        Assert.Equal(1u, writer.Publishes[0].IntentId);
        Assert.Equal(ENavigationMode.NAV_DIRECT_POINT, writer.Publishes[0].Mode);
    }

    /// <summary>
    /// With no component mutations after the initial publish, every subsequent
    /// ScanAndPublish must produce zero DDS writes.
    /// </summary>
    [Fact]
    public void NoChange_ZeroPublishesOnSubsequentScans()
    {
        using var world = CreateWorld();
        var (translator, writer) = CreateTranslator();

        var entity = SpawnAuthoritativeEntity(world, netId: 7);

        // Issue the first command.
        world.Tick();
        world.SetComponent(entity, new EcsNavigationIntent
        {
            IntentId = 1,
            Mode     = EcsNavMode.DirectPoint,
        });
        translator.ScanAndPublish(world);
        Assert.Single(writer.Publishes); // first publish

        // Simulate 5 subsequent frames with no mutations.
        for (int frame = 0; frame < 5; frame++)
        {
            world.Tick();
            translator.ScanAndPublish(world);
        }

        // Still only 1 publish total.
        Assert.Single(writer.Publishes);
    }

    /// <summary>
    /// When a second command arrives (IntentId 1 -> 2), it must be published again.
    /// This is the critical regression test for the SmartEgressUtil bug that silently
    /// dropped all commands after the first one (ShouldPublish returned false because
    /// no executor called MarkDirty).
    /// </summary>
    [Fact]
    public void SecondCommand_PublishedWhenIntentIdChanges()
    {
        using var world = CreateWorld();
        var (translator, writer) = CreateTranslator();

        var entity = SpawnAuthoritativeEntity(world, netId: 99);

        // Command 1.
        world.Tick();
        world.SetComponent(entity, new EcsNavigationIntent { IntentId = 1, Mode = EcsNavMode.DirectPoint });
        translator.ScanAndPublish(world);
        Assert.Single(writer.Publishes);

        // No-op frames.
        world.Tick();
        translator.ScanAndPublish(world);
        world.Tick();
        translator.ScanAndPublish(world);

        // Command 2 (new order from executor).
        world.Tick();
        world.SetComponent(entity, new EcsNavigationIntent { IntentId = 2, Mode = EcsNavMode.DirectPoint,
            FinalDestination = new Vector2(500f, 600f) });
        translator.ScanAndPublish(world);

        Assert.Equal(2, writer.Publishes.Count);
        Assert.Equal(2u, writer.Publishes[1].IntentId);
    }

    /// <summary>
    /// An entity sitting in the same 64KB memory chunk as the changed entity
    /// (chunk-level false positive from QueryDelta) must NOT be published if its
    /// own IntentId has not changed.
    /// </summary>
    [Fact]
    public void ChunkFalsePositive_AdjacentEntityNotPublished()
    {
        using var world = CreateWorld();
        var (translator, writer) = CreateTranslator();

        // Spawn two entities. Both land in the same EntityHeader chunk because
        // indices are allocated contiguously starting from 0.
        var changedEntity   = SpawnAuthoritativeEntity(world, netId: 1);
        var unchangedEntity = SpawnAuthoritativeEntity(world, netId: 2);

        // Give unchanged entity an active intent first so it would be published
        // if the filter were not working.
        world.Tick();
        world.SetComponent(unchangedEntity, new EcsNavigationIntent { IntentId = 1, Mode = EcsNavMode.DirectPoint });
        translator.ScanAndPublish(world); // publishes unchangedEntity once

        int publishedAfterSetup = writer.Publishes.Count;

        // Now only mutate changedEntity. QueryDelta yields the whole chunk
        // (both entities), but only changedEntity has a new IntentId.
        world.Tick();
        world.SetComponent(changedEntity, new EcsNavigationIntent { IntentId = 1, Mode = EcsNavMode.DirectPoint });
        translator.ScanAndPublish(world);

        // Exactly one more publish -- for changedEntity only.
        Assert.Equal(publishedAfterSetup + 1, writer.Publishes.Count);
        Assert.Equal(1, writer.Publishes[^1].EntityId); // changedEntity has netId=1
    }

    /// <summary>
    /// An entity with Mode = None must never be published, even if its chunk was
    /// dirtied and its IntentId has not been recorded.
    /// </summary>
    [Fact]
    public void ModeNone_NeverPublished()
    {
        using var world = CreateWorld();
        var (translator, writer) = CreateTranslator();

        var entity = SpawnAuthoritativeEntity(world, netId: 5);

        // Write a Mode=None intent (e.g., MoveToExecutor.OnExit).
        world.Tick();
        world.SetComponent(entity, new EcsNavigationIntent { IntentId = 3, Mode = EcsNavMode.None });
        translator.ScanAndPublish(world);

        Assert.Empty(writer.Publishes);
    }
}
