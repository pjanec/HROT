using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-03 (Slice W1) -- SetComponent multi-pin (unmanaged) WRITE lowering. Proves the Stage5_Schedule
/// <c>SetComponentNode</c> case + the new <c>IrOp_WriteComponentFields</c> emit: a SINGLE guarded
/// block -- <c>HasComponent&lt;T&gt;</c> drives both the "Written" data-out and the write guard,
/// <c>GetComponentRW&lt;T&gt;</c> is fetched only INSIDE that guard, and only the WIRED field ("X")
/// is assigned -- the deliberately-UNWIRED field ("Y") never appears in the emitted C# at all
/// ("unwired preserved"). Self-only by construction -- entity is always <c>self</c> via
/// <c>IrOp_Self</c>, and there is no "Target" pin on this node kind at all.
/// <para>
/// Uses <c>System.Numerics.Vector3</c> as the "component" (real, already-resolvable, zero-
/// Hrot.AI.Behaviors-dependency blittable struct with MUTABLE public fields -- mirrors
/// <see cref="GetComponentMultiPinLoweringTests"/>'s rationale) -- <c>GetComponentRW&lt;T&gt;</c>/
/// <c>HasComponent&lt;T&gt;</c> only require <c>T : unmanaged</c> at compile time (no registration
/// check). Runs Stage3-7 directly (skips Stage2_Validate, mirrors
/// <see cref="GetComponentMultiPinLoweringTests"/>'s <c>Compile</c> helper) -- no Roslyn/ALC needed.
/// </para>
/// </summary>
public sealed class SetComponentWriteLoweringTests
{
    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs all stages (skipping Stage 2) and returns the generated C# source.</summary>
    private static string? Compile(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        asset       = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(asset, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (source, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        return sink.HasErrors ? null : source;
    }

    // -----------------------------------------------------------------------
    // Asset builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// EventEntry -&gt; SetComponent(Fields=[X, Y]; only "X" wired from a Literal 5.5f; "Written" wired
    /// to SetVariable(WrittenOut)) -&gt; Return. "Y" is baked but deliberately left UNWIRED, to prove
    /// it is never assigned (mirrors multi-pin SetShared's runtime "unwired preserved" proof, but as
    /// an emitted-C# assertion instead of a live ECS read-back).
    /// </summary>
    private static BlueprintAsset BuildAsset()
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var litOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Single", ValueJson = "5.5f" };
        lit.Pins.Add(litOut);

        var sExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var sExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var sX       = new Pin { Id = Guid.NewGuid(), Name = "X",       Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var sY       = new Pin { Id = Guid.NewGuid(), Name = "Y",       Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var sWritten = new Pin { Id = Guid.NewGuid(), Name = "Written", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var setComp = new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "X", TypeId = "System.Single" },
                new ComponentFieldDecl { Name = "Y", TypeId = "System.Single" },
            },
        };
        setComp.Pins.AddRange(new[] { sExecIn, sExecOut, sX, sY, sWritten });

        var boolVarId = Guid.NewGuid();
        var boolVar   = new VariableDecl { Id = boolVarId, Name = "WrittenOut", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var setVarExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var setVarExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var setVarValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setVar.Pins.AddRange(new[] { setVarExecIn, setVarExecOut, setVarValueIn });

        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        var ret   = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retIn);

        var nodes = new List<Node> { entry, lit, setComp, setVar, ret };
        var links = new List<Link>
        {
            new() { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setComp.Id, ToPinId = sExecIn.Id },
            new() { FromNodeId = setComp.Id, FromPinId = sExecOut.Id,     ToNodeId = setVar.Id,  ToPinId = setVarExecIn.Id },
            new() { FromNodeId = setVar.Id,  FromPinId = setVarExecOut.Id, ToNodeId = ret.Id,    ToPinId = retIn.Id },
            new() { FromNodeId = lit.Id,     FromPinId = litOut.Id,       ToNodeId = setComp.Id, ToPinId = sX.Id },
            new() { FromNodeId = setComp.Id, FromPinId = sWritten.Id,     ToNodeId = setVar.Id,  ToPinId = setVarValueIn.Id },
            // "Y" deliberately left unwired -- no link to sY.
        };

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = nodes, Links = links, Inputs = new(), Outputs = new(),
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "SetComponentWriteTest",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = new List<VariableDecl> { boolVar },
            Graphs    = { graph },
        };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Write_WiredFieldOnly_EmitsGuardedBlock_UnwiredFieldNeverAssigned()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        // Self-only: entity is always `self` (IrOp_Self), never a "Target" resolution.
        Assert.Contains(" = self;", source);

        // HasComponent guards the write; the SAME check drives "Written" (single call site, no
        // separate/duplicate HasComponent evaluation for the guard vs. the pin).
        int hasCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                "HasComponent<global::System.Numerics.Vector3>")).Count;
        Assert.Equal(1, hasCount);

        // GetComponentRW is fetched exactly once, and only INSIDE the guard (never at top level).
        int rwCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                "GetComponentRW<global::System.Numerics.Vector3>")).Count;
        Assert.Equal(1, rwCount);

        // Guarded-block shape: `var __tN = ...HasComponent...; if (__tN) { ref var __wcN = ref
        // ...GetComponentRW...; ... }`.
        Assert.Matches(@"var __t\d+ = \S+\.HasComponent<global::System\.Numerics\.Vector3>\(__t\d+\);", source);
        Assert.Matches(@"if \(__t\d+\)", source);
        Assert.Matches(@"ref var __wc\d+ = ref \S+\.GetComponentRW<global::System\.Numerics\.Vector3>\(__t\d+\);", source);

        // Only the WIRED field ("X") is assigned.
        Assert.Matches(@"__wc\d+\.X = __t\d+;", source);

        // The UNWIRED field ("Y") is never assigned -- "unwired preserved".
        Assert.DoesNotMatch(@"__wc\d+\.Y\s*=", source);
        Assert.DoesNotContain(".Y =", source);
    }

    [Fact]
    public void Write_NoFieldsWired_StillEmitsGuard_WithEmptyBody()
    {
        var asset = BuildAsset();
        // Strip the X wiring (leave "Written" wired) -- zero wired fields at all.
        var setComp = (SetComponentNode)asset.Graphs[0].Nodes.First(n => n is SetComponentNode);
        var xPin = setComp.Pins.First(p => p.Name == "X");
        asset.Graphs[0].Links.RemoveAll(l => l.ToNodeId == setComp.Id && l.ToPinId == xPin.Id);

        var source = Compile(asset);
        Assert.NotNull(source);

        // The guard still exists (Written must still reflect HasComponent)...
        Assert.Matches(@"if \(__t\d+\)", source);
        // ...but GetComponentRW is never fetched (nothing to write).
        Assert.DoesNotContain("GetComponentRW<global::System.Numerics.Vector3>", source);
    }
}
