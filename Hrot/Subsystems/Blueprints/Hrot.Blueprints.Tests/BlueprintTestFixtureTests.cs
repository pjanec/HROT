using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Mocks;
using FdpBlueprintDispatchKind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests;

[Collection("DebugProbe")]
public sealed class BlueprintTestFixtureTests
{
    // SC1: Constructor initializes all properties
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        using var fixture = new BlueprintTestFixture();
        Assert.NotNull(fixture.World);
        Assert.NotNull(fixture.View);
        Assert.NotNull(fixture.Ecb);
        Assert.NotNull(fixture.Registry);
        Assert.NotNull(fixture.TickSystem);
        Assert.NotNull(fixture.MaintenanceSystem);
        Assert.NotNull(fixture.Compiler);
        Assert.NotNull(fixture.DebugSession);
        // DebugProbe.Sink wired to DebugSession
        Assert.Same(fixture.DebugSession, DebugProbe.Sink);
    }

    // SC2: PublishEvent -> TickFrame -> ReadEvents (via Patch 1: FdpEventBus SwapBuffers)
    [Fact]
    public void PublishEvent_ViaBus_ReadableInNextTickFrame()
    {
        using var fixture = new BlueprintTestFixture();

        // Publish into the bus (will be readable after SwapBuffers in TickFrame)
        fixture.World.Bus.Publish(new HitEvent { Target = new Entity(1, 0), Damage = 30f });

        IReadOnlyList<HitEvent>? captured = null;
        fixture.RegisterTickAction((view, _) =>
        {
            captured = view.ReadEvents<HitEvent>().ToArray();
        });

        fixture.TickFrame(0.016f);

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Count);
        Assert.Equal(30f, captured[0].Damage);

        // Second tick: no new publishes, event list should be empty
        IReadOnlyList<HitEvent>? secondCapture = null;
        fixture.RegisterTickAction((view, _) => secondCapture = view.ReadEvents<HitEvent>().ToArray());
        fixture.TickFrame(0.016f);
        Assert.NotNull(secondCapture);
        Assert.Empty(secondCapture!);
    }

    // SC3: ECB AddComponent deferred until TickFrame
    [Fact]
    public void EcbAddComponent_DeferredUntilTickFrame()
    {
        using var fixture = new BlueprintTestFixture();
        var e = fixture.World.CreateEntity();

        fixture.Ecb.AddComponent(e, new TestComponent { Value = 42 });
        // Before TickFrame: not yet visible
        Assert.False(fixture.View.HasComponent<TestComponent>(e));

        fixture.TickFrame(0.016f);

        // After TickFrame: ECB played back
        Assert.True(fixture.View.HasComponent<TestComponent>(e));
        Assert.Equal(42, fixture.View.GetComponentRO<TestComponent>(e).Value);
    }

    // SC4: CompileAndLoad requires Phase 3 compiler (skip in Phase 1)
    [Fact(Skip = "Requires Phase 3 compiler")]
    [Trait("Category", "RequiresCompiler")]
    public void CompileAndLoad_IncrementsAlcWeakReferences()
    {
        using var fixture = new BlueprintTestFixture();
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestLib" };
        fixture.CompileAndLoad(asset);
        Assert.Equal(1, fixture.GetAlcWeakReferences().Count);
        Assert.True(fixture.GetAlcWeakReferences()[0].TryGetTarget(out _));
    }

    // SC5: ChooseTier threshold boundaries
    [Fact]
    public void ChooseTier_CorrectBoundaries()
    {
        Assert.Equal(BlackboardTier.B1024,  BlueprintTestFixture.ChooseTier(928));
        Assert.Equal(BlackboardTier.B4096,  BlueprintTestFixture.ChooseTier(929));
        Assert.Equal(BlackboardTier.B4096,  BlueprintTestFixture.ChooseTier(3936));
        Assert.Equal(BlackboardTier.B16384, BlueprintTestFixture.ChooseTier(3937));
    }

    // SC6: AttachBlueprint with hand-crafted fake definition (no compiler needed)
    [Fact]
    public void AttachBlueprint_RegisteredAsset_SetsHasSlot()
    {
        using var fixture = new BlueprintTestFixture();
        var asset = BlueprintAssetBuilder.Instance("TestBp").Build();

        // Register a hand-crafted fake definition (no compiler needed)
        var staging = fixture.Registry.BeginStaging();
        var def = new BlueprintDefinition
        {
            Name          = asset.Name,
            Kind          = FdpBlueprintDispatchKind.Instance,
            StructureHash = 0xDEADBEEFCAFEBABEUL,
            StateSize     = 8,
            InitDefault   = bytes => bytes.Clear(),
        };
        staging.Add(BlueprintIdHash.Compute(asset.AssetId), def);
        fixture.Registry.CommitStaging(staging);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        Assert.True(fixture.HasSlot(asset, entity));
    }

    // Additional: Dispose with no ALCs loaded completes without exception
    [Fact]
    public void Dispose_WithNoAlcsLoaded_Succeeds()
    {
        var fixture = new BlueprintTestFixture();
        fixture.Dispose();   // should not throw
    }

    // Additional: TickFrame with aux system calls Execute
    [Fact]
    public void AddSimulationSystem_SystemExecutedEachTick()
    {
        using var fixture = new BlueprintTestFixture();
        var tracker = new CountingSystem();
        fixture.AddSimulationSystem(tracker);

        fixture.TickFrame(0.016f);
        fixture.TickFrame(0.016f);

        Assert.Equal(2, tracker.ExecuteCount);
    }

    // Additional: DebugProbe.Sink is wired to DebugSession
    [Fact]
    public void DebugProbe_WiredToDebugSession_RecordsProbeCall()
    {
        using var fixture = new BlueprintTestFixture();
        var entity = new Entity(77, 0);

        DebugProbe.NodeEnter(entity, "node-test");

        Assert.True(fixture.DebugSession.Hit("node-test"));
    }
}

/// <summary>Helper: ECS system that counts Execute invocations.</summary>
internal sealed class CountingSystem : IEcsModuleSystem
{
    public int ExecuteCount { get; private set; }
    public void Execute(ISimulationView view, float deltaTime) => ExecuteCount++;
}
