using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-104 — Stage 5's return terminator must be outputs-driven for Library dispatch.</b>
///
/// <para>
/// Before this fix, <c>BuildReturnTerminator</c> took the <c>AiPrimitive || Library</c> branch
/// unconditionally and emitted <see cref="IrTerm_ReturnStatus"/> for every Library function
/// regardless of declared outputs -- while three OTHER halves (<c>LibraryEmitter.CSharpReturnType</c>,
/// <c>CSharpEmitter.EmitLibraryFunctionAdapter</c>, and the BP-73 Library adapter test) already
/// derived the method's C# shape from <c>graph.Outputs</c>. A Library function that declares
/// outputs was therefore declared to return that type/tuple while its body executed
/// <c>return NodeStatus.Success;</c> -- CS0029. See
/// <see cref="Hrot.Blueprints.Tests.Compiler.BP73_MultipleFunctionOutputsTests"/> section 8 for the
/// Roslyn-level proof; these are the Stage-5 level tests pinning the terminator SHAPE itself.
/// </para>
/// <para>
/// ⚠ Zero-output Library returning <see cref="NodeStatus"/> is DELIBERATE and separately locked by
/// <see cref="BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn"/> (the
/// no-explicit-Return / <c>SealFallThrough</c> path). <see cref="LibraryWithZeroOutputs_ExplicitReturn_StillGetsStatus"/>
/// pins the same behaviour through the OTHER path -- an explicit <see cref="ReturnNode"/> reaching
/// <c>BuildReturnTerminator</c> directly.
/// </para>
/// </summary>
public sealed class BP104_LibraryReturnTerminatorTests
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

    private static (IrAsset ir, DiagnosticSink sink) RunSchedule(BlueprintAsset asset)
    {
        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }

    /// <summary>
    /// Builds a Function graph "Compute": Entry -&gt; Return, with an explicit <see cref="ReturnNode"/>
    /// carrying zero or more value pins, each wired to a literal. Mirrors
    /// <c>BP73_MultipleFunctionOutputsTests.MakeMultiOutputFunction</c> -- kept as an independent copy
    /// here rather than a cross-file reuse so this file exercises the same wiring shape as an
    /// unrelated test author would, not a shared fixture the tested code could special-case.
    /// </summary>
    private static Graph MakeReturnGraph(
        Guid id, NodeStatus status, params (string Name, string TypeId, string Literal)[] outputs)
    {
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var entryEx = Guid.NewGuid();
        var retEx   = Guid.NewGuid();

        var nodes = new List<Node>
        {
            new EventEntryNode
            {
                Id = entryId,
                Pins = new List<Pin>
                {
                    new() { Id = entryEx, Name = "ExecOut", Direction = "Out",
                            IsExec = true, TypeRef = new() },
                },
            },
        };
        var links = new List<Link>
        {
            new() { FromNodeId = entryId, FromPinId = entryEx, ToNodeId = retId, ToPinId = retEx },
        };

        var retPins = new List<Pin>
        {
            new() { Id = retEx, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
        };
        foreach (var (name, typeId, literal) in outputs)
        {
            var litId  = Guid.NewGuid();
            var litOut = Guid.NewGuid();
            var pinId  = Guid.NewGuid();

            nodes.Add(new LiteralNode
            {
                Id = litId, TypeId = typeId, ValueJson = literal,
                Pins = new List<Pin>
                {
                    new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = typeId } },
                },
            });
            retPins.Add(new Pin
            {
                Id = pinId, Name = name, Direction = "In", IsExec = false,
                TypeRef = new BlueprintTypeRef { TypeId = typeId },
            });
            links.Add(new Link
            {
                FromNodeId = litId, FromPinId = litOut, ToNodeId = retId, ToPinId = pinId,
            });
        }

        nodes.Add(new ReturnNode { Id = retId, Status = status, Pins = retPins });

        return new Graph
        {
            Id = id, Name = "Compute", Kind = GraphKind.Function,
            Inputs = new(),
            Outputs = outputs.Select(o => new ParameterDecl
            {
                Id = Guid.NewGuid(), Name = o.Name, Type = new BlueprintTypeRef { TypeId = o.TypeId },
            }).ToList(),
            Nodes = nodes, Links = links,
        };
    }

    private static BlueprintAsset MakeLibraryAsset(Graph graph) => new()
    {
        AssetId          = Guid.NewGuid(), Name = "Bp104Asset",
        Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new() { graph },
        Header           = new Header(),
    };

    /// <summary>
    /// BP-104 fact 2: a Library graph that DECLARES an output gets <see cref="IrTerm_Return"/>
    /// carrying that value -- not <see cref="IrTerm_ReturnStatus"/>.
    /// </summary>
    [Fact]
    public void LibraryWithDeclaredOutput_GetsValueReturn_NotStatus()
    {
        var graph = MakeReturnGraph(Guid.NewGuid(), NodeStatus.Success,
            ("Result", "System.Int32", "42"));

        var (ir, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        Assert.NotNull(retTerm.Value);
    }

    /// <summary>
    /// BP-104 fact 3: a Library graph with ZERO declared outputs, reaching an EXPLICIT
    /// <see cref="ReturnNode"/> (not the no-Return <c>SealFallThrough</c> path already pinned by
    /// <see cref="BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn"/>), still gets
    /// <see cref="IrTerm_ReturnStatus"/>(Success) -- the deliberate behaviour must not regress when
    /// the fix for BP-104 lands.
    /// </summary>
    [Fact]
    public void LibraryWithZeroOutputs_ExplicitReturn_StillGetsStatus()
    {
        var graph = MakeReturnGraph(Guid.NewGuid(), NodeStatus.Success); // no outputs declared

        var (ir, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_ReturnStatus>(lastBlock.Terminator);
        Assert.Equal(NodeStatus.Success, retTerm.Status);
    }
}
