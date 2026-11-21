using System.Text.RegularExpressions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// FC-1 (Q#20) -- lowering + emit coverage for <see cref="CollectionWriteNode"/> /
/// <c>IrOp_CollectionWrite</c>. Mirrors <see cref="ComponentCollectionConsumerLoweringTests"/>'
/// fixture shape (a <c>GetComponent&lt;BpFixedListDemo&gt;</c> collection out-pin wired into the
/// consumer; Stage1-7 real pipeline, no Roslyn -- the accessor FQNs are baked strings never
/// resolved at compile time) and <see cref="SetComponentWriteLoweringTests"/>' guarded-write
/// assertions. The contract proven here:
/// <list type="bullet">
///   <item>guarded write-if-present: <c>HasComponent&lt;T&gt;</c> drives BOTH the "Ok" out and the
///   guard; <c>GetComponentRW&lt;T&gt;</c> is fetched only INSIDE the guard;</item>
///   <item>the mutation is a CURATED ACCESSOR CALL (<c>global::{WriteAccessorFqn}(ref __wc, ...)</c>)
///   -- raw buffer/element access never appears (Q#5-C / Q#20 G1);</item>
///   <item>SELF-BOUND even when the producing GetComponent resolves another entity (G4 defense in
///   depth -- Stage2's BP2070 rejects that wiring, proven by skipping Stage2 here);</item>
///   <item>never-silent: refused op / absent component emit
///   <c>DebugProbe.CollectionWriteFailed</c> in Debug mode, nothing in Release;</item>
///   <item>unwired/unbaked/missing-operand degrade to a constant <c>Ok=false</c>, no write.</item>
/// </list>
/// </summary>
public sealed class CollectionWriteLoweringTests
{
    private const string ComponentFqn = "Hrot.AI.Behaviors.BpFixedListDemo";
    private const string OpsFqn       = "Hrot.AI.Behaviors.Brains.BpFixedListDemoOps";
    private const string CountFqn     = OpsFqn + ".Count";
    private const string ItemFqn      = OpsFqn + ".Item";

    private static CompileOptions Options(CompilerMode mode = CompilerMode.Debug) => new(
        Mode:              mode,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId, bool isArray = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray } };

    /// <summary>Pin-authored GetComponent producer with the baked "Items" collection decl (mirrors the consumer tests' builder, retargeted at the FC-0 InlineArray demo).</summary>
    private static (GetComponentNode Node, Pin ItemsOut) BuildProducer()
    {
        var itemsOut = DataPin("Items", "Out", "System.Int32", isArray: true);
        var foundOut = DataPin("Found", "Out", "System.Boolean");
        var node = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            Fields = new List<ComponentFieldDecl>
            {
                new()
                {
                    Name             = "Items",
                    TypeId           = "",
                    IsCollection     = true,
                    ElementTypeId    = "System.Int32",
                    CountAccessorFqn = CountFqn,
                    ItemAccessorFqn  = ItemFqn,
                },
            },
        };
        node.Pins.AddRange(new[] { itemsOut, foundOut });
        return (node, itemsOut);
    }

    /// <summary>Pin-authored write node -- mirrors Stage0's EnrichCollectionWritePins pin set for <paramref name="op"/>.</summary>
    private static CollectionWriteNode BuildWriteNode(CollectionWriteOp op, string writeAccessorFqn)
    {
        var node = new CollectionWriteNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ComponentFqn,
            Op               = op,
            WriteAccessorFqn = writeAccessorFqn,
            ElementTypeFqn   = "System.Int32",
        };
        node.Pins.Add(ExecPin("In", "In"));
        node.Pins.Add(ExecPin("Out", "Out"));
        node.Pins.Add(DataPin("Collection", "In", "System.Int32", isArray: true));
        if (op is CollectionWriteOp.SetAt or CollectionWriteOp.InsertAt or CollectionWriteOp.RemoveAt)
            node.Pins.Add(DataPin("Index", "In", "System.Int32"));
        if (op is CollectionWriteOp.Resize)
            node.Pins.Add(DataPin("Length", "In", "System.Int32"));
        if (op is CollectionWriteOp.Add or CollectionWriteOp.SetAt or CollectionWriteOp.InsertAt)
            node.Pins.Add(DataPin("Value", "In", "System.Int32"));
        node.Pins.Add(DataPin("Ok", "Out", "System.Boolean"));
        return node;
    }

    private static Pin FindPin(Node node, string name) =>
        node.Pins.First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Entry -> write -> Return, producer's collection out wired into the write's "Collection",
    /// int literals wired into whichever operand pins exist. Returns the built asset.
    /// </summary>
    private static BlueprintAsset BuildAsset(
        CollectionWriteNode writeNode, GetComponentNode? producer, Pin? producerOut,
        bool wireCollection = true, bool wireValue = true)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("Out", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, writeNode, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,     FromPinId = entryOut.Id,               ToNodeId = writeNode.Id, ToPinId = FindPin(writeNode, "In").Id },
                new Link { FromNodeId = writeNode.Id, FromPinId = FindPin(writeNode, "Out").Id, ToNodeId = ret.Id,    ToPinId = retIn.Id },
            },
        };

        if (producer is not null)
        {
            graph.Nodes.Add(producer);
            if (wireCollection && producerOut is not null)
                graph.Links.Add(new Link
                {
                    FromNodeId = producer.Id, FromPinId = producerOut.Id,
                    ToNodeId = writeNode.Id, ToPinId = FindPin(writeNode, "Collection").Id,
                });
        }

        void WireIntLiteral(string pinName, string valueJson)
        {
            var pin = writeNode.Pins.FirstOrDefault(p =>
                !p.IsExec && p.Direction == "In"
                && string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
            if (pin is null) return;
            var litOut = DataPin("Value", "Out", "System.Int32");
            var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = valueJson };
            lit.Pins.Add(litOut);
            graph.Nodes.Add(lit);
            graph.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = writeNode.Id, ToPinId = pin.Id });
        }

        WireIntLiteral("Index", "1");
        WireIntLiteral("Length", "2");
        if (wireValue) WireIntLiteral("Value", "42");

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "CollectionWriteCoverage",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    private static string CompileFull(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug)
    {
        var result = new BlueprintCompiler().Compile(asset, Options(mode));
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.NotNull(result.GeneratedSource);
        return result.GeneratedSource!;
    }

    /// <summary>Stage3-7 only (skips Stage2) -- for fixtures Stage2 deliberately rejects (BP2067/BP2070).</summary>
    private static string CompileSkippingValidate(BlueprintAsset asset)
    {
        var opts = Options();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);
        var norm    = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(norm, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (src, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        Assert.False(sink.HasErrors,
            "Emit errors: " + string.Join(", ", sink.All.Where(d => d.IsError).Select(d => $"{d.Code}:{d.Message}")));
        Assert.NotNull(src);
        return src!;
    }

    // -----------------------------------------------------------------------
    // The guarded accessor-call shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAt_EmitsGuardedAccessorCall_OkReassigned()
    {
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.SetAt, OpsFqn + ".SetAt");
        var src = CompileFull(BuildAsset(write, producer, itemsOut));

        // Guard temp drives both Ok and the guard; RW fetched only inside; accessor call reassigns.
        var m = Regex.Match(src, @"var (__t\d+) = \S+\.HasComponent<global::Hrot\.AI\.Behaviors\.BpFixedListDemo>\((__t\d+)\);");
        Assert.True(m.Success, "guarded HasComponent<BpFixedListDemo> not found:\n" + src);
        string ok = m.Groups[1].Value;
        Assert.Contains($"if ({ok})", src);
        Assert.Matches(@"ref var __wc\d+ = ref \S+\.GetComponentRW<global::Hrot\.AI\.Behaviors\.BpFixedListDemo>\(__t\d+\);", src);
        Assert.Matches(ok + Regex.Escape($" = global::{OpsFqn}.SetAt(ref __wc") + @"\d+, __t\d+, __t\d+\);", src);

        // Never-silent (Debug mode): both failure probes present, with the verb + reasons.
        Assert.Contains($"DebugProbe.CollectionWriteFailed(self, \"{write.Id:D}\", \"SetAt\", \"op-rejected\");", src);
        Assert.Contains($"DebugProbe.CollectionWriteFailed(self, \"{write.Id:D}\", \"SetAt\", \"component-absent\");", src);

        // Q#5-C: no raw element access anywhere in the write path.
        Assert.DoesNotContain(".Items[", src);
    }

    [Fact]
    public void Add_AccessorArgsAreRefAndValueOnly()
    {
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.Add, OpsFqn + ".Add");
        var src = CompileFull(BuildAsset(write, producer, itemsOut));

        Assert.Matches(Regex.Escape($"global::{OpsFqn}.Add(ref __wc") + @"\d+, __t\d+\);", src);
        Assert.DoesNotMatch(Regex.Escape($"global::{OpsFqn}.Add(ref __wc") + @"\d+, __t\d+, __t\d+\);", src);
    }

    [Fact]
    public void Clear_VoidAccessor_OkKeepsGuardBool_NoOpRejectedProbe()
    {
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.Clear, OpsFqn + ".Clear");
        var src = CompileFull(BuildAsset(write, producer, itemsOut));

        // Plain call -- NOT reassigned into the Ok temp (void accessor; Ok stays the guard bool).
        Assert.Matches(@"(?<!= )" + Regex.Escape($"global::{OpsFqn}.Clear(ref __wc") + @"\d+\);", src);
        Assert.DoesNotContain($"= global::{OpsFqn}.Clear(", src);

        // Clear cannot be refused -- only the component-absent probe exists.
        Assert.DoesNotContain("\"op-rejected\"", src);
        Assert.Contains($"DebugProbe.CollectionWriteFailed(self, \"{write.Id:D}\", \"Clear\", \"component-absent\");", src);
    }

    // -----------------------------------------------------------------------
    // G4 defense in depth: self-bound even with a cross-entity producer
    // -----------------------------------------------------------------------

    [Fact]
    public void CrossEntityProducer_WriteStillBindsSelf()
    {
        // Producer's "Target" wired from an Entity variable -- Stage2 (BP2070) rejects this
        // wiring, so compile Stage3-7 only to prove the EMIT-side defense: the write's entity is a
        // temp assigned from `self`, never the producer's resolved entity.
        var (producer, itemsOut) = BuildProducer();
        var targetIn = DataPin("Target", "In", "Fdp.Core.Entity");
        producer.Pins.Add(targetIn);

        var entityVarId = Guid.NewGuid();
        var entityVar = new VariableDecl { Id = entityVarId, Name = "Other", Type = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var getVarOut = DataPin("Value", "Out", "Fdp.Core.Entity");
        var getVar = new GetVariableNode { Id = Guid.NewGuid(), VariableId = entityVarId.ToString() };
        getVar.Pins.Add(getVarOut);

        var write = BuildWriteNode(CollectionWriteOp.SetAt, OpsFqn + ".SetAt");
        var asset = BuildAsset(write, producer, itemsOut);
        asset.Variables.Add(entityVar);
        asset.Graphs[0].Nodes.Add(getVar);
        asset.Graphs[0].Links.Add(new Link
        {
            FromNodeId = getVar.Id, FromPinId = getVarOut.Id,
            ToNodeId = producer.Id, ToPinId = targetIn.Id,
        });

        var src = CompileSkippingValidate(asset);

        var rw = Regex.Match(src, @"ref var __wc\d+ = ref \S+\.GetComponentRW<global::Hrot\.AI\.Behaviors\.BpFixedListDemo>\((__t\d+)\);");
        Assert.True(rw.Success, "write RW fetch not found:\n" + src);
        Assert.Contains($"var {rw.Groups[1].Value} = self;", src);
    }

    // -----------------------------------------------------------------------
    // Probe gating + degraded paths
    // -----------------------------------------------------------------------

    [Fact]
    public void ReleaseMode_EmitsNoFailureProbes()
    {
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.SetAt, OpsFqn + ".SetAt");
        var src = CompileFull(BuildAsset(write, producer, itemsOut), CompilerMode.Release);

        Assert.DoesNotContain("CollectionWriteFailed", src);
        Assert.Contains($"global::{OpsFqn}.SetAt(ref __wc", src);   // the write itself still emits
    }

    [Fact]
    public void UnwiredCollection_DegradesToConstFalse_NoWriteEmitted()
    {
        var write = BuildWriteNode(CollectionWriteOp.SetAt, OpsFqn + ".SetAt");
        var src = CompileFull(BuildAsset(write, producer: null, producerOut: null, wireCollection: false));

        Assert.DoesNotContain("GetComponentRW", src);
        Assert.DoesNotContain(OpsFqn, src);
        Assert.Matches(@"var __t\d+ = false;", src);
    }

    [Fact]
    public void UnbakedAccessor_DegradesToConstFalse_NoWriteEmitted()
    {
        // Wired but WriteAccessorFqn empty -- Stage2 (BP2067) rejects this; Stage3-7 proves the
        // Stage5 degrade backstop.
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.SetAt, writeAccessorFqn: "");
        var src = CompileSkippingValidate(BuildAsset(write, producer, itemsOut));

        Assert.DoesNotContain("GetComponentRW<global::Hrot.AI.Behaviors.BpFixedListDemo", src);
        Assert.Matches(@"var __t\d+ = false;", src);
    }

    [Fact]
    public void MissingRequiredOperand_DegradesToConstFalse_NoWriteEmitted()
    {
        var (producer, itemsOut) = BuildProducer();
        var write = BuildWriteNode(CollectionWriteOp.SetAt, OpsFqn + ".SetAt");
        var src = CompileFull(BuildAsset(write, producer, itemsOut, wireValue: false));

        Assert.DoesNotContain($"global::{OpsFqn}.SetAt(", src);
        Assert.Matches(@"var __t\d+ = false;", src);
    }
}
