using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-75 / BP-77 — the asset's own callable graphs get palette entries, and dropping one yields a
/// call node carrying <b>only</b> a target.
///
/// <para>
/// ⭐ <b>One iteration serves both rows.</b> <c>BlueprintNodeCatalog</c> minted per-asset entries for
/// custom events and peers and <b>never iterated <c>asset.Graphs</c> at all</b> — which is why a
/// Function graph has had no palette entry since BP-24 made Function graphs creatable, not just
/// macros. Fixing macros alone would have left BP-75 half-open.
/// </para>
/// </summary>
public sealed class GraphPaletteEntryTests
{
    private static BlueprintAsset AssetWithGraphs()
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "PaletteAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
        };
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Tick",    Kind = GraphKind.Function });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Helper",  Kind = GraphKind.Function });
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "OnSpawn", Kind = GraphKind.Event });
        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro,
            ExecInputs  = { new ExecInDecl  { Id = Guid.NewGuid(), Name = "Fast" },
                            new ExecInDecl  { Id = Guid.NewGuid(), Name = "Slow" } },
            ExecOutputs = { new ExecOutDecl { Id = Guid.NewGuid(), Name = "Done" } },
        });
        return asset;
    }

    private static BlueprintNodeCatalog CatalogFor(BlueprintAsset asset)
        => new(new NodeKindRegistry()) { Asset = asset };

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ Both kinds, from one loop — this is the assertion that keeps BP-75 closed.</summary>
    [Fact]
    public void FunctionAndMacroGraphs_BothMintPaletteEntries()
    {
        var asset   = AssetWithGraphs();
        var catalog = CatalogFor(asset);

        var names = catalog.All.Select(e => e.DisplayName).ToList();
        Assert.Contains("Call Tick",    names);
        Assert.Contains("Call Helper",  names);
        Assert.Contains("Call AimFire", names);

        // ⚠ An Event body is not callable — it is entered by the engine, not by a call node.
        Assert.DoesNotContain("Call OnSpawn", names);
    }

    /// <summary>
    /// The preview must agree with what <c>NodePinSchema</c> will project, or the palette advertises
    /// a shape the dropped node does not have. Q26-A3: N exec-ins from the target's declarations.
    /// </summary>
    [Fact]
    public void MacroEntry_PreviewsItsDeclaredExecPins()
    {
        var asset   = AssetWithGraphs();
        var macro   = asset.Graphs.Single(g => g.Kind == GraphKind.Macro);
        var catalog = CatalogFor(asset);

        var entry = catalog.All.Single(e => e.Kind.Id ==
            $"{BlueprintNodeCatalog.MacroGraphKindPrefix}{macro.Id:N}");

        Assert.Equal(new[] { "Fast", "Slow" },
            entry.Inputs.Where(p => p.Kind == PinKind.Exec).Select(p => p.Label).ToArray());
        Assert.Equal(new[] { "Done" },
            entry.Outputs.Where(p => p.Kind == PinKind.Exec).Select(p => p.Label).ToArray());
    }

    /// <summary>A macro declaring nothing previews the single implicit In/Out — the N=0 rule.</summary>
    [Fact]
    public void MacroWithNoDeclarations_PreviewsTheImplicitSinglePins()
    {
        var asset = AssetWithGraphs();
        var bare  = new Graph { Id = Guid.NewGuid(), Name = "Bare", Kind = GraphKind.Macro };
        asset.Graphs.Add(bare);

        var entry = CatalogFor(asset).All.Single(e => e.Kind.Id ==
            $"{BlueprintNodeCatalog.MacroGraphKindPrefix}{bare.Id:N}");

        Assert.Equal(new[] { "In" },  entry.Inputs .Where(p => p.Kind == PinKind.Exec).Select(p => p.Label).ToArray());
        Assert.Equal(new[] { "Out" }, entry.Outputs.Where(p => p.Kind == PinKind.Exec).Select(p => p.Label).ToArray());
    }

    // ── the drop path ───────────────────────────────────────────────────────

    private static BlueprintCommandSink SinkFor(BlueprintAsset asset, Graph graph)
    {
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(registry) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        return new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });
    }

    /// <summary>
    /// ⭐⭐ <b>F4: the dropped node carries the target and nothing else.</b> Baking pin names, types or
    /// counts onto it is the <c>CallablePeers</c>/<c>ArgTypes</c> mistake that has bitten twice; the
    /// pins come from re-projecting the target every rebuild.
    /// </summary>
    [Fact]
    public void DroppingAMacroEntry_YieldsAMacroCallNodeCarryingOnlyTheTarget()
    {
        var asset = AssetWithGraphs();
        var host  = asset.Graphs.First(g => g.Kind == GraphKind.Function);
        var macro = asset.Graphs.Single(g => g.Kind == GraphKind.Macro);
        var sink  = SinkFor(asset, host);

        var id = Guid.NewGuid();
        var result = sink.Apply(new GraphCommand.AddNode(
            new NodeId(id),
            new NodeKindKey($"{BlueprintNodeCatalog.MacroGraphKindPrefix}{macro.Id:N}"),
            new System.Numerics.Vector2(10, 10),
            null));

        Assert.True(result.Success, result.Message);
        var node = Assert.Single(host.Nodes.OfType<MacroCallNode>());
        Assert.Equal(macro.Id.ToString(), node.TargetGraphId);
        // ⛔ Nothing baked: the projection owns the pins.
        Assert.Empty(node.Pins);
    }

    /// <summary>
    /// The Function half of the same fix. ⚠ Without a case here the kind fell through to the generic
    /// <c>FunctionCallNode { MethodName = kindId }</c> — a node naming a CLR method that does not
    /// exist, which is BP-84's shape.
    /// </summary>
    [Fact]
    public void DroppingAFunctionEntry_YieldsAFunctionCallNodeTargetingTheGraph()
    {
        var asset  = AssetWithGraphs();
        var host   = asset.Graphs.First(g => g.Kind == GraphKind.Function);
        var callee = asset.Graphs.Last(g => g.Kind == GraphKind.Function);
        var sink   = SinkFor(asset, host);

        sink.Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey($"{BlueprintNodeCatalog.FunctionGraphKindPrefix}{callee.Id:N}"),
            new System.Numerics.Vector2(10, 10),
            null));

        var node = Assert.Single(host.Nodes.OfType<FunctionCallNode>());
        Assert.Equal(callee.Id.ToString(), node.TargetGraphId);
        Assert.Empty(node.MethodName);      // ⚠ not the kind id — that was the fallback's defect
    }

    /// <summary>
    /// ⚠ The palette mints the guid in "N" form while every consumer compares against
    /// <c>Graph.Id.ToString()</c> ("D" form). A pass-through would produce a node whose target
    /// resolves nowhere and reports BP1660 — right-looking and non-functional.
    /// </summary>
    [Fact]
    public void DroppedTarget_IsStoredInTheFormTheProjectionsCompareAgainst()
    {
        var asset = AssetWithGraphs();
        var host  = asset.Graphs.First(g => g.Kind == GraphKind.Function);
        var macro = asset.Graphs.Single(g => g.Kind == GraphKind.Macro);

        SinkFor(asset, host).Apply(new GraphCommand.AddNode(
            new NodeId(Guid.NewGuid()),
            new NodeKindKey($"{BlueprintNodeCatalog.MacroGraphKindPrefix}{macro.Id:N}"),
            new System.Numerics.Vector2(0, 0), null));

        var node = host.Nodes.OfType<MacroCallNode>().Single();
        Assert.Contains("-", node.TargetGraphId);                       // "D" form, not "N"
        Assert.Equal(macro.Id, Guid.Parse(node.TargetGraphId));
    }
}
