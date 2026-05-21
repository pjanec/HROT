using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class V_PeerReferencesTests
{
    private static readonly Guid PeerId = new Guid("aa000001-0000-0000-0000-000000000000");

    // Builds an Instance asset with one CallPeerBlueprintNode referencing PeerId
    // and calling FunctionRef "Compute".
    private static BlueprintAsset BuildAssetWithPeerCall(string funcRef = "Compute")
    {
        var assetId  = new Guid("bb000001-0000-0000-0000-000000000000");
        var graphId  = new Guid("cc000001-0000-0000-0000-000000000000");
        var entryId  = new Guid("dd000001-0000-0000-0000-000000000000");
        var callId   = new Guid("ee000001-0000-0000-0000-000000000000");
        var returnId = new Guid("ff000001-0000-0000-0000-000000000000");
        var e1 = Guid.NewGuid(); var e2 = Guid.NewGuid();
        var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid();
        var r1 = Guid.NewGuid();

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "Caller",
            Dispatch = AssetDispatchKind.Instance,
            Parameters   = new(),
            WorkingState = new(),
            Variables    = new(),
            EventDispatchers = new(),
            CustomEvents = new(),
            CallablePeers = new List<Guid> { PeerId },
            Graphs = new List<Graph>
            {
                new Graph
                {
                    Id   = graphId,
                    Name = "Main",
                    Kind = GraphKind.Function,
                    Inputs  = new(),
                    Outputs = new(),
                    Nodes = new List<Node>
                    {
                        new EventEntryNode { Id = entryId,
                            Pins = new() { new Pin { Id = e1, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                        new CallPeerBlueprintNode { Id = callId,
                            PeerBlueprintId = PeerId.ToString(),
                            FunctionRef = funcRef,
                            Pins = new() {
                                new Pin { Id = c1, Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                                new Pin { Id = c2, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                            } },
                        new ReturnNode { Id = returnId, Status = NodeStatus.Success,
                            Pins = new() { new Pin { Id = r1, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                    },
                    Links = new List<Link>
                    {
                        new Link { FromNodeId = entryId, FromPinId = e1, ToNodeId = callId,   ToPinId = c1 },
                        new Link { FromNodeId = callId,  FromPinId = c2, ToNodeId = returnId, ToPinId = r1 },
                    },
                },
            },
            Header = new Header { SubsystemType = "Hrot.Blueprints", SchemaVersion = "1.0" },
        };
    }

    private static BlueprintSignature MakeSiblingSignature(params string[] functionNames) =>
        new BlueprintSignature(
            Path:                  "Peer.bp.json",
            AssetId:               PeerId,
            Name:                  "PeerLib",
            SanitizedName:         "PeerLib",
            BlueprintId:           42,
            Dispatch:              AssetDispatchKind.Library,
            ExportedFunctionNames: functionNames,
            Hostings:              Array.Empty<AiPrimitiveHosting>(),
            DeclaredCallablePeers: Array.Empty<Guid>());

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset,
        IReadOnlyList<BlueprintSignature>? siblings = null)
    {
        var sink = new DiagnosticSink();
        var opts = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));
        return sink.All;
    }

    [Fact]
    [CoversDiagnosticCode("BP1300")]
    public void Instance_CallPeerNotInCallablePeers_EmitsBP1300()
    {
        var asset = BuildAssetWithPeerCall();
        asset.CallablePeers.Clear(); // Remove PeerId from declared peers.

        var diags = Validate(asset, new[] { MakeSiblingSignature("Compute") });

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1300);
    }

    [Fact]
    [CoversDiagnosticCode("BP1301")]
    public void Instance_CallPeerNotInSiblings_EmitsBP1301()
    {
        var asset = BuildAssetWithPeerCall();

        // No sibling signatures provided -> peer not found.
        var diags = Validate(asset, Array.Empty<BlueprintSignature>());

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1301);
    }

    [Fact]
    [CoversDiagnosticCode("BP1302")]
    public void Instance_CallPeerFunctionNotExported_EmitsBP1302()
    {
        var asset = BuildAssetWithPeerCall("Compute");

        // Sibling exists but has no function named "Compute".
        var sibling = MakeSiblingSignature("OtherFunction");
        var diags = Validate(asset, new[] { sibling });

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1302);
    }

    [Fact]
    public void Instance_CallPeerHappyPath_NoDiagnostics()
    {
        var asset = BuildAssetWithPeerCall("Compute");
        var sibling = MakeSiblingSignature("Compute");

        var diags = Validate(asset, new[] { sibling });

        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP1300
            || d.Code == DiagnosticCodes.BP1301
            || d.Code == DiagnosticCodes.BP1302);
    }
}
