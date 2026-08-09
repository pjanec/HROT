using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>BP-127 — renaming a graph.</b> There was no way to do it at all: <c>RenameItem</c> resolved
/// <c>var:</c> and <c>evt:</c> item ids and simply returned <c>false</c> for the <c>graph:</c> ids the
/// My Blueprint panel gives its Graphs and Functions rows.
///
/// <para>
/// ⭐ <b>Why it lives in My Blueprint's context menu.</b> The authoring-UX decisions round settled this
/// against Unreal's actual model: rename is a context-menu action on the item, not a field on an
/// empty-canvas Details panel. That is also what unblocked it — the item was blocked on a surface that,
/// on inspection, Unreal does not have.
/// </para>
///
/// <para>
/// ⚠ <b>The interesting half is not the rename, it is what a rename BREAKS.</b> A graph name is not
/// decoration: the compiler emits a method per Function graph, an <c>Event_{Name}</c> per Event graph,
/// and <c>CallPeerBlueprint</c> addresses a function <b>by name</b>. Each of those is a way for a
/// rename to compile into silence.
/// </para>
/// </summary>
public sealed class BP127_GraphRenameTests
{
    [Fact]
    public void AFunctionGraph_CanBeRenamed()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        Assert.True(BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", "CombineScores"));
        Assert.Equal("CombineScores", graph.Name);
    }

    /// <summary>The context menu needs the current name to pre-fill its prompt.</summary>
    [Fact]
    public void ItemDisplayName_ResolvesAGraphItemId()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        Assert.Equal("Combine", BlueprintDocumentFactory.ItemDisplayName(asset, $"graph:{graph.Id}"));
    }

    /// <summary>
    /// ⭐ <b>A peer call names its function by string</b>, and <c>Stage5</c> resolves it that way — so a
    /// rename that left the reference behind would turn into <c>BP1302 "no function graph named …"</c>,
    /// a build break with no visible cause. Same class of miss BP-24 fixed for custom events.
    /// </summary>
    [Fact]
    public void RenamingAFunction_RewritesNameKeyedPeerCallReferences()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        var caller = new CallPeerBlueprintNode { Id = Guid.NewGuid(), FunctionRef = "Combine" };
        asset.Graphs[0].Nodes.Add(caller);

        Assert.True(BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", "CombineScores"));
        Assert.Equal("CombineScores", caller.FunctionRef);
    }

    /// <summary>⚠ A reference to a DIFFERENT function must not be rewritten along with it.</summary>
    [Fact]
    public void RenamingAFunction_LeavesUnrelatedPeerCallReferencesAlone()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        var other = new CallPeerBlueprintNode { Id = Guid.NewGuid(), FunctionRef = "SomethingElse" };
        asset.Graphs[0].Nodes.Add(other);

        BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", "CombineScores");

        Assert.Equal("SomethingElse", other.FunctionRef);
    }

    /// <summary>
    /// ⚠ <b>An Event graph paired with a declaration is refused.</b> The pairing is by name — renaming
    /// the declaration renames the graph, and renaming the graph alone would desync them into a
    /// <c>BP1407</c>. Refusing points the designer at the rename that does work.
    /// </summary>
    [Fact]
    public void AnEventGraphPairedWithADeclaration_IsNotRenamable()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "Peer",
            Dispatch = BlueprintDispatchKind.Instance,
        };
        var eventGraph = new Graph { Id = Guid.NewGuid(), Name = "OnSpotted", Kind = GraphKind.Event };
        asset.Graphs.Add(eventGraph);
        asset.CustomEvents.Add(new CustomEventDecl { Id = Guid.NewGuid(), Name = "OnSpotted" });

        Assert.False(BlueprintDocumentFactory.RenameItem(asset, $"graph:{eventGraph.Id}", "OnSeen"));
        Assert.Equal("OnSpotted", eventGraph.Name);
    }

    /// <summary>
    /// The compiler emits a method named after the graph, so the name must be a legal identifier —
    /// the same rule the custom-event create path applies. ⚠ Rejected by <b>changing nothing</b>.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2Fast")]
    [InlineData("has space")]
    [InlineData("has-dash")]
    public void AnInvalidName_IsRefused_AndChangesNothing(string proposed)
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        Assert.False(BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", proposed));
        Assert.Equal("Combine", graph.Name);
    }

    /// <summary>
    /// ⚠ Duplicate names are refused <b>case-insensitively</b>: the emitted members would collide as
    /// C# identifiers regardless of case, so accepting one would produce a build error rather than an
    /// editor message.
    /// </summary>
    [Fact]
    public void ANameAlreadyTakenByAnotherGraph_IsRefused()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Split", Kind = GraphKind.Function });

        Assert.False(BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", "split"));
        Assert.Equal("Combine", graph.Name);
    }

    /// <summary>Renaming to the name it already has is a no-op, so the caller records no undo entry.</summary>
    [Fact]
    public void RenamingToTheSameName_ReturnsFalse()
    {
        var (asset, graph) = AssetWithFunctionGraph("Combine");

        Assert.False(BlueprintDocumentFactory.RenameItem(asset, $"graph:{graph.Id}", "Combine"));
    }

    /// <summary>An id that resolves to nothing must not be mistaken for a graph.</summary>
    [Fact]
    public void AnUnknownGraphId_IsRefused()
    {
        var (asset, _) = AssetWithFunctionGraph("Combine");

        Assert.False(BlueprintDocumentFactory.RenameItem(asset, $"graph:{Guid.NewGuid()}", "Whatever"));
    }

    // ── fixture ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>An asset with a Tick graph plus one named Function graph — the rename target.</summary>
    private static (BlueprintAsset Asset, Graph Function) AssetWithFunctionGraph(string functionName)
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "Lib",
            Dispatch = BlueprintDispatchKind.Library,
        };
        asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function });

        var function = new Graph { Id = Guid.NewGuid(), Name = functionName, Kind = GraphKind.Function };
        asset.Graphs.Add(function);

        return (asset, function);
    }
}
