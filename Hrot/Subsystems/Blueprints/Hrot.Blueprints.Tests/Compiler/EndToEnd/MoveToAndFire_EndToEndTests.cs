using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// End-to-end compile test for the MoveToAndFire AiPrimitive blueprint.
/// Runs the full pipeline (Stages 1-8) and verifies structural properties.
/// </summary>
public sealed class MoveToAndFire_EndToEndTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    [Fact]
    public void MoveToAndFire_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);
        Assert.False(string.IsNullOrEmpty(result.GeneratedSource));
    }

    [Fact]
    public void MoveToAndFire_GeneratedSource_ContainsExpectedStructures()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        var src = result.GeneratedSource!;
        // AiPrimitive must emit Params and WorkingState structs.
        Assert.Contains("public struct Params",      src);
        Assert.Contains("public struct WorkingState", src);
        // TickCore method is the core execution method.
        Assert.Contains("TickCore",                  src);
        // BTree thunk for BTreeAction hosting.
        Assert.Contains("BTreeTick",                 src);
    }

    [Fact]
    public void MoveToAndFire_DebugMap_IsNonNull()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);
        Assert.NotNull(result.DebugMap);
    }

    [Fact]
    public void MoveToAndFire_GeneratedFileName_ContainsBpSuffix()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);
        Assert.Contains("_Bp.g.cs", result.GeneratedFileName);
    }
}

/// <summary>
/// SC6: Verifies the 2-tick BTreeAction phase-advance sequence for MoveToAndFire.
/// Tick1 returns Running (channel waiting), Tick2 returns Success (channel complete).
/// </summary>
public sealed class MoveToAndFire_BTreeTick_Tests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();
    private readonly BlueprintAsset _asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

    public void Dispose() => _fixture.Dispose();

    // Previously part of a "7 interacting bugs" skip block. Most of those were fixed in-tree
    // (Stage0_Rehydrate pin rehydration; Stage5_Schedule catalog-driven FQN/ActionId lookup;
    // StatementEmitter TryGetSynthesizedOpInfix + NodeStatus FQN-qualification), so the first-tick
    // (channel-idle → Running) path now works and this test is re-enabled (verified 2026-07-13).
    // The second-tick channel-complete path is still broken — see BTreeTick_AfterChannelComplete
    // below, which remains skipped. NOTE: this exercises the generated TickCore directly (via
    // BlueprintTestFixture.InvokeBTreeAction reflection); it does NOT exercise a real BTree interpreter
    // binding — that end-to-end path is the still-open I1 registration wire (DEBT-AIB-025), see
    // MoveToAndFire-Bug-Triage-2026-07-13.md.
    [Fact]
    public void BTreeTick_FirstCall_ReturnsRunning_WhenChannelIsIdle()
    {
        // Arrange: compile → load → register
        _fixture.CompileAndLoad(_asset);
        var entity = _fixture.CreateEntity();
        _fixture.World.AddComponent(entity, default(LocomotionChannel));

        // Act: Tick 1 — channel starts idle, WaitForChannel suspends
        var status = _fixture.InvokeBTreeAction(_asset, entity);

        // Assert: working state is persisted, action is pending
        Assert.Equal(NodeStatus.Running, status);
    }

    // Channel-complete path: after the channel reports Success, Tick 2 must return Success.
    // (This exercises the generated TickCore directly via reflection, not a real interpreter
    // binding — that end-to-end path is the still-open I1 registration wire, DEBT-AIB-025.)
    [Fact]
    public void BTreeTick_AfterChannelComplete_ReturnsSuccess()
    {
        // Arrange: compile → load → register → Tick 1
        _fixture.CompileAndLoad(_asset);
        var entity = _fixture.CreateEntity();
        _fixture.World.AddComponent(entity, default(LocomotionChannel));

        var tick1 = _fixture.InvokeBTreeAction(_asset, entity);
        Assert.Equal(NodeStatus.Running, tick1);  // sanity

        // Simulate movement completion — set the channel to success.
        // LocomotionChannel.Status is Fbt.NodeStatus. Assign the runtime value DIRECTLY:
        // the asset-side NodeStatus (Hrot.Blueprints.Core.Assets, Success=0) and Fbt.NodeStatus
        // (Success=1) have DIFFERENT ordinals, so an (Fbt.NodeStatus)(int)NodeStatus.Success cast
        // would produce Fbt.Failure. These two enums must only be converted by name, never by ordinal.
        ref var chan = ref _fixture.World.GetComponentRW<LocomotionChannel>(entity);
        chan.Status = Fbt.NodeStatus.Success;

        // Act: Tick 2 — WaitForChannel sees Success, continues to Return
        var tick2 = _fixture.InvokeBTreeAction(_asset, entity);

        Assert.Equal(NodeStatus.Success, tick2);
    }
}
