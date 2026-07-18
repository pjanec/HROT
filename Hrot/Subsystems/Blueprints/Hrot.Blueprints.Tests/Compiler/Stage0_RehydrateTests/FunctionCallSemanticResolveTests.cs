using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Guards the Blocker-1 fix: Stage0 rehydrates a pin-less <see cref="FunctionCallNode"/> into typed
/// pins using an injected <see cref="IClrSignatureResolver"/> — the reflection-free path the Roslyn
/// incremental generator uses (its <c>RoslynClrSignatureResolver</c> is backed by the semantic model,
/// since the curated-helper types live in the very assembly being compiled and cannot be reflected).
/// These tests stub the resolver so they run game-free, and assert the exact pin shape the editor's
/// projection produces — proving a same-assembly-helper blueprint round-trips with NO explicit pins.
/// </summary>
public sealed class FunctionCallSemanticResolveTests
{
    /// <summary>Deterministic stub resolver keyed by "Type.Method".</summary>
    private sealed class StubResolver : IClrSignatureResolver
    {
        private readonly Dictionary<string, ClrMethodSig> _map;
        public StubResolver(Dictionary<string, ClrMethodSig> map) => _map = map;
        public bool TryResolve(string targetTypeId, string methodName, out ClrMethodSig? sig)
        {
            if (_map.TryGetValue($"{targetTypeId}.{methodName}", out var s)) { sig = s; return true; }
            sig = null;
            return false;
        }
    }

    private static CompileOptions OptionsWith(IClrSignatureResolver resolver) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            ClrSignatureResolver: resolver);

    /// <summary>Wraps a single pin-less FunctionCall node in an AiPrimitive asset (so trailing
    /// self/view context is in scope) and returns the node after Stage0 rehydration.</summary>
    private static FunctionCallNode Rehydrate(FunctionCallNode fc, IClrSignatureResolver resolver)
    {
        fc.Pins = new List<Pin>();
        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { fc }, Links = new List<Link>(),
            Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "FcTest",
            Dispatch = BlueprintDispatchKind.AiPrimitive,
            Graphs = new List<Graph> { graph }, Variables = new(), CustomEvents = new(),
        };
        Stage0_Rehydrate.Run(asset, OptionsWith(resolver));
        return fc;
    }

    private static (string Name, string Dir, bool Exec, string? Type) P(Pin p) =>
        (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId);

    [Fact]
    public void PureCall_NoContext_RegisteredTypes_ProducesTypedPins()
    {
        var resolver = new StubResolver(new()
        {
            ["Hrot.AI.Behaviors.Brains.HillAttackSharedStateOps.GetCachedEqsRequestId"] =
                new ClrMethodSig(
                    new[] { new ClrParamInfo("s", "Hrot.AI.Behaviors.Brains.HillAttackSharedState") },
                    "System.Int64"),
        });
        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = true,
            TargetTypeId = "Hrot.AI.Behaviors.Brains.HillAttackSharedStateOps",
            MethodName = "GetCachedEqsRequestId",
            TrailingContext = FunctionCallContextKind.None,
        };
        var pins = Rehydrate(fc, resolver).Pins.Select(P).ToList();

        // Registered curated struct → UNPREFIXED TypeId (resolves via the type table).
        Assert.Equal(("s", "In", false, "Hrot.AI.Behaviors.Brains.HillAttackSharedState"), pins[0]);
        Assert.Equal(("Return", "Out", false, "System.Int64"), pins[1]);
        Assert.Equal(2, pins.Count);
    }

    [Fact]
    public void PureCall_TrailingView_OmitsViewPin()
    {
        var resolver = new StubResolver(new()
        {
            ["Hrot.AI.Behaviors.Brains.AreaQueryBatchOps.IsReady"] = new ClrMethodSig(
                new[]
                {
                    new ClrParamInfo("requestId", "System.Int64"),
                    new ClrParamInfo("view", "Fdp.ModuleHost.Abstractions.ISimulationView"),
                },
                "System.Boolean"),
        });
        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = true,
            TargetTypeId = "Hrot.AI.Behaviors.Brains.AreaQueryBatchOps",
            MethodName = "IsReady",
            TrailingContext = FunctionCallContextKind.View,
        };
        var pins = Rehydrate(fc, resolver).Pins.Select(P).ToList();

        // The trailing ISimulationView param is consumed as engine context → NOT a pin.
        Assert.Equal(("requestId", "In", false, "System.Int64"), pins[0]);
        Assert.Equal(("Return", "Out", false, "System.Boolean"), pins[1]);
        Assert.Equal(2, pins.Count);
    }

    [Fact]
    public void ImpureCall_SelfAndView_ExecPinsPlusOmittedContext()
    {
        var resolver = new StubResolver(new()
        {
            ["Hrot.AI.Behaviors.Brains.AreaQueryBatchOps.Request"] = new ClrMethodSig(
                new[]
                {
                    new ClrParamInfo("targetArea", "Fdp.Core.Entity"),
                    new ClrParamInfo("self", "Fdp.Core.Entity"),
                    new ClrParamInfo("view", "Fdp.ModuleHost.Abstractions.ISimulationView"),
                },
                "System.Int64"),
        });
        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = false,
            TargetTypeId = "Hrot.AI.Behaviors.Brains.AreaQueryBatchOps",
            MethodName = "Request",
            TrailingContext = FunctionCallContextKind.SelfAndView,
        };
        var pins = Rehydrate(fc, resolver).Pins.Select(P).ToList();

        // Impure → exec In/Out; self + view both omitted; only targetArea + Return remain as data.
        Assert.Equal(("In", "In", true, ""), pins[0]);
        Assert.Equal(("Out", "Out", true, ""), pins[1]);
        Assert.Equal(("targetArea", "In", false, "Fdp.Core.Entity"), pins[2]);
        Assert.Equal(("Return", "Out", false, "System.Int64"), pins[3]);
        Assert.Equal(4, pins.Count);
    }

    [Fact]
    public void UnregisteredCuratedStruct_IsGlobalStamped()
    {
        var resolver = new StubResolver(new()
        {
            ["Demo.Curated.WidgetOps.Bump"] = new ClrMethodSig(
                new[] { new ClrParamInfo("w", "Demo.Curated.Widget") },
                "Demo.Curated.Widget"),
        });
        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = true,
            TargetTypeId = "Demo.Curated.WidgetOps",
            MethodName = "Bump",
            TrailingContext = FunctionCallContextKind.None,
        };
        var pins = Rehydrate(fc, resolver).Pins.Select(P).ToList();

        // Widget is unknown to StaticTypeRegistry → stamped with the global:: sentinel so the
        // registry's project-type acceptance path resolves it (mirrors GetShared/SetShared).
        Assert.Equal(("w", "In", false, "global::Demo.Curated.Widget"), pins[0]);
        Assert.Equal(("Return", "Out", false, "global::Demo.Curated.Widget"), pins[1]);
        Assert.Equal(2, pins.Count);
    }

    [Fact]
    public void Unresolvable_FallsBackToPlaceholder_NoTypedPins()
    {
        // Empty resolver + a target reflection cannot find (fake type) → no ClrMethodSig, so the
        // pure-call placeholder path runs. With no links and no PinDefaults, no data pins are added
        // (and pure calls carry no exec pins) — i.e. it does NOT fabricate typed pins.
        var resolver = new StubResolver(new());
        var fc = new FunctionCallNode
        {
            Id = Guid.NewGuid(), IsPure = true,
            TargetTypeId = "No.Such.Type.Anywhere",
            MethodName = "Nope",
            TrailingContext = FunctionCallContextKind.None,
        };
        var pins = Rehydrate(fc, resolver).Pins;
        Assert.Empty(pins);
    }
}
