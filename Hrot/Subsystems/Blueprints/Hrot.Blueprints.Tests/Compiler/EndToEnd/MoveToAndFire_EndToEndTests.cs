using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
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

/// <summary>
/// I1 canary: proves a blueprint-authored AiPrimitive action executes through a <b>real FastBTree
/// <see cref="Interpreter{TBlackboard,TContext}"/> binding</b> — not the reflection <c>TickCore</c>
/// bypass used by <see cref="MoveToAndFire_BTreeTick_Tests"/>.
/// <para>
/// The AiPrimitive registrar now registers <c>BTreeTick</c> into the string-keyed
/// <see cref="ActionRegistry{TBlackboard,TContext}"/> under <c>{ClassFqn}.BTreeTick@0</c> (I1),
/// which is the registry the interpreter binds from. A one-node blob whose action MethodName is
/// that key binds and ticks it for real. Before I1 the thunk was registered into the orphaned
/// int-keyed <c>BehaviorRegistry</c> side-table, so this binding resolved to the interpreter's
/// silent Failure fallback and no blueprint action ever ran through a tick.
/// </para>
/// </summary>
public sealed class MoveToAndFire_InterpreterTick_Tests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();
    private readonly BlueprintAsset _asset = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);

    public void Dispose() => _fixture.Dispose();

    // The interpreter binds by the same key the AiPrimitive registrar registers under.
    private string ActionKey()
        => $"Hrot.AI.Behaviors.Generated.MoveToAndFire_{BlueprintIdHash.Compute(_asset.AssetId):X8}_Bp.BTreeTick@0";

    [Fact]
    public unsafe void InterpreterBinding_TicksBlueprintAction_RunningThenSuccess()
    {
        // Arrange: compile + load → registrar populates the fixture's ActionRegistry (I1).
        _fixture.CompileAndLoad(_asset);

        var entity = _fixture.CreateEntity();
        _fixture.World.AddComponent(entity, default(LocomotionChannel));
        _fixture.World.AddComponent(entity, default(Blackboard1024)); // AiPrimitive working-state rail

        // The action must resolve in the ActionRegistry — otherwise the interpreter would silently
        // bind the Failure fallback (the pre-I1 behavior).
        Assert.True(_fixture.ActionRegistry.TryGetAction(ActionKey(), out _),
            $"AiPrimitive action '{ActionKey()}' must be registered into the FastBTree ActionRegistry (I1).");

        // A minimal one-node tree whose single action references the blueprint action by key.
        var builder = new BTreeBuilder<BrainBlackboard, BTreeContext>();
        builder.Action(ActionKey());
        var blob = builder.Compile("I1_MoveToAndFire_Smoke");
        var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, _fixture.ActionRegistry);

        var bb    = default(BrainBlackboard);
        var state = default(Fbt.BehaviorTreeState);
        var ctx   = new BTreeContext { Self = entity, World = _fixture.World };

        // Tick 1: channel idle → WaitForChannel suspends → Running.
        var tick1 = interpreter.Tick(ref bb, ref state, ref ctx);
        Assert.Equal(Fbt.NodeStatus.Running, tick1);

        // Complete the channel; Tick 2 → the blueprint action returns Success through the interpreter.
        ref var chan = ref _fixture.World.GetComponentRW<LocomotionChannel>(entity);
        chan.Status = Fbt.NodeStatus.Success;
        var tick2 = interpreter.Tick(ref bb, ref state, ref ctx);
        Assert.Equal(Fbt.NodeStatus.Success, tick2);
    }
}
