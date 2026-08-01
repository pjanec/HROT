using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Stages;
using NodeEditor.Primitives;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Blocker-1 part 2 (architect Q#10-A/C option 1): the deterministic-first, positional-fallback pin
/// reconstruction. Guards the parity with the editor's <see cref="IdGenerator.Deterministic"/>, the
/// order-independence that kills the old exec/data-bucket swap, the mixed-asset case that broke the naive
/// "any-link-migrated" attempt, byte-for-byte legacy behavior, and the catalog-driven PublishEvent enricher.
/// All game-free.
/// </summary>
public sealed class DeterministicPinReconstructionTests
{
    // ── parity ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("In", "In")]
    [InlineData("Condition", "In")]
    [InlineData("Return", "Out")]
    public void DeterministicPinId_MatchesEditorIdGenerator(string name, string dir)
    {
        var node = new Guid("ae000000-0000-0000-0000-000000000001");
        Assert.Equal(IdGenerator.Deterministic($"pin:{node:N}:{name}:{dir}"),
                     DeterministicIds.PinId(node, name, dir));
    }

    // ── reconstruction harness ──────────────────────────────────────────────────
    private static CompileOptions Options(
        IEngineEventCatalog? events = null, IChannelCommandCatalog? channelCommands = null) =>
        new CompileOptions(
            CompilerMode.Debug, BuiltInNodeRegistry.Instance, StaticTypeRegistry.Instance,
            events ?? BuiltInEngineEventCatalog.Instance, channelCommands ?? BuiltInChannelCommandCatalog.Instance,
            BuiltInWaitPrimitiveCatalog.Instance, Array.Empty<BlueprintSignature>());

    private static (BlueprintAsset asset, Node node) RunBranch(List<Link> links)
    {
        var branchId = Guid.NewGuid();
        var branch = new BranchNode { Id = branchId, Pins = new List<Pin>() };
        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { branch }, Links = links, Inputs = new(), Outputs = new(),
        };
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "T", Dispatch = BlueprintDispatchKind.Instance,
            Graphs = new List<Graph> { graph }, Variables = new(), CustomEvents = new(),
        };
        Stage0_Rehydrate.Run(asset, Options());
        return (asset, branch);
    }

    private static Guid PinId(Node n, string name) => n.Pins.First(p => p.Name == name).Id;

    [Fact]
    public void Deterministic_SwappedLinkOrder_DoesNotSwapExecAndData()
    {
        // Branch In-bucket = [In(exec), Condition(data)]. Links arrive Condition-FIRST (the order that
        // made the old positional scheme bind In<-Condition's GUID). With deterministic GUIDs each binds
        // its own pin by name regardless of order.
        var branchId = Guid.NewGuid();
        // We don't know branchId before construction, so build links after — do it via a fixed id.
        var id = new Guid("bb000000-0000-0000-0000-0000000000aa");
        var inDet   = DeterministicIds.PinId(id, "In", "In");
        var condDet = DeterministicIds.PinId(id, "Condition", "In");
        var branch = new BranchNode { Id = id, Pins = new List<Pin>() };
        var links = new List<Link>
        {
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = condDet },
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = inDet },
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { branch }, Links = links, Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        Assert.Equal(inDet, PinId(branch, "In"));
        Assert.Equal(condDet, PinId(branch, "Condition"));
        _ = branchId;
    }

    [Fact]
    public void Mixed_DeterministicAndLegacyLinks_BothResolve()
    {
        // One pin carries its deterministic GUID (a link drawn to a pin that had been saved unconnected);
        // another pin is bound by a legacy/arbitrary link GUID. Both must resolve.
        var id = new Guid("cc000000-0000-0000-0000-0000000000bb");
        var condDet = DeterministicIds.PinId(id, "Condition", "In");
        var legacyExec = new Guid("11111111-2222-3333-4444-555555555555");
        var branch = new BranchNode { Id = id, Pins = new List<Pin>() };
        var links = new List<Link>
        {
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = condDet },
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = legacyExec },
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { branch }, Links = links, Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        Assert.Equal(condDet, PinId(branch, "Condition"));   // deterministic bind by name
        Assert.Equal(legacyExec, PinId(branch, "In"));        // legacy positional to the remaining In pin
    }

    [Fact]
    public void Legacy_AllArbitraryLinks_PositionalUnchanged()
    {
        var id = new Guid("dd000000-0000-0000-0000-0000000000cc");
        var execGuid = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        var condGuid = new Guid("aaaaaaaa-0000-0000-0000-000000000002");
        var branch = new BranchNode { Id = id, Pins = new List<Pin>() };
        // In canonical order [In, Condition]; links in that same order → positional binds In<-exec, Condition<-cond.
        var links = new List<Link>
        {
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = execGuid },
            new() { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(), ToNodeId = id, ToPinId = condGuid },
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { branch }, Links = links, Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        Assert.Equal(execGuid, PinId(branch, "In"));
        Assert.Equal(condGuid, PinId(branch, "Condition"));
    }

    // ── PublishEvent enricher ────────────────────────────────────────────────────
    private sealed class StubEventCatalog : IEngineEventCatalog
    {
        private readonly EngineEventCatalogEntry _e;
        public StubEventCatalog(EngineEventCatalogEntry e) => _e = e;
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => new[] { _e };
    }

    [Fact]
    public void PublishEvent_RehydratesTargetAndPayloadPins_FromCatalog()
    {
        var entry = new EngineEventCatalogEntry(
            Name: "AssignTacticalIntentEvent", EventTypeFqn: "Ns.AssignTacticalIntentEvent",
            TargetFieldName: "Entity",
            PayloadFields: new EventPayloadField[]
            {
                new("IntentId", "System.String"),
                new("JsonParams", "System.String"),
            });

        var pen = new PublishEventNode { Id = Guid.NewGuid(), EventId = "AssignTacticalIntentEvent", Pins = new List<Pin>() };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { pen }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.AiPrimitive, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options(new StubEventCatalog(entry)));

        var shape = pen.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Contains(("In", "In", true, ""), shape);
        Assert.Contains(("Out", "Out", true, ""), shape);
        Assert.Contains(("Target", "In", false, "Fdp.Core.Entity"), shape);
        Assert.Contains(("IntentId", "In", false, "System.String"), shape);
        Assert.Contains(("JsonParams", "In", false, "System.String"), shape);
    }

    // ── ChannelCommand enricher ──────────────────────────────────────────────────
    private sealed class StubChannelCommandCatalog : IChannelCommandCatalog
    {
        private readonly ChannelCommandCatalogEntry _e;
        public StubChannelCommandCatalog(ChannelCommandCatalogEntry e) => _e = e;
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => new[] { _e };
    }

    [Fact]
    public void ChannelCommand_RehydratesParamPins_FromCatalog()
    {
        var entry = new ChannelCommandCatalogEntry(
            Name: "MoveTo", ChannelTypeFqn: "Fdp.Toolkit.Behavior.Components.LocomotionChannel",
            ActionId: 1, ParamsTypeFqn: "Fdp.Toolkit.Navigation.MoveToParams",
            ParamFields: new ParamField[]
            {
                new("Destination",   "System.Numerics.Vector3"),
                new("ArrivalRadius", "System.Single"),
                new("Speed",         "System.Single"),
            });

        var cc = new ChannelCommandNode
        {
            Id = Guid.NewGuid(), ChannelType = "LocomotionChannel", ActionId = "MoveTo",
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { cc }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.AiPrimitive, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options(channelCommands: new StubChannelCommandCatalog(entry)));

        var shape = cc.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Contains(("In", "In", true, ""), shape);
        Assert.Contains(("Out", "Out", true, ""), shape);
        Assert.Contains(("Destination", "In", false, "System.Numerics.Vector3"), shape);
        Assert.Contains(("ArrivalRadius", "In", false, "System.Single"), shape);
        Assert.Contains(("Speed", "In", false, "System.Single"), shape);
    }

    [Fact]
    public void ChannelCommand_UnknownAction_FallsBackToExecOnly()
    {
        var cc = new ChannelCommandNode
        {
            Id = Guid.NewGuid(), ChannelType = "LocomotionChannel", ActionId = "NoSuchAction",
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { cc }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.AiPrimitive, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset,
            Options(channelCommands: new StubChannelCommandCatalog(
                new ChannelCommandCatalogEntry("Other", "Foo.BarChannel", 1, "Foo.BarParams"))));

        Assert.Equal(new[] { ("In", "In"), ("Out", "Out") },
            cc.Pins.Select(p => (p.Name, p.Direction)).ToArray());
    }

    // ── Compare/BinaryOp/BooleanOp/Not static shape (Blocker-1 tail: pin-less pure nodes) ──────────
    // These pure nodes used to return no static pins (on the assumption assets always author them). A
    // migrated pin-less asset then produced EMPTY pins, so Stage5 (which reads "A"/"B" by name) dropped
    // the Compare's operand producers and emitted an undefined SSA temp (CS0103). They now carry a static
    // A/B/Result shape.
    [Theory]
    [InlineData(typeof(CompareNode))]
    [InlineData(typeof(BinaryOpNode))]
    [InlineData(typeof(BooleanOpNode))]
    public void PureOperatorNode_Pinless_RehydratesABResult(Type nodeType)
    {
        var node = (Node)Activator.CreateInstance(nodeType)!;
        node.Id = Guid.NewGuid();
        node.Pins = new List<Pin>();
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { node }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        Assert.Equal(new[] { ("A", "In"), ("B", "In"), ("Result", "Out") },
            node.Pins.Select(p => (p.Name, p.Direction)).ToArray());
    }

    [Fact]
    public void NotNode_Pinless_RehydratesAResult()
    {
        var node = new NotNode { Id = Guid.NewGuid(), Pins = new List<Pin>() };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { node }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        Assert.Equal(new[] { ("A", "In"), ("Result", "Out") },
            node.Pins.Select(p => (p.Name, p.Direction)).ToArray());
    }

    // ── GetComponent enricher (CA-01, Slice 1a: unmanaged read) ─────────────────────
    // Mirrors GetShared's Target/Fields/Found enrichment exactly (EnrichGetSharedPins ->
    // EnrichGetComponentPins), except the legacy single-field "Value" pin's TypeId comes straight
    // from FieldTypeFqn (see EnrichGetComponentPins's comment on why no "global::" stamp).

    [Fact]
    public void GetComponent_MultiPinFields_ProjectsTargetPerFieldOutAndFound()
    {
        var gcn = new GetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "X", TypeId = "System.Single" },
                new ComponentFieldDecl { Name = "Y", TypeId = "System.Single" },
            },
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { gcn }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        var shape = gcn.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Equal(4, shape.Count);   // Target + X + Y + Found -- no leftover legacy "Value" pin
        Assert.Contains(("Target", "In",  false, "Fdp.Core.Entity"), shape);
        Assert.Contains(("X",      "Out", false, "System.Single"),   shape);
        Assert.Contains(("Y",      "Out", false, "System.Single"),   shape);
        Assert.Contains(("Found",  "Out", false, "System.Boolean"),  shape);
    }

    [Fact]
    public void GetComponent_LegacyNullFields_ProjectsValueOnly()
    {
        // Frozen legacy single-field shape: self-only, single "Value" out, NO Target/Found (those
        // are multi-pin-mode only, so Stage5's untouched legacy lowering never leaves a projected
        // pin uncomputed).
        var gcn = new GetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            FieldName = "X",
            FieldTypeFqn = "System.Single",
            Fields = null,
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { gcn }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        var shape = gcn.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Equal(new[] { ("Value", "Out", false, (string?)"System.Single") }, shape);
    }

    [Fact]
    public void GetComponent_LegacyNullFields_EmptyFieldTypeFqn_FallsBackToSystemObject()
    {
        var gcn = new GetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            FieldName = "X",
            FieldTypeFqn = "",
            Fields = null,
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { gcn }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        var valuePin = gcn.Pins.First(p => p.Name == "Value");
        Assert.Equal("System.Object", valuePin.TypeRef?.TypeId);
    }

    // ── SetComponent enricher (CA-03, Slice W1: unmanaged write) ────────────────────
    // Mirrors GetComponent's Fields/Found enrichment (EnrichGetComponentPins -> EnrichSetComponentPins),
    // but exec (not pure-data), self-only (no "Target" pin, ever), and "Written" is UNCONDITIONAL
    // (there is no legacy whole-struct shape to freeze -- SetComponent is a brand-new node kind).

    [Fact]
    public void SetComponent_MultiPinFields_ProjectsExecPerFieldInAndWritten()
    {
        var scn = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "X", TypeId = "System.Single" },
                new ComponentFieldDecl { Name = "Y", TypeId = "System.Single" },
            },
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { scn }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        var shape = scn.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Equal(5, shape.Count);   // In + Out (exec) + X + Y + Written -- no "Target" pin, ever
        Assert.Contains(("In",      "In",  true,  (string?)""), shape);
        Assert.Contains(("Out",     "Out", true,  (string?)""), shape);
        Assert.Contains(("X",       "In",  false, "System.Single"),  shape);
        Assert.Contains(("Y",       "In",  false, "System.Single"),  shape);
        Assert.Contains(("Written", "Out", false, "System.Boolean"), shape);
        Assert.DoesNotContain(shape, p => p.Name == "Target");
    }

    [Fact]
    public void SetComponent_NullFields_ProjectsExecAndWrittenOnly_NoFieldPins()
    {
        // A freshly-dropped SetComponent before the editor bakes any fields: still exec In/Out +
        // "Written" (the write-if-present guard result always exists), just zero field pins.
        var scn = new SetComponentNode
        {
            Id = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
            Fields = null,
            Pins = new List<Pin>(),
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event,
            Nodes = new List<Node> { scn }, Links = new(), Inputs = new(), Outputs = new() };
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "T",
            Dispatch = BlueprintDispatchKind.Instance, Graphs = new List<Graph> { graph },
            Variables = new(), CustomEvents = new() };
        Stage0_Rehydrate.Run(asset, Options());

        var shape = scn.Pins.Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId)).ToList();
        Assert.Equal(3, shape.Count);
        Assert.Contains(("In",      "In",  true,  (string?)""), shape);
        Assert.Contains(("Out",     "Out", true,  (string?)""), shape);
        Assert.Contains(("Written", "Out", false, "System.Boolean"), shape);
    }
}
