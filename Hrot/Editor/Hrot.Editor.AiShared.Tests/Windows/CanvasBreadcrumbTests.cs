using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// BP-85 — the canvas must say which graph is being edited.
///
/// <para>
/// The tab shows only the asset name, so creating a function graph (which correctly switches the
/// canvas) read as "my graph has been emptied" — a false data-loss scare — and nothing on screen
/// answered "is this an Instance blueprint?".
/// </para>
///
/// <para>
/// These drive the real <see cref="BlueprintGraphModel"/> so the graph-kind label is the one the
/// canvas will actually show, not a restatement of the format string.
/// </para>
/// </summary>
public sealed class CanvasBreadcrumbTests
{
    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name) => Name = name;
        public Guid AssetId => Guid.Empty;
        public string Name { get; }
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeAssetWithDispatch : IEditableAsset, IAssetSubtitleProvider
    {
        public FakeAssetWithDispatch(string name, string? dispatch)
        {
            Name = name;
            Subtitle = dispatch;
        }
        public Guid AssetId => Guid.Empty;
        public string Name { get; }
        public string? Subtitle { get; }
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public event Action? Changed { add { } remove { } }
    }

    private static BlueprintGraphModel ModelFor(GraphKind kind, string graphName)
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "SquadState1" };
        var graph = new Graph { Id = Guid.NewGuid(), Name = graphName, Kind = kind };
        asset.Graphs.Add(graph);
        return new BlueprintGraphModel(asset, graph);
    }

    // ── the graph-kind label the breadcrumb depends on ───────────────────────

    [Fact]
    public void GraphModel_ReportsFunctionKind_ForAFunctionGraph()
    {
        var model = ModelFor(GraphKind.Function, "GetThreatLevel");

        Assert.Equal("FunctionGraph", model.Kind.Id);
        Assert.Equal("Function",      model.Kind.DisplayName);
    }

    [Fact]
    public void GraphModel_ReportsEventKind_ForAnEventGraph()
    {
        var model = ModelFor(GraphKind.Event, "EventGraph");

        Assert.Equal("EventGraph",  model.Kind.Id);
        Assert.Equal("Event Graph", model.Kind.DisplayName);
    }

    [Fact]
    public void GraphModel_KindFollowsRetarget()
    {
        // The canvas switches graphs in place (BP-24 Retarget); a descriptor captured at
        // construction would keep describing the graph the model was built with.
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "SquadState1" };
        var evt   = new Graph { Id = Guid.NewGuid(), Name = "EventGraph",     Kind = GraphKind.Event };
        var fn    = new Graph { Id = Guid.NewGuid(), Name = "GetThreatLevel", Kind = GraphKind.Function };
        asset.Graphs.Add(evt);
        asset.Graphs.Add(fn);

        var model = new BlueprintGraphModel(asset, evt);
        Assert.Equal("Event Graph", model.Kind.DisplayName);

        model.Retarget(fn);

        Assert.Equal("Function",       model.Kind.DisplayName);
        Assert.Equal("GetThreatLevel", model.DisplayName);
    }

    // ── the breadcrumb string ────────────────────────────────────────────────

    [Fact]
    public void Breadcrumb_NamesTheAssetGraphAndKind()
    {
        var doc   = new AiDocument(new FakeAssetWithDispatch("SquadState1", "Instance"), AssetKind.Blueprint);
        var model = ModelFor(GraphKind.Function, "GetThreatLevel");

        var text = AiGraphCanvasWindow.BuildBreadcrumb(doc, model);

        Assert.Contains("SquadState1",    text);
        Assert.Contains("Instance",       text);   // answers "is this an Instance blueprint?"
        Assert.Contains("GetThreatLevel", text);   // answers "which graph am I on?"
        Assert.Contains("Function",       text);
    }

    [Fact]
    public void Breadcrumb_UsesGlyphsTheEditorFontCanActuallyRender()
    {
        // Verified in the running editor: "▸" (U+25B8) has no glyph in the ImGui font atlas and
        // draws as "?", so the breadcrumb read "Count4 · Instance ? Tick (Function)". The middle
        // dot does render. Locks the separator to something the atlas covers.
        var doc   = new AiDocument(new FakeAssetWithDispatch("SquadState1", "Instance"), AssetKind.Blueprint);
        var model = ModelFor(GraphKind.Function, "GetThreatLevel");

        var text = AiGraphCanvasWindow.BuildBreadcrumb(doc, model);

        Assert.Contains(" > ", text);
        Assert.All(text, ch => Assert.True(ch < 0x2000, $"U+{(int)ch:X4} is outside the atlas' safe range"));
    }

    [Fact]
    public void Breadcrumb_OmitsDispatch_WhenAssetDoesNotProvideOne()
    {
        // BTree / HSM assets do not implement IAssetSubtitleProvider — no stray separator.
        var doc   = new AiDocument(new FakeAsset("SomeTree"), AssetKind.BTree);
        var model = ModelFor(GraphKind.Event, "EventGraph");

        var text = AiGraphCanvasWindow.BuildBreadcrumb(doc, model);

        Assert.StartsWith("SomeTree", text);
        Assert.DoesNotContain("·", text);
        Assert.Contains("EventGraph", text);
    }

    [Fact]
    public void Breadcrumb_DoesNotRepeatKind_WhenItEqualsTheGraphName()
    {
        // A graph literally called "EventGraph" must not render "EventGraph (EventGraph)".
        var doc   = new AiDocument(new FakeAsset("A"), AssetKind.Blueprint);
        var model = ModelFor(GraphKind.Event, "Event Graph");

        var text = AiGraphCanvasWindow.BuildBreadcrumb(doc, model);

        Assert.DoesNotContain("(", text);
    }

    [Fact]
    public void Breadcrumb_IsEmpty_WhenNoDocumentIsActive()
    {
        Assert.Equal(string.Empty, AiGraphCanvasWindow.BuildBreadcrumb(null, null));
    }

    [Fact]
    public void Breadcrumb_FallsBackToAssetName_WhenModelIsMissing()
    {
        var doc = new AiDocument(new FakeAsset("SquadState1"), AssetKind.Blueprint);

        Assert.Equal("SquadState1", AiGraphCanvasWindow.BuildBreadcrumb(doc, null));
    }
}
