using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// P1 (GAP-1) -- validator coverage for <c>V_FlowForEachRules</c> (BP2050): a
/// <see cref="FlowForEachNode"/>'s "Body" exec-chain must be latent-free and (P1a) branch-free.
/// </summary>
public sealed class V_FlowForEachValidatorTests
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

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>
    /// Builds EventEntry -&gt; FlowForEach(Body -&gt; <paramref name="bodyRoot"/> -&gt; ... -&gt; Return)
    /// [Completed] -&gt; Return, with <paramref name="bodyRoot"/> (and any nodes it wires to, via
    /// <paramref name="extraNodes"/>/<paramref name="extraLinks"/>) spliced into the Body chain.
    /// </summary>
    private static BlueprintAsset BuildFlowForEachAsset(
        Node bodyRoot, IReadOnlyList<Node> extraNodes, IReadOnlyList<Link> extraLinks)
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("Out", "Out");
        entry.Pins.Add(entryOut);

        var feIn           = ExecPin("In", "In");
        var feBodyOut      = ExecPin("Body", "Out");
        var feCompletedOut = ExecPin("Completed", "Out");
        var feCurrentItem  = DataPin("CurrentItem", "Out", "Fdp.Core.Entity");
        var feNode = new FlowForEachNode
        {
            Id                 = Guid.NewGuid(),
            SourceComponentFqn = "Fdp.Core.CommandHierarchy.UnitRoster",
            CountAccessorFqn   = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Count",
            ItemAccessorFqn    = "Hrot.AI.Behaviors.Brains.UnitRosterOps.Subordinate",
        };
        feNode.Pins.AddRange(new[] { feIn, feBodyOut, feCompletedOut, feCurrentItem });

        var bodyRootIn = bodyRoot.Pins.First(p => p.IsExec && p.Direction == "In");

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("In", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, feNode, bodyRoot, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,  FromPinId = entryOut.Id,       ToNodeId = feNode.Id,    ToPinId = feIn.Id },
                new Link { FromNodeId = feNode.Id,  FromPinId = feBodyOut.Id,      ToNodeId = bodyRoot.Id,  ToPinId = bodyRootIn.Id },
                new Link { FromNodeId = feNode.Id,  FromPinId = feCompletedOut.Id, ToNodeId = ret.Id,       ToPinId = retIn.Id },
            },
        };
        graph.Nodes.AddRange(extraNodes);
        graph.Links.AddRange(extraLinks);

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "FlowForEachValidatorTest",
            Dispatch  = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.AiPrimitive,
            Primitive = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = { AiPrimitiveHosting.BTreeAction },
            },
            Graphs    = { graph },
        };
    }

    // ---- BP2050: Branch reachable from Body (P1a is branch-free) -----------

    [Fact]
    [CoversDiagnosticCode("BP2050")]
    public void Validate_BranchInBody_BP2050()
    {
        var branchIn    = ExecPin("In", "In");
        var branchTrue  = ExecPin("True", "Out");
        var branchFalse = ExecPin("False", "Out");
        var branch = new BranchNode { Id = Guid.NewGuid() };
        branch.Pins.AddRange(new[] { branchIn, branchTrue, branchFalse });

        var asset = BuildFlowForEachAsset(branch, Array.Empty<Node>(), Array.Empty<Link>());

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2050);
    }

    // ---- BP2050: latent node reachable from Body ---------------------------

    [Fact]
    [CoversDiagnosticCode("BP2050")]
    public void Validate_LatentDelayInBody_BP2050()
    {
        var delayIn  = ExecPin("In", "In");
        var delayOut = ExecPin("Out", "Out");
        var delay = new LatentDelayNode { Id = Guid.NewGuid() };
        delay.Pins.AddRange(new[] { delayIn, delayOut });

        var asset = BuildFlowForEachAsset(delay, Array.Empty<Node>(), Array.Empty<Link>());

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2050);
    }

    // ---- Happy path: branch-free, latent-free body -> no BP2050 -----------

    [Fact]
    public void Validate_BranchFreeLatentFreeBody_NoBP2050()
    {
        var pubIn  = ExecPin("In",  "In");
        var pubOut = ExecPin("Out", "Out");
        var pub = new PublishEventNode { Id = Guid.NewGuid(), EventId = "ClearBehaviorEvent" };
        pub.Pins.AddRange(new[] { pubIn, pubOut });

        var asset = BuildFlowForEachAsset(pub, Array.Empty<Node>(), Array.Empty<Link>());

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2050);
    }
}
