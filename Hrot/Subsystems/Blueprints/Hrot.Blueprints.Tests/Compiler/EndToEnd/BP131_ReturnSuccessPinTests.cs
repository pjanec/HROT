using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
// CS0104: both Fdp.Toolkit.Blueprints and Hrot.Blueprints.Core.Assets declare BlueprintDispatchKind
// (a pre-existing duplicate-enum situation this test project already works around elsewhere -- see
// e.g. BP71_FunctionReturnValueTests/BP4005_And_MacroGraphKindTests). Pin to the Core.Assets one,
// which is what BlueprintAssetBuilder/BlueprintAsset.Dispatch actually use.
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler.EndToEnd;

/// <summary>
/// BP-131 — <c>Return.Success : bool</c>, AiPrimitive only.
///
/// <para>
/// ⭐ <b>The defect this closes.</b> An AiPrimitive's returned <c>NodeStatus</c> came from a combo the
/// designer picked at author time, i.e. a <b>compile-time constant</b>. A node whose entire job is to
/// report how execution went could therefore report nothing that execution decided. The fix is one
/// <c>bool</c> data-in pin; the <b>ABI is unchanged</b> — the method still returns <c>NodeStatus</c>,
/// and the bool maps to Success/Failure at the return statement.
/// </para>
///
/// <para>
/// ⚠⚠ <b><see cref="TicksFlip_FailureThenSuccess_ThroughTheRealRoslynGenerator"/> is the load-bearing
/// test, and it is deliberately a RUNTIME one.</b> Everything else here inspects IR or generated
/// source, and IR-shaped assertions cannot tell "the status is computed" from "the status happens to
/// be the constant we expected". Only compiling through real Roslyn and invoking the emitted
/// <c>TickCore</c> twice, across a value change, proves the status actually follows the pin.
/// (<c>CompileResult.Succeeded</c> never invokes Roslyn — asserting on it proves nothing about the
/// C# ever compiling.)
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class BP131_ReturnSuccessPinTests
{
    private const string DemoComponentFqn = "Hrot.AI.Behaviors.BpComponentDemo";

    private static BlueprintTestFixtureOptions NoAlcCheck { get; } =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ────────────────────────────────────────────────────────────────────────
    // Asset construction
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An AiPrimitive whose Return status is decided by <c>BpComponentDemo.Health &gt;= threshold</c>:
    ///
    /// <code>
    ///   EventEntry ──exec──► Return
    ///   GetComponent(Health) ──► Compare.A
    ///   Literal(threshold)   ──► Compare.B      Compare.Result ──► Return.Success
    /// </code>
    ///
    /// The component read is what makes the status genuinely runtime-varying: the test mutates
    /// <c>Health</c> on the entity between ticks, so the SAME compiled assembly must return a
    /// different status the second time. A parameter would not do — <c>InvokeBTreeAction</c> builds a
    /// fresh <c>Params</c> each call.
    /// </summary>
    private static BlueprintAsset BuildHealthGatedPrimitive(string name, int threshold)
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive(name)
            // BP1021: an AiPrimitive must declare at least one hosting. BTreeAction is the hosting
            // whose contract this feature is about — the tree reads the returned NodeStatus.
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var graph  = asset.Graphs[0];
        var ret    = graph.Nodes.OfType<ReturnNode>().Single();

        // The Success pin is projected by Stage0_Rehydrate for AiPrimitive, but this asset is built
        // in-process with authored pins, so it must carry the pin explicitly — exactly as a
        // hand-authored asset would. Name and type must match ReturnNode.SuccessPinName / bool.
        var successPinId = Guid.NewGuid();
        ret.Pins.Add(new Pin
        {
            Id        = successPinId,
            Name      = ReturnNode.SuccessPinName,
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Boolean" },
        });

        var getCompId  = Guid.NewGuid();
        var getCompOut = Guid.NewGuid();
        graph.Nodes.Add(new GetComponentNode
        {
            Id               = getCompId,
            ComponentTypeFqn = DemoComponentFqn,
            FieldName        = "Health",
            FieldTypeFqn     = "System.Int32",
            Pins = new List<Pin>
            {
                new Pin { Id = getCompOut, Name = "Value", Direction = "Out", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
        });

        var litId  = Guid.NewGuid();
        var litOut = Guid.NewGuid();
        graph.Nodes.Add(new LiteralNode
        {
            Id        = litId,
            TypeId    = "System.Int32",
            ValueJson = threshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Pins = new List<Pin>
            {
                new Pin { Id = litOut, Name = "Value", Direction = "Out", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
        });

        var cmpId  = Guid.NewGuid();
        var cmpA   = Guid.NewGuid();
        var cmpB   = Guid.NewGuid();
        var cmpOut = Guid.NewGuid();
        graph.Nodes.Add(new CompareNode
        {
            Id       = cmpId,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Pins = new List<Pin>
            {
                new Pin { Id = cmpA,   Name = "A",      Direction = "In",  IsExec = false, TypeRef = new() },
                new Pin { Id = cmpB,   Name = "B",      Direction = "In",  IsExec = false, TypeRef = new() },
                new Pin { Id = cmpOut, Name = "Result", Direction = "Out", IsExec = false,
                          TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } },
            },
        });

        graph.Links.Add(new Link { FromNodeId = getCompId, FromPinId = getCompOut, ToNodeId = cmpId, ToPinId = cmpA });
        graph.Links.Add(new Link { FromNodeId = litId,     FromPinId = litOut,     ToNodeId = cmpId, ToPinId = cmpB });
        graph.Links.Add(new Link { FromNodeId = cmpId,     FromPinId = cmpOut,     ToNodeId = ret.Id, ToPinId = successPinId });

        return asset;
    }

    /// <summary>The same primitive with the Success pin PRESENT but UNWIRED, at the given status.</summary>
    private static BlueprintAsset BuildUnwiredSuccessPrimitive(string name, NodeStatus status)
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive(name)
            .WithHostings(AiPrimitiveHosting.BTreeAction)   // BP1021
            .WithGraph("Tick", g => g.Entry().Return(status))
            .Build();

        var ret = asset.Graphs[0].Nodes.OfType<ReturnNode>().Single();
        ret.Pins.Add(new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = ReturnNode.SuccessPinName,
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Boolean" },
        });
        return asset;
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The runtime proof
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The test the whole item exists for.</b> One compiled assembly, two ticks, one value
    /// changed in between — Failure then Success. A constant-status implementation cannot pass this,
    /// which is precisely what an IR-level assertion could not have established.
    /// </summary>
    [Fact]
    public void TicksFlip_FailureThenSuccess_ThroughTheRealRoslynGenerator()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset = BuildHealthGatedPrimitive("BP131FlipPrimitive", threshold: 50);

        fixture.CompileAndLoad(asset);   // real Roslyn — not CompileResult.Succeeded

        fixture.World.RegisterComponent<Hrot.AI.Behaviors.BpComponentDemo>();
        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, new Hrot.AI.Behaviors.BpComponentDemo { Health = 10 });

        // Tick 1: Health 10 >= 50 is false ⇒ the bool is false ⇒ Failure.
        var tick1 = fixture.InvokeBTreeAction(asset, entity);
        Assert.Equal(NodeStatus.Failure, tick1);

        // Nothing about the blueprint changes — only the world does.
        ref var demo = ref fixture.World.GetComponentRW<Hrot.AI.Behaviors.BpComponentDemo>(entity);
        demo.Health = 100;

        // Tick 2: 100 >= 50 is true ⇒ Success. Same assembly, same asset, different outcome.
        var tick2 = fixture.InvokeBTreeAction(asset, entity);
        Assert.Equal(NodeStatus.Success, tick2);
    }

    /// <summary>
    /// ⚠ The migration guarantee. An unwired <c>Success</c> pin must fall back to the authored
    /// <c>rn.Status</c> — NOT to <c>default(bool)</c>, which is <c>false</c> = Failure and would have
    /// silently flipped every AiPrimitive Return already shipped. Asserted at runtime rather than on
    /// the IR, because a wrong-values regression of exactly this shape is what a green IR suite would
    /// have missed.
    /// </summary>
    [Fact]
    public void UnwiredSuccessPin_FallsBackToTheAuthoredStatus_NotToFalse()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset = BuildUnwiredSuccessPrimitive("BP131UnwiredPrimitive", NodeStatus.Success);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();

        Assert.Equal(NodeStatus.Success, fixture.InvokeBTreeAction(asset, entity));
    }

    /// <summary>And the fallback honours a Return whose author deliberately chose Failure.</summary>
    [Fact]
    public void UnwiredSuccessPin_HonoursAnAuthoredFailure()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var asset = BuildUnwiredSuccessPrimitive("BP131UnwiredFailurePrimitive", NodeStatus.Failure);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();

        Assert.Equal(NodeStatus.Failure, fixture.InvokeBTreeAction(asset, entity));
    }

    // ────────────────────────────────────────────────────────────────────────
    // H1 — the IR/emitter half
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// H1: a wired Success pin makes <c>IrTerm_ReturnStatus</c> carry a runtime condition instead of
    /// relying on its constant.
    /// </summary>
    [Fact]
    public void WiredSuccessPin_MakesTheTerminatorCarryACondition()
    {
        var asset = BuildHealthGatedPrimitive("BP131IrPrimitive", threshold: 50);
        var ir    = CompileToIr(asset);

        var term = ir.Graphs
            .SelectMany(g => g.Blocks)
            .Select(b => b.Terminator)
            .OfType<IrTerm_ReturnStatus>()
            .Single();

        Assert.NotNull(term.Condition);
    }

    /// <summary>
    /// The complement: with no wired pin the terminator stays a pure constant, so every existing
    /// AiPrimitive keeps emitting exactly the C# it emitted before this feature.
    /// </summary>
    [Fact]
    public void UnwiredSuccessPin_LeavesTheTerminatorConstant()
    {
        var asset = BuildUnwiredSuccessPrimitive("BP131IrUnwiredPrimitive", NodeStatus.Success);
        var ir    = CompileToIr(asset);

        var term = ir.Graphs
            .SelectMany(g => g.Blocks)
            .Select(b => b.Terminator)
            .OfType<IrTerm_ReturnStatus>()
            .Single();

        Assert.Null(term.Condition);
        Assert.Equal(NodeStatus.Success, term.Status);
    }

    // ────────────────────────────────────────────────────────────────────────
    // H2 — the two shapes that branch on valuePins.COUNT
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ H2. <c>BuildReturnTerminator</c> collects <c>valuePins</c> as "every non-exec pin on the
    /// Return node" and branches on the COUNT: <c>== 0</c> selects the zero-output-Library NodeStatus
    /// return. A <c>Success</c> pin counted among them would flip that (test-locked) shape onto the
    /// value-return path.
    ///
    /// <para>
    /// This asserts the name-exclusion holds even when the pin appears on a dispatch whose projection
    /// would never have produced it — a hand-authored asset can carry pins no projection wrote, and
    /// the projection gate and the name exclusion fail independently.
    /// </para>
    /// </summary>
    [Fact]
    public void SuccessPinOnAZeroOutputLibrary_DoesNotDisturbItsNodeStatusReturn()
    {
        var asset = BlueprintAssetBuilder
            .Library("BP131ZeroOutputLib")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var ret = asset.Graphs[0].Nodes.OfType<ReturnNode>().Single();
        ret.Pins.Add(new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = ReturnNode.SuccessPinName,
            Direction = "In",
            IsExec    = false,
            TypeRef   = new BlueprintTypeRef { TypeId = "System.Boolean" },
        });

        var ir = CompileToIr(asset);

        // Still a status return, exactly as before BP-131 — not IrTerm_Return.
        var term = ir.Graphs
            .SelectMany(g => g.Blocks)
            .Select(b => b.Terminator)
            .OfType<IrTerm_ReturnStatus>()
            .Single();
        Assert.Null(term.Condition);   // Library never gets the runtime path
    }

    // ────────────────────────────────────────────────────────────────────────
    // The projection gate — AiPrimitive only
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pin is projected for AiPrimitive and for nothing else. This gate is the PRIMARY
    /// containment for H2 — the name exclusion above is the second line.
    /// </summary>
    [Theory]
    [InlineData(BlueprintDispatchKind.AiPrimitive, true)]
    [InlineData(BlueprintDispatchKind.Library,     false)]
    [InlineData(BlueprintDispatchKind.Instance,    false)]
    public void SuccessPin_IsProjected_ForAiPrimitiveOnly(BlueprintDispatchKind dispatch, bool expected)
    {
        var builder = dispatch switch
        {
            BlueprintDispatchKind.AiPrimitive => BlueprintAssetBuilder.AiPrimitive("BP131Gate")
                                                      .WithHostings(AiPrimitiveHosting.BTreeAction),
            BlueprintDispatchKind.Library     => BlueprintAssetBuilder.Library("BP131Gate"),
            _                                  => BlueprintAssetBuilder.Instance("BP131Gate"),
        };

        var asset = builder.WithGraph("Main", g => g.Entry().Return()).Build();

        // Strip authored pins so Stage 0 is genuinely the thing projecting them.
        foreach (var n in asset.Graphs[0].Nodes) n.Pins.Clear();

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var ret = asset.Graphs[0].Nodes.OfType<ReturnNode>().Single();
        bool hasSuccess = ret.Pins.Any(
            p => !p.IsExec && p.Name == ReturnNode.SuccessPinName);

        Assert.Equal(expected, hasSuccess);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Mirrors <c>BPC_ImplicitReturnTests.RunSchedule</c> — the established Stage 5 harness.</summary>
    private static IrAsset CompileToIr(BlueprintAsset asset)
    {
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, DefaultOptions());
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        return Stage5_Schedule.Run(typed, ctx);
    }
}
