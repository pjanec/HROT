using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-01 (Slice 1a) -- GetComponent multi-pin (unmanaged) READ lowering. Proves the Stage5_Schedule
/// <c>GetComponentNode</c> case's new <c>Fields</c>-baked branch: the component is read ONCE via
/// <c>IrOp_GetComponentRO</c>, each baked field is projected via its own <c>IrOp_FieldRead</c>, and
/// "Found" is wired to a single <c>IrOp_HasComponent</c> -- the exact read-once-then-project idiom
/// multi-pin <c>GetShared</c> uses (mirrored by <see cref="Runtime.MultiPinSetSharedTests"/>'s sibling
/// read test), substituting <c>GetComponentRO</c>/<c>HasComponent</c> for <c>ReadShared</c>. Also
/// proves the Target-entity resolution (unwired =&gt; self via <c>IrOp_Self</c>; wired =&gt; the wired
/// entity, reused for BOTH the component read and the Found check) is untouched by the new branch --
/// it is the SAME resolution code the pre-existing legacy single-field path already used.
/// <para>
/// Uses <c>System.Numerics.Vector3</c> as the "component" (real, already-resolvable, zero-
/// Hrot.AI.Behaviors-dependency blittable struct -- mirrors <c>NodeCoverageTests.BuildGetComponentMinimalAsset</c>'s
/// rationale) -- <c>GetComponentRO&lt;T&gt;</c>/<c>HasComponent&lt;T&gt;</c> only require <c>T : unmanaged</c>
/// at compile time (no registration check), so this exercises the real lowering without pulling in
/// game assemblies. Runs Stage3-7 directly (skips Stage2_Validate, mirrors
/// <see cref="SpawnEqsSensorLoweringTests"/>'s <c>Compile</c> helper) -- no Roslyn/ALC needed.
/// </para>
/// </summary>
public sealed class GetComponentMultiPinLoweringTests
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
    /// EventEntry -&gt; SetVariable(FloatOut &lt;- GetComponent.X) -&gt; SetVariable(FoundOut &lt;-
    /// GetComponent.Found) -&gt; Return. GetComponent is a pure multi-pin node (Fields = [X, Y], both
    /// System.Single) -- "Y" is baked but deliberately left UNCONSUMED, to prove every baked field is
    /// read regardless of downstream wiring (mirrors multi-pin GetShared's unconditional per-field
    /// projection). When <paramref name="wireTarget"/>, Target is wired from a GetVariable(Entity);
    /// otherwise Target is left unwired (self-default).
    /// </summary>
    private static BlueprintAsset BuildAsset(bool wireTarget)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var gTarget = new Pin { Id = Guid.NewGuid(), Name = "Target", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var gX      = new Pin { Id = Guid.NewGuid(), Name = "X",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var gY      = new Pin { Id = Guid.NewGuid(), Name = "Y",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var gFound  = new Pin { Id = Guid.NewGuid(), Name = "Found",  Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var getComp = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "X", TypeId = "System.Single" },
                new ComponentFieldDecl { Name = "Y", TypeId = "System.Single" },
            },
        };
        getComp.Pins.AddRange(new[] { gTarget, gX, gY, gFound });

        var floatVarId = Guid.NewGuid();
        var floatVar   = new VariableDecl { Id = floatVarId, Name = "FloatOut", Type = new BlueprintTypeRef { TypeId = "System.Single" } };
        var boolVarId  = Guid.NewGuid();
        var boolVar    = new VariableDecl { Id = boolVarId, Name = "FoundOut", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var set1ExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var set1ExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true,  TypeRef = new() };
        var set1ValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var set1 = new SetVariableNode { Id = Guid.NewGuid(), VariableId = floatVarId.ToString() };
        set1.Pins.AddRange(new[] { set1ExecIn, set1ExecOut, set1ValueIn });

        var set2ExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var set2ExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true,  TypeRef = new() };
        var set2ValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var set2 = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        set2.Pins.AddRange(new[] { set2ExecIn, set2ExecOut, set2ValueIn });

        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        var ret   = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retIn);

        var nodes = new List<Node> { entry, getComp, set1, set2, ret };
        var links = new List<Link>
        {
            new() { FromNodeId = entry.Id,    FromPinId = entryOut.Id,     ToNodeId = set1.Id, ToPinId = set1ExecIn.Id },
            new() { FromNodeId = set1.Id,     FromPinId = set1ExecOut.Id,  ToNodeId = set2.Id, ToPinId = set2ExecIn.Id },
            new() { FromNodeId = set2.Id,     FromPinId = set2ExecOut.Id,  ToNodeId = ret.Id,  ToPinId = retIn.Id },
            new() { FromNodeId = getComp.Id,  FromPinId = gX.Id,           ToNodeId = set1.Id, ToPinId = set1ValueIn.Id },
            new() { FromNodeId = getComp.Id,  FromPinId = gFound.Id,       ToNodeId = set2.Id, ToPinId = set2ValueIn.Id },
        };

        var variables = new List<VariableDecl> { floatVar, boolVar };

        if (wireTarget)
        {
            var entityVarId = Guid.NewGuid();
            var entityVar   = new VariableDecl { Id = entityVarId, Name = "TargetEntity", Type = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
            variables.Add(entityVar);

            var getVarOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
            var getVar    = new GetVariableNode { Id = Guid.NewGuid(), VariableId = entityVarId.ToString() };
            getVar.Pins.Add(getVarOut);
            nodes.Add(getVar);

            links.Add(new Link { FromNodeId = getVar.Id, FromPinId = getVarOut.Id, ToNodeId = getComp.Id, ToPinId = gTarget.Id });
        }

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = nodes, Links = links, Inputs = new(), Outputs = new(),
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "GetComponentMultiPinTest",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = variables,
            Graphs    = { graph },
        };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void MultiPinRead_SelfDefault_ReadsComponentOnceAndProjectsEachField()
    {
        var source = Compile(BuildAsset(wireTarget: false));
        Assert.NotNull(source);

        // Self-default: unwired Target -> IrOp_Self ("var __tN = self;") feeds the read.
        Assert.Contains("var __t", source);
        Assert.Contains(" = self;", source);

        // The component is read EXACTLY ONCE via GetComponentRO<global::Vector3> (single call site) --
        // both baked fields (X, Y) project off that SAME read, not a fresh read each.
        int readCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                "GetComponentRO<global::System.Numerics.Vector3>")).Count;
        Assert.Equal(1, readCount);

        // Both baked fields are projected off that single read -- Y included even though UNCONSUMED
        // downstream (mirrors multi-pin GetShared's unconditional per-field projection).
        Assert.Matches(@"var __t\d+ = __t\d+\.X;", source);
        Assert.Matches(@"var __t\d+ = __t\d+\.Y;", source);

        // "Found" is wired to a single HasComponent<global::Vector3> check on the SAME entity value
        // used for the read (not a fresh Self/entity resolution).
        Assert.Contains("HasComponent<global::System.Numerics.Vector3>", source);
    }

    [Fact]
    public void MultiPinRead_WiredTarget_UsesWiredEntity_NotSelf()
    {
        var source = Compile(BuildAsset(wireTarget: true));
        Assert.NotNull(source);

        // No self-default: the wired Target entity feeds BOTH GetComponentRO and HasComponent, so
        // IrOp_Self is never emitted for this node.
        Assert.DoesNotContain(" = self;", source);

        int readCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                "GetComponentRO<global::System.Numerics.Vector3>")).Count;
        Assert.Equal(1, readCount);
        Assert.Contains("HasComponent<global::System.Numerics.Vector3>", source);
    }
}
