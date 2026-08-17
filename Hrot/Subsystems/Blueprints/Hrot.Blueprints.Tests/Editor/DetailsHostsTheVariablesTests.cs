using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>U-6</c> — the Details panel hosts the SHARED variables list, and an outline click routes
/// to it.</b>
///
/// <para>📌 <b><c>Q32</c> §4 row 57, verbatim:</b> <i>"`U-6` — Details hosts the <b>shared</b> control
/// + ruling 2's selection routing | ⛔ <b>the shared control, never a blueprint copy</b> (ruling
/// 9)"</i></para>
///
/// <para>📌 <b><c>Q32</c> ruling 2</b> — <i>"Selection routes: click a <b>global</b> in My Blueprint ⇒
/// the list of <b>globals / working state</b>. Click a <b>local</b> ⇒ the locals of the <b>currently
/// selected graph</b>."</i></para>
///
/// <para>🔴 <b>This closes <c>BP-315</c>'s measurement.</b> Batch 81 measured that
/// <c>MyBlueprintPanel.SelectionChanged</c> had <b>zero subscribers anywhere in the repo</b>, and that
/// <c>navigateToItem</c> explicitly ignored variables — <i>"Variables stay non-navigating — nowhere
/// sensible to go."</i> ⇒ ⭐ the Details panel was correct about its own contract and the capability
/// was simply absent. There is now somewhere sensible to go.</para>
/// </summary>
public sealed class DetailsHostsTheVariablesTests
{
    // ══ the shared control, not a copy (ruling 9) ════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The acceptance criterion.</b> The list the Details panel hosts must be Track C's
    /// <see cref="VariableTableControl"/> — ⛔ a blueprint-local table would be the exact thing
    /// <c>U-6</c> exists to prevent.
    /// </summary>
    [Fact]
    public void TheDetailsPanel_HostsTheSharedControl_NotACopy()
    {
        var details = MakeDetails();

        Assert.NotNull(details.Variables);
        Assert.IsType<VariableTableControl>(details.Variables.Control);
        Assert.IsType<VariableTableModel>(details.Variables.Model);
        // ⭐ …and it comes from the shared assembly, which is what "shared" means here.
        Assert.Equal("Hrot.Editor.AiShared",
                     typeof(VariableTableControl).Assembly.GetName().Name);
    }

    /// <summary>⭐ Nothing selected ⇒ nothing hosted. ⛔ An empty table is not an empty state.</summary>
    [Fact]
    public void WithNoSelection_TheDetailsPanelHostsNoList()
    {
        var details = MakeDetails();

        Assert.False(details.Variables.HasContent);
        Assert.Null(details.Variables.Heading);
    }

    // ══ ruling 2 — selection routes ══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A global click yields that global list.</b> ⚠ One stated deviation from ruling 2's
    /// wording: it says <i>"globals / working state"</i> as ONE merged list, and 📌 <c>Q39</c> rules
    /// that merge is <b>stage <c>D</c></b> with its own batch and a JSON migration. ⛔ Merging in the
    /// router would do stage <c>D</c>'s job in the UI and have to be undone.
    /// </summary>
    [Theory]
    [InlineData(BlueprintMyBlueprintModel.SectionVariables,    "Variables")]
    [InlineData(BlueprintMyBlueprintModel.SectionParameters,   "Inputs")]
    [InlineData(BlueprintMyBlueprintModel.SectionWorkingState, "Working State")]
    public void ClickingAGlobal_ResolvesToThatSectionsList(string sectionId, string heading)
    {
        var (window, asset) = MakeOutline();
        BlueprintDocumentFactory.CreateDeclaration(asset, KindOf(sectionId), "Health", "System.Int32");

        var selection = window.ResolveVariableSelection(Item(sectionId));

        Assert.True(selection.HasRows);
        Assert.Equal(heading, selection.Heading);
        Assert.Equal(new[] { "Health" }, selection.Source!.GetRows().Select(r => r.ShortName));
    }

    /// <summary>
    /// ⭐⭐ <b>A local click yields the CURRENT GRAPH's locals</b> — the half that makes ruling 2 a
    /// routing model rather than a filter. ⭐ The heading names the graph, because the two lists are
    /// otherwise identical tables.
    /// </summary>
    [Fact]
    public void ClickingALocal_ResolvesToTheCurrentGraphsLocals()
    {
        var tick  = new Graph { Id = Guid.NewGuid(), Name = "Tick",  Kind = GraphKind.Function };
        var other = new Graph { Id = Guid.NewGuid(), Name = "Other", Kind = GraphKind.Function };
        tick.LocalVariables.Add(Decl("Scratch"));
        other.LocalVariables.Add(Decl("Elsewhere"));

        var current = tick.Id;
        var (window, _) = MakeOutline(g => { g.Graphs.Add(tick); g.Graphs.Add(other); }, () => current);

        var selection = window.ResolveVariableSelection(
            Item(BlueprintMyBlueprintModel.SectionLocalVariables));

        Assert.True(selection.HasRows);
        Assert.Equal("Local Variables — Tick", selection.Heading);
        Assert.Equal(new[] { "Scratch" }, selection.Source!.GetRows().Select(r => r.ShortName));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And it FOLLOWS the canvas.</b> ⛔ A captured graph would go stale on the first
    /// <c>BP-24</c> graph switch — the same delegate discipline the locals section already uses.
    /// </summary>
    [Fact]
    public void SwitchingGraph_ChangesWhichLocalsTheClickResolvesTo()
    {
        var tick  = new Graph { Id = Guid.NewGuid(), Name = "Tick",  Kind = GraphKind.Function };
        var other = new Graph { Id = Guid.NewGuid(), Name = "Other", Kind = GraphKind.Function };
        tick.LocalVariables.Add(Decl("Scratch"));
        other.LocalVariables.Add(Decl("Elsewhere"));

        var current = tick.Id;
        var (window, _) = MakeOutline(g => { g.Graphs.Add(tick); g.Graphs.Add(other); }, () => current);

        current = other.Id;
        var selection = window.ResolveVariableSelection(
            Item(BlueprintMyBlueprintModel.SectionLocalVariables));

        Assert.Equal("Local Variables — Other", selection.Heading);
        Assert.Equal(new[] { "Elsewhere" }, selection.Source!.GetRows().Select(r => r.ShortName));
    }

    /// <summary>
    /// ⛔ A graph, function, macro or custom-event row is NOT a variable ⇒ the Details panel must let
    /// go, not leave a stale list beside an unrelated selection.
    /// </summary>
    [Theory]
    [InlineData(BlueprintMyBlueprintModel.SectionGraphs)]
    [InlineData(BlueprintMyBlueprintModel.SectionFunctions)]
    [InlineData(BlueprintMyBlueprintModel.SectionMacros)]
    [InlineData(BlueprintMyBlueprintModel.SectionCustomEvents)]
    public void ClickingANonVariable_ResolvesToNothing(string sectionId)
    {
        var (window, _) = MakeOutline();
        Assert.False(window.ResolveVariableSelection(Item(sectionId)).HasRows);
    }

    /// <summary>⭐ And a null selection — the outline deselecting — is the same "let go".</summary>
    [Fact]
    public void DeselectingResolvesToNothing()
    {
        var (window, _) = MakeOutline();
        Assert.False(window.ResolveVariableSelection(null).HasRows);
    }

    // ══ the wiring, on the CONSTRUCTED objects ═══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The routing is DERIVED by the registrar</b>, from the two windows the composition root
    /// already hands it — ⛔ <b>not another line <c>EditorSubsystem</c> must remember.</b> Batches
    /// 79, 80 and 81 each lost a surface to a seam of exactly this shape.
    /// </summary>
    [Fact]
    public void TheRegistrar_ConnectsTheOutlineToDetails_InEitherOrder()
    {
        foreach (var detailsFirst in new[] { false, true })
        {
            var wm  = new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));
            var reg = MakeRegistrar();
            var outline = new BlueprintMyBlueprintWindow();
            var details = MakeDetails();

            Assert.False(reg.OutlineIsRoutedToDetails);

            if (detailsFirst) { reg.RegisterExtraWindow(wm, details); reg.RegisterExtraWindow(wm, outline); }
            else              { reg.RegisterExtraWindow(wm, outline); reg.RegisterExtraWindow(wm, details); }

            Assert.True(reg.OutlineIsRoutedToDetails,
                $"the registrar did not connect the pair (detailsFirst={detailsFirst}).");
        }
    }

    /// <summary>
    /// 🔴🔴 <b>End to end, on the path the editor actually takes.</b> ⛔ Nothing here subscribes an
    /// event or calls <c>ShowVariables</c> — the registrar wires it, the outline publishes it, and the
    /// Details panel ends up hosting a list.
    /// </summary>
    [Fact]
    public void AnOutlineSelection_ReachesTheDetailsPanel()
    {
        var wm  = new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));
        var reg = MakeRegistrar();
        var (outline, asset) = MakeOutline();
        var details = MakeDetails();
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        reg.RegisterExtraWindow(wm, outline);
        reg.RegisterExtraWindow(wm, details);

        outline.PublishSelection(Item(BlueprintMyBlueprintModel.SectionVariables));

        Assert.True(details.Variables.HasContent);
        Assert.Equal("Variables", details.Variables.Heading);
        Assert.Equal(new[] { "Health" },
                     details.Variables.Model.Build().AllRows.Select(r => r.ShortName));
    }

    /// <summary>⭐ And selecting a graph afterwards makes it let go again.</summary>
    [Fact]
    public void SelectingANonVariableAfterwards_ClearsTheList()
    {
        var wm  = new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));
        var reg = MakeRegistrar();
        var (outline, asset) = MakeOutline();
        var details = MakeDetails();
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        reg.RegisterExtraWindow(wm, outline);
        reg.RegisterExtraWindow(wm, details);

        outline.PublishSelection(Item(BlueprintMyBlueprintModel.SectionVariables));
        Assert.True(details.Variables.HasContent);

        outline.PublishSelection(Item(BlueprintMyBlueprintModel.SectionFunctions));
        Assert.False(details.Variables.HasContent);
    }

    // ══ the arms do not fight ════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Last selection wins, in BOTH directions.</b> A variable click takes the panel from the
    /// node arm; ⛔ a LATER node click takes it back. ⚠ Merely asking <i>"is a node selected?"</i>
    /// would let a stale node selection outrank a fresh variable click.
    /// </summary>
    [Fact]
    public void ANodeSelectedAfterAVariable_TakesThePanelBack()
    {
        // ⚠ EditorSelectionStore.ActiveSubSelection is a NO-OP without an active asset — measured
        //   while writing this. ⛔ Without the asset the test would pass vacuously against a store
        //   that never changed.
        var store   = StoreWithAnAsset();
        var details = MakeDetails(store);

        details.ShowVariables(new VariableOutlineSelection(
            "Variables", new FixedVariableRowSource(Array.Empty<VariableRow>())));
        Assert.True(details.ShowingVariables);

        store.ActiveSubSelection = new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(details.ShowingVariables);
        Assert.True(details.Variables.HasContent);   // ⭐ kept, so re-selecting is cheap
    }

    /// <summary>⭐ A variable click while a node is selected wins — it is the newer choice.</summary>
    [Fact]
    public void AVariableSelectedAfterANode_TakesThePanel()
    {
        var store   = StoreWithAnAsset();
        var details = MakeDetails(store);
        store.ActiveSubSelection = new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid());
        Assert.NotNull(store.ActiveSubSelection);   // ⛔ guard: no active asset ⇒ silent no-op

        details.ShowVariables(new VariableOutlineSelection(
            "Variables", new FixedVariableRowSource(Array.Empty<VariableRow>())));

        Assert.True(details.ShowingVariables);
    }

    // ══ authoring-time honesty ═══════════════════════════════════════════════

    /// <summary>
    /// ⚠ <b>No entity at authoring time ⇒ <c>(pending)</c>, not <c>&lt;unreadable&gt;</c>.</b>
    /// 🔴 <c>SectionVariableRowSource</c> hard-coded <c>HasEverBeenWritten: true</c> and required a
    /// reader ⇒ every authored row would have claimed a decode failure that never happened.
    /// ⭐ Same rule as <c>BlackboardSectionRowSource</c>, which is the point — one rule, one meaning.
    /// </summary>
    [Fact]
    public void AnAuthoringRow_ReadsAsPending_NotUnreadable()
    {
        var (window, asset) = MakeOutline();
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        var row = Assert.Single(
            window.ResolveVariableSelection(Item(BlueprintMyBlueprintModel.SectionVariables))
                  .Source!.GetRows());

        Assert.False(row.HasEverBeenWritten);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     new VariableValueFormatter(RawValueDecoder.Instance).Cell(row));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar MakeRegistrar()
        => new(
            perspectiveName: "Blueprint",
            selectionStore:  new AiSelectionStore(),
            catalog:         new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService: new StubRefactor(),
            debugRegistry:   new Hrot.Editor.AiShared.Debug.DebugSessionRegistry());

    /// <summary>
    /// ⚠ A store with an ACTIVE ASSET. <c>ActiveSubSelection</c>'s setter returns early when there is
    /// none, so a store without one silently swallows every sub-selection.
    /// </summary>
    private static AiSelectionStore StoreWithAnAsset()
    {
        var store = new AiSelectionStore { ActiveAsset = new FakeEditableAsset() };
        return store;
    }

    private sealed class FakeEditableAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "SelectionHost";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Blueprint;
        public string SourceFilePath => "/fake.bp.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }

    private static BlueprintDetailsWindow MakeDetails(AiSelectionStore? store = null)
        => new(store ?? new AiSelectionStore(), new BlueprintNodeDrawerRegistry());

    private static (BlueprintMyBlueprintWindow Window, BlueprintAsset Asset) MakeOutline(
        Action<BlueprintAsset>? configure = null, Func<Guid>? currentGraphId = null)
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "DetailsHost",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = new List<Graph>(),
            Header   = new Header(),
        };
        configure?.Invoke(asset);

        var window = new BlueprintMyBlueprintWindow();
        window.Retarget(null, asset, null, new EditorCommandsImpl(), null,
                        currentGraphId ?? (() => Guid.Empty));
        return (window, asset);
    }

    private static MyBlueprintItem Item(string sectionId)
        => new(ItemId: $"var:{Guid.NewGuid():D}", SectionId: sectionId, DisplayName: "x",
               CategoryPath: null, IconKey: null, BadgeText: null, AccentColor: null,
               Children: null, IsRenamable: true, IsDeletable: true, IsHostDefined: false,
               Tooltip: null);

    private static VariableDecl Decl(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = "System.Int32" },
    };

    private static Hrot.Blueprints.Core.Assets.DeclarationKind KindOf(string sectionId)
        => sectionId == BlueprintMyBlueprintModel.SectionParameters
               ? Hrot.Blueprints.Core.Assets.DeclarationKind.Parameter
         : sectionId == BlueprintMyBlueprintModel.SectionWorkingState
               ? Hrot.Blueprints.Core.Assets.DeclarationKind.WorkingState
               : Hrot.Blueprints.Core.Assets.DeclarationKind.Variable;

    private sealed class StubRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
    {
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public Hrot.Editor.AiShared.Refactor.RefactorPreview PreviewRename(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o)
            => new(f, t, Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorFileEdit>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyRename(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p)
            => new(true, Array.Empty<string>(), null);
        public Hrot.Editor.AiShared.Refactor.DeletePreview PreviewDelete(
            Guid id, Hrot.Editor.AiShared.Refactor.DeleteOptions o)
            => new(id, Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyDelete(
            Hrot.Editor.AiShared.Refactor.DeletePreview p)
            => new(true, Array.Empty<string>(), null);
        public Task<Hrot.Editor.AiShared.Refactor.RefactorPreview> PreviewRenameAsync(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o, CancellationToken ct = default)
            => Task.FromResult(PreviewRename(f, t, o));
        public Task<Hrot.Editor.AiShared.Refactor.RefactorResult> ApplyRenameAsync(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p, CancellationToken ct = default)
            => Task.FromResult(ApplyRename(p));
    }
}
