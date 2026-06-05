using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
// Disambiguate: both Hrot.Blueprints.Core.Assets and Fdp.Toolkit.Blueprints define BlueprintDispatchKind.
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Unit tests for Stage0_Rehydrate — the compiler pin-rehydration pre-pass.
/// Covers the keystone invariant: pin-less nodes (Pins:[]) get their Pins
/// populated with the correct link GUIDs after Run().
/// </summary>
public sealed class Stage0_RehydrateTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

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
    /// Builds a minimal 2-node linked graph with Pins:[] on both nodes.
    ///
    /// Graph: EventEntry --execLink--> ReturnNode
    ///
    /// The link carries FromPinId / ToPinId that were authored in the editor
    /// but stripped when the asset was saved projection-only.
    /// After Stage0_Rehydrate.Run the pins must be restored with those exact GUIDs.
    /// </summary>
    private static (BlueprintAsset asset, Guid fromPinId, Guid toPinId,
                    Guid entryNodeId, Guid returnNodeId)
        BuildTwoNodeAsset()
    {
        var assetId      = Guid.NewGuid();
        var graphId      = Guid.NewGuid();
        var entryNodeId  = Guid.NewGuid();
        var returnNodeId = Guid.NewGuid();

        // These are the AUTHORED link-pin GUIDs — they live in the Link record
        // but NOT in Node.Pins when the asset is saved projection-only.
        var fromPinId = Guid.NewGuid();
        var toPinId   = Guid.NewGuid();

        var link = new Link
        {
            FromNodeId = entryNodeId,
            FromPinId  = fromPinId,
            ToNodeId   = returnNodeId,
            ToPinId    = toPinId,
        };

        var entryNode = new EventEntryNode
        {
            Id   = entryNodeId,
            Pins = new List<Pin>(),   // stripped — mimic projection-only save
        };

        var returnNode = new ReturnNode
        {
            Id     = returnNodeId,
            Status = NodeStatus.Success,
            Pins   = new List<Pin>(), // stripped
        };

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { entryNode, returnNode },
            Links = new List<Link> { link },
            Inputs  = new(),
            Outputs = new(),
        };

        var asset = new BlueprintAsset
        {
            AssetId      = assetId,
            Name         = "TwoNodeTest",
            Dispatch     = BlueprintDispatchKind.Instance,
            Graphs       = new List<Graph> { graph },
            Variables    = new(),
            CustomEvents = new(),
        };

        return (asset, fromPinId, toPinId, entryNodeId, returnNodeId);
    }

    // ── Helper for single-node assets ─────────────────────────────────────────

    private static BlueprintAsset MakeSingleNodeAsset(Node node,
        List<VariableDecl>? variables = null)
    {
        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { node },
            Links = new List<Link>(),
            Inputs  = new(),
            Outputs = new(),
        };
        return new BlueprintAsset
        {
            AssetId      = Guid.NewGuid(),
            Name         = "TestAsset",
            Dispatch     = BlueprintDispatchKind.Instance,
            Graphs       = new List<Graph> { graph },
            Variables    = variables ?? new(),
            CustomEvents = new(),
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// STAGE0-001: after Run, every pin-less node has its Pins list populated.
    /// </summary>
    [Fact]
    public void Run_PinlessNodes_PinsPopulated()
    {
        var (asset, _, _, _, _) = BuildTwoNodeAsset();
        var graph      = asset.Graphs[0];
        var entryNode  = graph.Nodes[0];
        var returnNode = graph.Nodes[1];

        // Pre-condition: both nodes have no pins.
        Assert.Empty(entryNode.Pins);
        Assert.Empty(returnNode.Pins);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        // Post-condition: both nodes have at least one pin.
        Assert.NotEmpty(entryNode.Pins);
        Assert.NotEmpty(returnNode.Pins);
    }

    /// <summary>
    /// STAGE0-002: the exec-Out pin of EventEntryNode is assigned the FromPinId from the link.
    /// </summary>
    [Fact]
    public void Run_EventEntryExecOut_CarriesLinkFromPinId()
    {
        var (asset, fromPinId, _, _, _) = BuildTwoNodeAsset();
        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var entryNode = asset.Graphs[0].Nodes[0];

        // EventEntryNode has a single exec-Out "Out" pin.
        var execOutPin = entryNode.Pins.FirstOrDefault(p => p.Direction == "Out" && p.IsExec);
        Assert.NotNull(execOutPin);
        Assert.Equal(fromPinId, execOutPin!.Id);
    }

    /// <summary>
    /// STAGE0-003: the exec-In pin of ReturnNode is assigned the ToPinId from the link.
    /// </summary>
    [Fact]
    public void Run_ReturnNodeExecIn_CarriesLinkToPinId()
    {
        var (asset, _, toPinId, _, _) = BuildTwoNodeAsset();
        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var returnNode = asset.Graphs[0].Nodes[1];

        // ReturnNode has a single exec-In "In" pin.
        var execInPin = returnNode.Pins.FirstOrDefault(p => p.Direction == "In" && p.IsExec);
        Assert.NotNull(execInPin);
        Assert.Equal(toPinId, execInPin!.Id);
    }

    /// <summary>
    /// STAGE0-004: nodes that already have pins are skipped (idempotency guard).
    /// </summary>
    [Fact]
    public void Run_NodeWithExistingPins_Skipped()
    {
        var (asset, _, _, _, _) = BuildTwoNodeAsset();
        var graph     = asset.Graphs[0];
        var entryNode = graph.Nodes[0];

        // Pre-populate the entry node with a sentinel pin.
        var sentinelPinId = Guid.NewGuid();
        entryNode.Pins.Add(new Pin
        {
            Id        = sentinelPinId,
            Name      = "Out",
            Direction = "Out",
            IsExec    = true,
            TypeRef   = new BlueprintTypeRef(),
        });

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        // Entry node must still have only the sentinel pin (not rehydrated).
        Assert.Single(entryNode.Pins);
        Assert.Equal(sentinelPinId, entryNode.Pins[0].Id);
    }

    /// <summary>
    /// STAGE0-005: BranchNode — exec-In "In", exec-Out "True", exec-Out "False",
    /// data-In "Condition"/System.Boolean — all four pins present after Run.
    /// </summary>
    [Fact]
    public void Run_BranchNode_FourPinsPresent()
    {
        var branchNode = new BranchNode { Id = Guid.NewGuid(), Pins = new List<Pin>() };
        var asset = MakeSingleNodeAsset(branchNode);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        Assert.Equal(4, branchNode.Pins.Count);

        // exec-In "In"
        Assert.Single(branchNode.Pins, p => p.Direction == "In" && p.IsExec);

        // exec-Out "True" and exec-Out "False"
        var execOuts = branchNode.Pins.Where(p => p.Direction == "Out" && p.IsExec).ToList();
        Assert.Equal(2, execOuts.Count);
        Assert.Contains(execOuts, p => p.Name == "True");
        Assert.Contains(execOuts, p => p.Name == "False");

        // data-In "Condition" typed System.Boolean
        var condPin = branchNode.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
        Assert.NotNull(condPin);
        Assert.Equal("Condition", condPin!.Name);
        Assert.Equal("System.Boolean", condPin.TypeRef?.TypeId);
    }

    /// <summary>
    /// STAGE0-006: WhenNode — exec-Out names "OnFired"/"OnEnded"/"Out" are load-bearing
    /// (Stage5_Schedule.GetWhenExecSuccessor matches by name). Verify all three are present.
    /// </summary>
    [Fact]
    public void Run_WhenNode_LoadBearingExecOutNames()
    {
        var whenNode = new WhenNode { Id = Guid.NewGuid(), Pins = new List<Pin>() };
        var asset = MakeSingleNodeAsset(whenNode);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var execOutNames = whenNode.Pins
            .Where(p => p.Direction == "Out" && p.IsExec)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("OnFired", execOutNames);
        Assert.Contains("OnEnded", execOutNames);
        Assert.Contains("Out",     execOutNames);
    }

    /// <summary>
    /// STAGE0-007: GetVariable pin — data-Out "Value" typed from asset.Variables.
    /// </summary>
    [Fact]
    public void Run_GetVariableNode_DataOutTypedFromVariable()
    {
        var varId = Guid.NewGuid();
        var getVarNode = new GetVariableNode
        {
            Id         = Guid.NewGuid(),
            VariableId = $"var:{varId:D}",
            Pins       = new List<Pin>(),
        };
        var varDecl = new VariableDecl
        {
            Id   = varId,
            Name = "Count",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        var asset = MakeSingleNodeAsset(getVarNode, variables: new List<VariableDecl> { varDecl });

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        Assert.Single(getVarNode.Pins);
        var valPin = getVarNode.Pins[0];
        Assert.Equal("Value",        valPin.Name);
        Assert.Equal("Out",          valPin.Direction);
        Assert.False(valPin.IsExec);
        Assert.Equal("System.Int32", valPin.TypeRef?.TypeId);
    }

    /// <summary>
    /// STAGE0-008: LiteralNode — data-Out "Value" typed by LiteralNode.TypeId.
    /// </summary>
    [Fact]
    public void Run_LiteralNode_DataOutTypedByLiteralTypeId()
    {
        var litNode = new LiteralNode
        {
            Id        = Guid.NewGuid(),
            TypeId    = "System.Int32",
            ValueJson = "1",
            Pins      = new List<Pin>(),
        };
        var asset = MakeSingleNodeAsset(litNode);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        Assert.Single(litNode.Pins);
        var valPin = litNode.Pins[0];
        Assert.Equal("Value",        valPin.Name);
        Assert.Equal("Out",          valPin.Direction);
        Assert.False(valPin.IsExec);
        Assert.Equal("System.Int32", valPin.TypeRef?.TypeId);
    }

    /// <summary>
    /// STAGE0-009: SetVariable — exec In/Out + data-In "Value" + data-Out "Value",
    /// both typed from the variable.
    /// </summary>
    [Fact]
    public void Run_SetVariableNode_FourPinsTypedFromVariable()
    {
        var varId = Guid.NewGuid();
        var setVarNode = new SetVariableNode
        {
            Id         = Guid.NewGuid(),
            VariableId = varId.ToString("D"),
            Pins       = new List<Pin>(),
        };
        var varDecl = new VariableDecl
        {
            Id   = varId,
            Name = "Count",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };
        var asset = MakeSingleNodeAsset(setVarNode, variables: new List<VariableDecl> { varDecl });

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        // exec-In "In", exec-Out "Out", data-In "Value", data-Out "Value"
        Assert.Equal(4, setVarNode.Pins.Count);

        Assert.Single(setVarNode.Pins, p => p.IsExec && p.Direction == "In");
        Assert.Single(setVarNode.Pins, p => p.IsExec && p.Direction == "Out");

        var dataIn  = setVarNode.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
        var dataOut = setVarNode.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");

        Assert.NotNull(dataIn);
        Assert.NotNull(dataOut);
        Assert.Equal("Value",        dataIn!.Name);
        Assert.Equal("Value",        dataOut!.Name);
        Assert.Equal("System.Int32", dataIn.TypeRef?.TypeId);
        Assert.Equal("System.Int32", dataOut.TypeRef?.TypeId);
    }

    /// <summary>
    /// STAGE0-010: fan-out — single FromPinId used by two links; positional algorithm
    /// must deduplicate and assign the SAME GUID to the one exec-Out pin.
    /// </summary>
    [Fact]
    public void Run_FanOutLinks_OutPinReceivesFirstOccurrenceGuid()
    {
        var assetId      = Guid.NewGuid();
        var graphId      = Guid.NewGuid();
        var entryNodeId  = Guid.NewGuid();
        var recv1Id      = Guid.NewGuid();
        var recv2Id      = Guid.NewGuid();
        var sharedFromId = Guid.NewGuid();

        var entryNode = new EventEntryNode { Id = entryNodeId, Pins = new List<Pin>() };

        // Both links share the same FromPinId (fan-out pattern).
        var link1 = new Link { FromNodeId = entryNodeId, FromPinId = sharedFromId,
                               ToNodeId   = recv1Id,     ToPinId   = Guid.NewGuid() };
        var link2 = new Link { FromNodeId = entryNodeId, FromPinId = sharedFromId,
                               ToNodeId   = recv2Id,     ToPinId   = Guid.NewGuid() };

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Event,
            Nodes = new List<Node> { entryNode },
            Links = new List<Link> { link1, link2 },
            Inputs  = new(),
            Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId      = assetId,
            Name         = "FanOutTest",
            Dispatch     = BlueprintDispatchKind.Instance,
            Graphs       = new List<Graph> { graph },
            Variables    = new(),
            CustomEvents = new(),
        };

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        // EventEntryNode has one exec-Out pin; it should carry sharedFromId.
        var execOutPin = entryNode.Pins.FirstOrDefault(p => p.Direction == "Out" && p.IsExec);
        Assert.NotNull(execOutPin);
        Assert.Equal(sharedFromId, execOutPin!.Id);
    }
}
