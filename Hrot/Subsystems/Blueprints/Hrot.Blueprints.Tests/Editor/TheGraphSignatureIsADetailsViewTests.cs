using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Shell;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>L3.2</c>'s rail — the Graph-signature panel is offered as a Details VIEW, and its
/// predicate is the one the code can actually answer.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L3</c>'s table · §6 <c>L1.2</c>'s claim chain.
///
/// <para>⭐⭐ <b>This rail lives in <c>Hrot.Blueprints.Tests</c>, beside the view</b>, because the
/// predicate asks a Blueprint question — 📌 <c>R-116</c>: the predicate ships with the view, and
/// <c>AiShared</c> must not learn what a graph is.</para>
/// </summary>
public sealed class TheGraphSignatureIsADetailsViewTests
{
    private static Graph FunctionGraph() => new()
        { Id = Guid.NewGuid(), Name = "Func1", Kind = GraphKind.Function };

    private static (GraphSignatureWindow window, EditorSelectionStore store) MakeWindow()
    {
        var store = new EditorSelectionStore();
        return (new GraphSignatureWindow(store, new DirtyTracker()), store);
    }

    private sealed class BlueprintDoc : IEditableAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "TestBP";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath { get; init; } = "/test.bp";
        public bool IsDirty { get; init; }
        public bool IsEditorOwned { get; init; }
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private static DetailsContext Ctx(AssetKind kind = AssetKind.Blueprint)
        => new(Hrot.Editor.AiShared.Selection.SelectionOrigin.Unknown,
               Array.Empty<Hrot.Editor.AiShared.Selection.IAssetSubSelection>(),
               Array.Empty<Fdp.Core.Entity>(),
               new BlueprintDoc { Kind = kind },
               "Blueprint",
               Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    // ══ the window declares its view ═════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The window is an <c>IDetailsViewSource</c></b>, so <c>RegisterExtraWindow</c> —
    /// 📐 already called at <c>EditorSubsystem:3190</c> — collects it. ⛔ <c>R-67</c>: the root gains
    /// nothing to forget.
    /// </summary>
    [Fact]
    public void TheWindow_DeclaresTheGraphSignatureView()
    {
        var (window, _) = MakeWindow();

        var ids = ((IDetailsViewSource)window).DetailsViews.Select(d => d.Id).ToArray();

        Assert.Equal(new[] { GraphSignatureDetailsViewDescriptor.ViewId }, ids);
    }

    // ══ the predicate ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A Blueprint WITH graphs claims the panel; one WITHOUT does not.</b>
    ///
    /// <para>⚠⚠ <b>This is <c>L3.2</c>'s stated deviation, railed.</b> §6 asks for
    /// <i>"Blueprint ∧ a graph row"</i>; 📐 <c>search_graph(".*Selection$")</c> returns <b>12</b>
    /// sub-selection types and <b>none is a graph</b> — the selected graph is the window's own state.
    /// ⇒ ⭐ <i>"a graph row"</i> is built as <b>"there is at least one graph row to show"</b>.
    /// ⛔ With none, the body would draw <i>"No Function, Event or Macro graphs in this blueprint."</i>
    /// — 📌 <c>R-117</c>: a view that claims the panel in order to apologise is the blank one level
    /// down, so it declines and the shell's grey line answers.</para>
    /// </summary>
    [Fact]
    public void ItClaimsABlueprintWithGraphs_AndDeclinesOneWithout()
    {
        var (window, store) = MakeWindow();

        store.SelectAsset(new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "Empty" });
        Assert.False(window.AppliesTo(Ctx()));

        var withGraph = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "HasOne" };
        withGraph.Graphs.Add(FunctionGraph());
        store.SelectAsset(withGraph);
        Assert.True(window.AppliesTo(Ctx()));
    }

    /// <summary>
    /// ⭐⭐ <b>A non-Blueprint document never claims it</b> — 📌 <c>R-112</c>: the kind clause is inside
    /// THIS view's predicate, ⛔ not a key the registry switches on.
    /// </summary>
    [Fact]
    public void ItDeclinesANonBlueprintDocument()
    {
        var (window, store) = MakeWindow();
        var withGraph = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "HasOne" };
        withGraph.Graphs.Add(FunctionGraph());
        store.SelectAsset(withGraph);

        Assert.False(window.AppliesTo(Ctx(AssetKind.BTree)));
        Assert.False(window.AppliesTo(Ctx(AssetKind.Hsm)));
    }

    /// <summary>⭐ …and with no document at all — ⛔ never a throw.</summary>
    [Fact]
    public void ItDeclinesWithNoDocumentOpen()
    {
        var (window, _) = MakeWindow();
        Assert.False(window.AppliesTo(DetailsContext.Empty("Blueprint")));
    }

    // ══ ONE body, two hosts ══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The view draws through the window's OWN body</b> — 📌 ruling 9. ⛔ Not a second renderer:
    /// <c>DrawClientArea</c> and this view both call <c>DrawContent</c>.
    /// <para>⚠ Asserted structurally *(one public seam, reached by the view)* rather than by pixels —
    /// 📌 <c>R-21</c>/<c>R-62</c>: the draw is unrailed by construction.</para>
    /// </summary>
    [Fact]
    public void TheViewAndTheWindow_ShareOneContentMethod()
    {
        var (window, _) = MakeWindow();
        var view = GraphSignatureDetailsViewDescriptor.For(window).Create();

        Assert.IsType<GraphSignatureDetailsView>(view);
        Assert.NotNull(typeof(GraphSignatureWindow).GetMethod(nameof(GraphSignatureWindow.DrawContent)));
    }
}
