using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Tests.Builders;
using FbtNodeStatus = Fbt.NodeStatus;

namespace Hrot.Blueprints.Tests.TestHarness;

/// <summary>
/// BPF-008: Verifies GetSlotEntry, SetChannelStatus and SnapshotAllBlackboards helpers on
/// BlueprintTestFixture.
/// </summary>
[Collection("DebugProbe")]
public sealed class FixtureHelperTests
{
    private static BlueprintAsset BuildSimpleInstanceAsset(string name = "FixtureHelperBp")
        => BlueprintAssetBuilder
            .Instance(name)
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    [Fact]
    public void GetSlotEntry_ReturnsCorrectBlueprintId_AfterAttach()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset  = BuildSimpleInstanceAsset("GetSlotEntryBp");
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var slot = fixture.GetSlotEntry(asset, entity);

        int expected = Fdp.Toolkit.Blueprints.BlueprintIdHash.Compute(asset.AssetId);
        Assert.Equal(expected, slot.BlueprintId);
    }

    [Fact]
    public void GetSlotEntry_Throws_WhenNoBlueprintAttached()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset  = BuildSimpleInstanceAsset("GetSlotEntryNoBp");
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();  // no AttachBlueprint call

        Assert.Throws<InvalidOperationException>(
            () => fixture.GetSlotEntry(asset, entity));
    }

    [Fact]
    public void SetChannelStatus_WritesStatus_ToLocomotionChannel()
    {
        using var fixture = new BlueprintTestFixture();

        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, default(LocomotionChannel));

        fixture.SetChannelStatus<LocomotionChannel>(entity, FbtNodeStatus.Running);

        ref readonly var channel = ref fixture.World.GetComponentRO<LocomotionChannel>(entity);
        Assert.Equal(FbtNodeStatus.Running, channel.Status);

        fixture.SetChannelStatus<LocomotionChannel>(entity, FbtNodeStatus.Success);

        ref readonly var channel2 = ref fixture.World.GetComponentRO<LocomotionChannel>(entity);
        Assert.Equal(FbtNodeStatus.Success, channel2.Status);
    }

    [Fact]
    public void SnapshotAllBlackboards_ReturnsNonEmpty_ForRunningEntity()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset  = BuildSimpleInstanceAsset("SnapshotBp");
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);  // ensure BB component is initialized

        var snapshot = fixture.SnapshotAllBlackboards();

        Assert.False(snapshot.IsEmpty);
    }

    [Fact]
    public void SnapshotAllBlackboards_ReturnsEmpty_WhenNoEntities()
    {
        using var fixture = new BlueprintTestFixture();

        var snapshot = fixture.SnapshotAllBlackboards();

        Assert.True(snapshot.IsEmpty);
    }
}

/// <summary>
/// BPF-009: Verifies InvokeHsmAction and InvokeHsmGuard helpers on BlueprintTestFixture.
/// Uses a compiled AiPrimitive with HsmAction hosting so the action is registered via the
/// generated registrar before InvokeHsmAction is called.
/// </summary>
[Collection("DebugProbe")]
public sealed class HsmInvokeHelpersTests
{
    private static CompileOptions HsmOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static BlueprintAsset BuildHsmAiPrimitive(string name = "HsmTestAction")
        => BlueprintAssetBuilder
            .AiPrimitive(name)
            .WithHostings(AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    [Fact]
    public void InvokeHsmAction_DoesNotThrow_And_Returns_True()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset  = BuildHsmAiPrimitive("HsmActionNoThrow");
        fixture.CompileAndLoad(asset, HsmOptions());

        var entity = fixture.CreateEntity();

        bool result = fixture.InvokeHsmAction(asset, entity);

        Assert.True(result);
    }

    [Fact]
    public void InvokeHsmGuard_ReturnsTrue_ForUnregisteredGuard()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset  = BuildHsmAiPrimitive("HsmGuardUnreg");
        // Compile with HsmAction only -- no HsmGuard hosting, so no guard is registered.
        fixture.CompileAndLoad(asset, HsmOptions());

        var entity = fixture.CreateEntity();

        bool result = fixture.InvokeHsmGuard(asset, entity);

        // HsmActionDispatcher.EvaluateGuard returns true when no guard is registered.
        Assert.True(result);
    }
}
