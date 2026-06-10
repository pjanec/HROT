using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using BlueprintAsset     = Hrot.Blueprints.Core.Assets.BlueprintAsset;
using Graph              = Hrot.Blueprints.Core.Assets.Graph;
using GraphKind          = Hrot.Blueprints.Core.Assets.GraphKind;
using Node               = Hrot.Blueprints.Core.Assets.Node;
using EventEntryNode     = Hrot.Blueprints.Core.Assets.EventEntryNode;
using ReturnNode         = Hrot.Blueprints.Core.Assets.ReturnNode;
using SetVariableNode    = Hrot.Blueprints.Core.Assets.SetVariableNode;
using GetVariableNode    = Hrot.Blueprints.Core.Assets.GetVariableNode;
using LiteralNode        = Hrot.Blueprints.Core.Assets.LiteralNode;
using Pin                = Hrot.Blueprints.Core.Assets.Pin;
using Link               = Hrot.Blueprints.Core.Assets.Link;
using NodeStatus         = Hrot.Blueprints.Core.Assets.NodeStatus;
using BlueprintTypeRef   = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using VariableDecl       = Hrot.Blueprints.Core.Assets.VariableDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BF-02: Verifies that Stage5_Schedule.FindVariableIndex strips the "var:" prefix
/// from VariableId before Guid.TryParse, so SetVariable/GetVariable nodes authored
/// with the My-Blueprint item-id format resolve to the correct variable index.
/// </summary>
public sealed class Stage5VarPrefixResolutionTests
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

    /// <summary>
    /// BF-02 primary regression test: a SetVariableNode with a "var:&lt;Guid&gt;"
    /// VariableId must resolve to the correct variable index, emit the real field
    /// (e.g. s.Count = ...), and must NOT produce the broken __var_-1 sentinel.
    /// </summary>
    [Fact]
    public void SetVariable_VarPrefixedId_EmitsRealFieldName_NotVarMinusOne()
    {
        // -- Build asset with one declared variable: Count (System.Int32) --------
        var asset = BlueprintAssetBuilder
            .Instance("VarPrefixSetTest")
            .WithVariable("Count", typeof(int))
            .Build();

        var countDecl = Assert.Single(asset.Variables);
        Assert.Equal("Count", countDecl.Name);

        // -- Build graph: EventEntry → SetVariable(var:<guid>) → Return ----------
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var litId    = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        // Pin IDs
        var entryOutId  = Guid.NewGuid();
        var setInId     = Guid.NewGuid();
        var setOutId    = Guid.NewGuid();
        var setValueId  = Guid.NewGuid();
        var litOutId    = Guid.NewGuid();
        var retInId     = Guid.NewGuid();

        var graph = new Graph
        {
            Id   = graphId,
            Name = "EventGraph",
            Kind = GraphKind.Event,
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryOutId, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = $"var:{countDecl.Id}",   // ← the prefixed form under test
                    Pins = new List<Pin>
                    {
                        new() { Id = setInId,    Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = setOutId,   Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = setValueId, Name = "Value",   Direction = "In",  IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new LiteralNode
                {
                    Id        = litId,
                    TypeId    = "System.Int32",
                    ValueJson = "7",
                    Pins = new List<Pin>
                    {
                        new() { Id = litOutId, Name = "Value", Direction = "Out", IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new() { Id = retInId, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                // Entry exec-out → SetVariable exec-in
                new() { FromNodeId = entryId,  FromPinId = entryOutId, ToNodeId = setVarId, ToPinId = setInId },
                // SetVariable exec-out → Return exec-in
                new() { FromNodeId = setVarId, FromPinId = setOutId,   ToNodeId = retId,    ToPinId = retInId },
                // Literal value-out → SetVariable value-in
                new() { FromNodeId = litId,    FromPinId = litOutId,   ToNodeId = setVarId, ToPinId = setValueId },
            },
            Inputs  = new(),
            Outputs = new(),
        };

        asset.Graphs.Add(graph);

        // -- Compile through Stage5 → Stage7 ------------------------------------
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();

        // Stage2 validates, Stage3 normalizes, Stage4 resolves types
        var ctx   = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm  = Stage3_Normalize.Run(asset, ctx);
        var typed = Stage4_TypeResolve.Run(norm, ctx);

        // Stage5 is the stage under test — schedules IR with variable indices
        var ir = Stage5_Schedule.Run(typed, ctx);

        // Stage6 lowers, Stage7 emits C# source
        var low     = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(low, CompilerMode.Debug, sink);

        // -- Assertions ---------------------------------------------------------
        // No compile errors.
        Assert.False(sink.HasErrors,
            $"Compile errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        // Regression guard: generated source must NOT contain the broken sentinel.
        Assert.DoesNotContain("__var_-1", src);

        // The generated source must write to the real variable field.
        Assert.Contains("s.Count", src);
    }

    /// <summary>
    /// BF-02 parallel case: a GetVariableNode with a "var:&lt;Guid&gt;"
    /// VariableId must also resolve correctly (the same FindVariableIndex path).
    /// </summary>
    [Fact]
    public void GetVariable_VarPrefixedId_EmitsRealFieldName_NotVarMinusOne()
    {
        var asset = BlueprintAssetBuilder
            .Instance("VarPrefixGetTest")
            .WithVariable("Speed", typeof(float))
            .Build();

        var speedDecl = Assert.Single(asset.Variables);
        Assert.Equal("Speed", speedDecl.Name);

        // Graph: EventEntry → SetVariable (writes s.Speed = 3.0f) → Return
        // The SetVariable value comes from a GetVariable that reads s.Speed with
        // a var:-prefixed id — proving GetVariable resolves correctly.
        var graphId    = Guid.NewGuid();
        var entryId    = Guid.NewGuid();
        var getVarId   = Guid.NewGuid();
        var setVarId   = Guid.NewGuid();
        var retId      = Guid.NewGuid();

        var entryOutId = Guid.NewGuid();
        var getOutId   = Guid.NewGuid();
        var setInId    = Guid.NewGuid();
        var setOutId   = Guid.NewGuid();
        var setValInId = Guid.NewGuid();
        var retInId    = Guid.NewGuid();

        // The GetVariable node: pure (no exec pins) — its output is the variable value.
        var getNode = new GetVariableNode
        {
            Id         = getVarId,
            VariableId = $"var:{speedDecl.Id}",
            Pins = new List<Pin>
            {
                new() { Id = getOutId, Name = "Value", Direction = "Out", IsExec = false,
                    TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } },
            },
        };

        var graph = new Graph
        {
            Id   = graphId,
            Name = "EventGraph",
            Kind = GraphKind.Event,
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryOutId, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                getNode,
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = speedDecl.Id.ToString(),   // SetVariable uses GUID directly (not var: prefix)
                    Pins = new List<Pin>
                    {
                        new() { Id = setInId,    Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = setOutId,   Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = setValInId, Name = "Value",   Direction = "In",  IsExec = false,
                            TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins = new List<Pin>
                    {
                        new() { Id = retInId, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                // Entry exec-out → SetVariable exec-in
                new() { FromNodeId = entryId,  FromPinId = entryOutId, ToNodeId = setVarId, ToPinId = setInId },
                // SetVariable exec-out → Return exec-in
                new() { FromNodeId = setVarId, FromPinId = setOutId,   ToNodeId = retId,    ToPinId = retInId },
                // GetVariable value-out → SetVariable value-in
                new() { FromNodeId = getVarId, FromPinId = getOutId,   ToNodeId = setVarId, ToPinId = setValInId },
            },
            Inputs  = new(),
            Outputs = new(),
        };

        asset.Graphs.Add(graph);

        // Compile.
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm  = Stage3_Normalize.Run(asset, ctx);
        var typed = Stage4_TypeResolve.Run(norm, ctx);
        var ir    = Stage5_Schedule.Run(typed, ctx);
        var low     = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(low, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Compile errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        // The GetVariable read should NOT produce __var_-1.
        Assert.DoesNotContain("__var_-1", src);

        // The generated source must read the real variable field.
        Assert.Contains("s.Speed", src);
    }
}
