using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-57 §3 — a local must be <b>aimable</b> and <b>readable</b> from the editor.
///
/// <para>
/// ⭐ Two independent gaps with one cause: nothing outside the compiler knew
/// <see cref="Graph.LocalVariables"/> existed. The picker offered only asset variables, so a local
/// declared in JSON could never be targeted; and the node title resolver searched only
/// <c>Variables</c> then <c>WorkingState</c>, so a local-targeting node displayed a <b>raw GUID</b>.
/// </para>
/// </summary>
public sealed class LocalVariablePickerAndTitleTests
{
    private static VariableDecl Decl(string name, string typeId = "System.Int32") => new()
    {
        Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = typeId },
    };

    private static Graph NewGraph(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Function,
    };

    /// <summary>The node's canvas body string — the same seam <c>BlueprintNodeTitleTests</c> uses.</summary>
    private static string Title(Node node, BlueprintAsset asset)
        => new BlueprintNodeModel(node, Array.Empty<IPinModel>(), asset).Title;

    private static BlueprintAsset Asset(params Graph[] graphs) => new()
    {
        AssetId = Guid.NewGuid(), Name = "PickerHost",
        Dispatch = BlueprintDispatchKind.Instance,
        Graphs = graphs.ToList(), Header = new Header(),
    };

    // ── the picker ──────────────────────────────────────────────────────────

    /// <summary>⭐ The blocker: even a JSON-declared local could not be aimed at from the editor.</summary>
    [Fact]
    public void ThePickerOffersTheCurrentGraphsLocalsAlongsideAssetVariables()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var asset = Asset(g);
        asset.Variables.Add(Decl("Ammo"));

        var src = new BlueprintPickerSources.BlueprintVariablePickerSource(asset, () => g);

        Assert.Equal(new[] { "Ammo", "Scratch" }, src.Query("", null).Select(v => v.Name).ToArray());
    }

    /// <summary>⭐ It follows the canvas — a graph switch changes what is offered (BP-72).</summary>
    [Fact]
    public void ThePickerFollowsTheCurrentGraph()
    {
        var first  = NewGraph("Tick");
        var second = NewGraph("Helper");
        first.LocalVariables.Add(Decl("A"));
        second.LocalVariables.Add(Decl("B"));

        Graph current = first;
        var src = new BlueprintPickerSources.BlueprintVariablePickerSource(Asset(first, second), () => current);

        Assert.Contains("A", src.Query("", null).Select(v => v.Name));
        current = second;
        Assert.Contains("B", src.Query("", null).Select(v => v.Name));
        Assert.DoesNotContain("A", src.Query("", null).Select(v => v.Name));
    }

    /// <summary>The search filter reaches locals too, not only asset variables.</summary>
    [Fact]
    public void TheSearchFilterMatchesLocals()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var asset = Asset(g);
        asset.Variables.Add(Decl("Ammo"));

        var src = new BlueprintPickerSources.BlueprintVariablePickerSource(asset, () => g);
        Assert.Equal(new[] { "Scratch" }, src.Query("scra", null).Select(v => v.Name).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>Shadowing must be visible.</b> <c>Q27-C1</c> permits a local and an asset variable to
    /// share a name, and the compiler picks the local silently and correctly — so two identically
    /// labelled rows would leave the designer choosing blind.
    /// </summary>
    [Fact]
    public void AShadowingLocalIsDistinguishableFromTheAssetVariable()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);
        var asset = Asset(g);
        var assetVar = Decl("Scratch");
        asset.Variables.Add(assetVar);

        var src = new BlueprintPickerSources.BlueprintVariablePickerSource(asset, () => g);

        Assert.DoesNotContain("(local)", src.RowLabel(assetVar));
        Assert.Contains("(local)", src.RowLabel(local));
    }

    /// <summary>
    /// ⛔⛔ <b>The scope guard.</b> <c>Parameters</c> are <c>BP-226</c>'s unfixed index space —
    /// widening the picker to them is exactly what would make that row live. This asserts the batch
    /// widened to locals and <b>nothing else</b>.
    ///
    /// <para>⭐⭐⭐ <b>Batch 86 — RESTATED, and this one is a SEMANTIC change, not a spelling one.</b>
    /// "Phase" was excluded because it was a <c>WorkingState</c>; <c>R-01</c> makes it a
    /// <c>Variable</c>, and asset variables have always been offered. ⇒ ⭐ it is now EXPECTED in the
    /// list, and excluding it would be the picker hiding half the state tier from the designer.
    /// ⛔ The guard itself is untouched and is the whole reason the test survives: <c>Speed</c>, a
    /// <c>Parameter</c>, must still not appear.</para>
    /// </summary>
    [Fact]
    public void ThePickerWasNotWidenedToParameters()
    {
        var g = NewGraph("Tick");
        g.LocalVariables.Add(Decl("Scratch"));
        var asset = Asset(g);
        asset.Variables.Add(Decl("Ammo"));
        asset.WorkingState.Add(Decl("Phase"));
        asset.Parameters.Add(new ParameterDecl
        {
            Id = Guid.NewGuid(), Name = "Speed", Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });

        var offered = new BlueprintPickerSources.BlueprintVariablePickerSource(asset, () => g)
            .Query("", null).Select(v => v.Name).ToArray();

        Assert.Equal(new[] { "Ammo", "Phase", "Scratch" }, offered);
        Assert.DoesNotContain("Speed", offered);
    }

    /// <summary>Without a graph provider the picker behaves exactly as before — asset variables only.</summary>
    [Fact]
    public void WithNoGraphProviderThePickerIsUnchanged()
    {
        var asset = Asset(NewGraph("Tick"));
        asset.Variables.Add(Decl("Ammo"));

        var src = new BlueprintPickerSources.BlueprintVariablePickerSource(asset);
        Assert.Equal(new[] { "Ammo" }, src.Query("", null).Select(v => v.Name).ToArray());
    }

    // ── the node title ──────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 <b>The raw-GUID bug.</b> A <c>Get</c> aimed at a local rendered its id, because the title
    /// resolver knew only the two asset-level lists.
    /// </summary>
    [Fact]
    public void ALocalTargetingNodeShowsItsNameNotAGuid()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);

        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = local.Id.ToString() };
        g.Nodes.Add(get);

        var title = Title(get, Asset(g));

        Assert.Contains("Scratch", title);
        Assert.DoesNotContain(local.Id.ToString(), title);
    }

    /// <summary>⭐ The <c>var:</c> prefix form resolves too — that is what the picker writes.</summary>
    [Fact]
    public void ThePrefixedFormResolvesAsWell()
    {
        var g = NewGraph("Tick");
        var local = Decl("Scratch");
        g.LocalVariables.Add(local);

        var set = new SetVariableNode { Id = Guid.NewGuid(), VariableId = "var:" + local.Id };
        g.Nodes.Add(set);

        Assert.Contains("Scratch", Title(set, Asset(g)));
    }

    /// <summary>
    /// ⭐ <b>The fallback shape is preserved deliberately.</b> An unresolvable id is returned as-is so
    /// a dangling reference stays visible on the node rather than reading as a valid one.
    /// </summary>
    [Fact]
    public void AnUnresolvableIdStillShowsAsItself()
    {
        var g = NewGraph("Tick");
        var stale = Guid.NewGuid();
        var get = new GetVariableNode { Id = Guid.NewGuid(), VariableId = stale.ToString() };
        g.Nodes.Add(get);

        Assert.Contains(stale.ToString(), Title(get, Asset(g)));
    }
}
