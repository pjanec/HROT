using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPF-039: GetOrdered must append residual fields (those not in the
/// explicit order list) in a deterministic, stable order across runs.
/// </summary>
public sealed class BPF039_GetOrderedDeterminismTests
{
    // Three GUIDs chosen so that alphabetical/byte sort order is: p1 < p2 < p3.
    private static readonly Guid P1Id = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid P2Id = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid P3Id = new("aaaaaaaa-0000-0000-0000-000000000003");

    /// <summary>
    /// Build a TypedAsset from a BlueprintAsset that has three parameters with
    /// known Guids, and a ParameterOrder that only names the first one.
    /// The remaining two are "residual" and should be appended in Guid-sorted order.
    /// </summary>
    private static IrAsset RunScheduleWithPartialOrder(
        IEnumerable<ParameterDecl> parameters,
        List<Guid> parameterOrder)
    {
        var asset = new BlueprintAsset
        {
            AssetId        = new Guid("bbbbbbbb-0000-0000-0000-000000000001"),
            Name           = "ResidualTest",
            Dispatch       = AssetDispatch.AiPrimitive,
            Primitive      = new AiPrimitiveDecl
            {
                Intent   = AiPrimitiveIntent.Action,
                Hostings = new List<AiPrimitiveHosting> { AiPrimitiveHosting.BTreeAction },
            },
            Parameters     = parameters.ToList(),
            ParameterOrder = parameterOrder,
            Graphs         = new List<Graph>
            {
                // Minimal graph to pass Stage5 without errors.
                new Graph
                {
                    Id    = new Guid("cccccccc-0000-0000-0000-000000000001"),
                    Name  = "Main",
                    Kind  = GraphKind.Function,
                    Nodes = new List<Node>
                    {
                        new EventEntryNode { Id = new Guid("dddddddd-0000-0000-0000-000000000001") },
                        new ReturnNode    { Id = new Guid("dddddddd-0000-0000-0000-000000000002") },
                    },
                    Links = new List<Link>
                    {
                        new Link
                        {
                            FromNodeId = new Guid("dddddddd-0000-0000-0000-000000000001"),
                            FromPinId  = new Guid("dddddddd-0000-0000-0000-000000000011"),
                            ToNodeId   = new Guid("dddddddd-0000-0000-0000-000000000002"),
                            ToPinId    = new Guid("dddddddd-0000-0000-0000-000000000021"),
                        },
                    },
                    Inputs  = new List<ParameterDecl>(),
                    Outputs = new List<ParameterDecl>(),
                },
            },
        };

        // Add exec pins to nodes so links are valid.
        var entry = (EventEntryNode)asset.Graphs[0].Nodes[0];
        entry.Pins.Add(new Pin
        {
            Id        = new Guid("dddddddd-0000-0000-0000-000000000011"),
            Name      = "ExecOut",
            Direction = "Out",
            IsExec    = true,
            TypeRef   = new(),
        });
        var ret = (ReturnNode)asset.Graphs[0].Nodes[1];
        ret.Pins.Add(new Pin
        {
            Id        = new Guid("dddddddd-0000-0000-0000-000000000021"),
            Name      = "ExecIn",
            Direction = "In",
            IsExec    = true,
            TypeRef   = new(),
        });

        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>()));
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        return Stage5_Schedule.Run(typed, ctx);
    }

    private static ParameterDecl MakeParam(Guid id, string name) =>
        new ParameterDecl
        {
            Id   = id,
            Name = name,
            Type = new BlueprintTypeRef { TypeId = "System.Single" },
        };

    /// <summary>
    /// BPF-039: When ParameterOrder lists only P3, the residual fields (P1, P2)
    /// must appear in ascending Guid order after P3.
    /// </summary>
    [Fact]
    public void GetOrdered_ResidualsAreSortedByGuid()
    {
        // Parameters inserted in reverse Guid order to expose dictionary non-determinism.
        var parameters = new[]
        {
            MakeParam(P3Id, "C"),
            MakeParam(P2Id, "B"),
            MakeParam(P1Id, "A"),
        };
        var order = new List<Guid> { P3Id }; // Only P3 is explicitly ordered.

        var ir = RunScheduleWithPartialOrder(parameters, order);

        Assert.Equal(3, ir.Parameters.Count);
        Assert.Equal(P3Id, ir.Parameters[0].Id); // Ordered first (from ParameterOrder).
        Assert.Equal(P1Id, ir.Parameters[1].Id); // Smallest residual Guid.
        Assert.Equal(P2Id, ir.Parameters[2].Id); // Larger residual Guid.
    }

    /// <summary>
    /// BPF-039: Two calls with different parameter insertion orders must produce
    /// identical IR parameter lists.
    /// </summary>
    [Fact]
    public void GetOrdered_SameInputDifferentInsertionOrder_ProducesIdenticalOutput()
    {
        var order = new List<Guid> { P2Id }; // Only P2 is explicitly ordered.

        // Run 1: P1, P2, P3 insertion order.
        var ir1 = RunScheduleWithPartialOrder(
            new[] { MakeParam(P1Id, "A"), MakeParam(P2Id, "B"), MakeParam(P3Id, "C") },
            order);

        // Run 2: P3, P1, P2 insertion order (reversed).
        var ir2 = RunScheduleWithPartialOrder(
            new[] { MakeParam(P3Id, "C"), MakeParam(P1Id, "A"), MakeParam(P2Id, "B") },
            order);

        Assert.Equal(ir1.Parameters.Count, ir2.Parameters.Count);
        for (int i = 0; i < ir1.Parameters.Count; i++)
            Assert.Equal(ir1.Parameters[i].Id, ir2.Parameters[i].Id);
    }
}
