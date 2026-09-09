using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 88 item <c>88b</c> / <c>BP-317</c> — BTree and HSM have a Details panel.</b>
///
/// <para>📄 <b>Design basis</b> — <c>Q32</c> ruling 6: <i>"The same Details panel is REUSED for every
/// asset type — HSM, BTree, Blueprint ⇒ this is a cross-host deliverable, not a blueprint one."</i>
/// 📌 <c>R-60</c> recorded that the AI perspectives had <b>no Details window at all</b>, and
/// <c>R-62</c> cites exactly that for keeping visual checks suspended on those two hosts.</para>
///
/// <para>⭐⭐⭐ <b>What these rails ASK</b> *(gate 9, and Batch 87's lesson)*. ⛔ <b>Not that a method
/// returns something</b> — 📌 Batch 87: <c>IsSelected</c> returned <c>true</c> throughout the defect it
/// was meant to catch. ⇒ every assertion below reads the <b>CONSTRUCTED</b> window a registrar built
/// the way the composition root builds one: the panel object, the rows its model builds, the window
/// the <c>WindowManager</c> actually holds. 📌 <c>R-67</c> — <i>"a rail that builds its own composition
/// root cannot see a composition-root defect"</i>, so ⛔ <b>none of these passes a resolver, a host
/// kind or a details host</b>. If any of those were required, the editor would be broken and these
/// would go red.</para>
///
/// <para>⚠ <b>What they cannot see</b>, stated rather than implied: that ImGui draws the panel. The
/// draw path needs a context no headless test can create — 📌 <c>R-21</c>/<c>R-62</c>: <b>no visual
/// checks</b>. These prove the panel exists, is registered, is routed, is gesture-bound and holds the
/// right rows.</para>
/// </summary>
public sealed class TheAiHostsHaveADetailsPanelTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    /// <summary>⭐ Built exactly as <c>EditorSubsystem</c> builds one — ⛔ no <c>hostKind</c>, no
    /// resolver, no details host passed in.</summary>
    private static PerspectiveWorkspaceRegistrar AsTheEditorBuildsIt(
        string perspective, EditorSelectionStore store, bool withEditService = false)
        => new(
            perspectiveName:  perspective,
            selectionStore:   store,
            catalog:          new AssetCatalog(),
            refactorService:  new StubRefactor(),
            debugRegistry:    new DebugSessionRegistry(),
            facetEditService: withEditService ? new ComponentEditServiceBuilder().Build() : null);

    // ══ the panel exists on the AI hosts, and only there ═════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail.</b> 🔴 RED before this batch on both hosts: the property did not exist and
    /// <b>no window titled "Details" was registered on any AI perspective</b> — measured by
    /// <c>search_graph</c> (gate 8): exactly one such window existed, on Blueprint.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b><c>S1</c> (<c>BP-399</c>, <c>2026-08-22</c>) added the <c>Blueprint</c> row.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3 ①: the shell is built for EVERY perspective.
    /// ⚠ Blueprint used to be the <b>negative</b> control here — see
    /// <see cref="TheBlueprintPerspective_KeepsItsPersistedWindowId"/> for what replaced it and why.
    /// </remarks>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void AnAiPerspective_GetsADetailsPanel(string perspective)
    {
        var reg = AsTheEditorBuildsIt(perspective, new EditorSelectionStore());

        Assert.NotNull(reg.Details);
        Assert.Equal("Details", reg.Details!.Title);
        Assert.Equal(perspective, reg.Details.OwningPerspective);
    }

    /// <summary>
    /// ⛔⛔ <b>RE-EXPRESSED at <c>S1</c> — the claim INVERTED, and the ruling behind it did not.</b>
    ///
    /// <para>📌 <b>What this rail used to say:</b> <i>"Blueprint gets NO ai details panel — it already has
    /// <c>BlueprintDetailsWindow</c>; a second Details there would be two panels for one concept
    /// (ruling 9), and it would collide on the window id."</i> ⭐ <b>Both halves were correct and both are
    /// now satisfied the other way round:</b> §7.3 ①③ keeps ONE panel by <b>retiring</b>
    /// <c>BlueprintDetailsWindow</c> and handing Blueprint the SAME shell — ⛔ not by leaving Blueprint
    /// without one.</para>
    ///
    /// <para>⭐⭐⭐ <b>So the reachable claim moved to the id</b>, which is the half that can still break
    /// silently. 📄 §5 / <c>TASKS_One_Shell_BP399.md</c> §4: <i>"the persisted ids are KEPT —
    /// <c>ai_details_blueprint</c> stays"</i>, because a bare key rename <b>silently resets every saved
    /// layout</b>. ⚠ Nothing else in the suite pins that string, and a refactor that regenerated the id
    /// from the perspective name would produce a working editor with everyone's docking lost.</para>
    /// </summary>
    [Fact]
    public void TheBlueprintPerspective_KeepsItsPersistedWindowId()
        => Assert.Equal("ai_details_blueprint",
                        AsTheEditorBuildsIt("Blueprint", new EditorSelectionStore()).Details!.Id);

    /// <summary>
    /// ⭐⭐ <b>Registered by <c>RegisterWindows</c>, not left to the host.</b> 🔴 Asked of the
    /// <c>WindowManager</c> — 📌 Batch 81: two windows claiming one id is a SILENT eviction, and a
    /// window nobody registered is simply absent from the Window menu with nothing logged.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheDetailsPanel_IsRegisteredWithTheWindowManager(string perspective)
    {
        var wm  = new WindowManager(_atlas);
        var reg = AsTheEditorBuildsIt(perspective, new EditorSelectionStore());
        reg.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow(reg.Details!.Id, out var found));
        Assert.Same(reg.Details, found);
    }

    /// <summary>⚠ The AI hosts must not share an id — all three perspectives exist at once, and the
    /// later registration would evict the earlier window.
    /// <para>⭐ <b><c>S1</c> widened this from two to THREE</b>: Blueprint's shell now goes through the
    /// same registrar, so it joins the same collision surface. 📌 <c>RegisterCore</c> throws on a
    /// duplicate id (Batch 81's guard) — which is exactly why <c>S1</c> could not be staged as "add the
    /// shell now, retire the old window later" (<c>TASKS_One_Shell_BP399.md</c> §3).</para></summary>
    [Fact]
    public void TheAiDetailsPanels_HaveDistinctIds()
    {
        var ids = new[] { "BTree", "HSM", "Blueprint" }
                  .Select(p => AsTheEditorBuildsIt(p, new EditorSelectionStore()).Details!.Id)
                  .ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // ══ the routing — outline click → the panel's ROWS ═══════════════════════

    /// <summary>⭐ The pair is connected at construction, through the ONE path Blueprint uses.</summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheOutlineIsRoutedToTheDetailsPanel(string perspective)
        => Assert.True(AsTheEditorBuildsIt(perspective, new EditorSelectionStore())
                       .OutlineIsRoutedToDetails);

    /// <summary>
    /// ⭐⭐⭐ <b>THE artefact rail — an outline click puts the right ROWS in the panel.</b>
    ///
    /// <para>⛔ Deliberately NOT an assertion that the event fired: 📌 <c>Q32</c> ruling 2 is about what
    /// the designer SEES. ⇒ this reads <c>Model.Build()</c> on the panel's own table, which is the
    /// projection the control draws.</para>
    ///
    /// <para>⚠ Nobody calls <c>SetSelectionResolver</c> or <c>SetSectionSourceResolver</c> here — 📌
    /// Batch 79's routing was inert in the editor precisely because it needed a call no production
    /// caller made.</para>
    /// </summary>
    [Fact]
    public void SelectingASection_PutsThatSectionsRowsInTheDetailsPanel()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);
        store.ActiveAsset = FakeAsset.With(Var("Health"), Var("Ammo"), State("Cursor"));

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);

        Assert.True(reg.Details!.ShowingVariables);
        Assert.Equal("Inputs", reg.Details.Heading);
        Assert.Equal(new[] { "Ammo", "Health" }, DetailsRowNames(reg));

        reg.MyBlueprint.SelectSection(BlackboardMyBlueprintModel.SectionWorkingState);

        Assert.Equal("Working State", reg.Details.Heading);
        Assert.Equal(new[] { "Cursor" }, DetailsRowNames(reg));
    }

    /// <summary>
    /// ⭐⭐ <b>The heading is the section's DISPLAY name, not its id.</b> ⛔ Without this the panel would
    /// read <c>"bb.assetGlobals"</c> — and it would still have "a heading", which is why the rail above
    /// asserts the string rather than non-nullness.
    /// </summary>
    [Fact]
    public void TheHeadingIsTheSectionsDisplayName()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("HSM", store);
        store.ActiveAsset = FakeAsset.With(Global("Wave"));

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionAssetGlobals);

        Assert.Equal("Asset Globals", reg.Details!.Heading);
    }

    /// <summary>
    /// ⭐⭐ <b>The clicked ROW is highlighted</b> — 📌 §1: <i>"the routing key is <c>(asset, section)</c>
    /// <b>+ a highlight</b>."</i> 🔴 <c>MyBlueprintPanel</c> always handed <c>(sectionId, itemId)</c> to
    /// its navigate callback and <c>AiMyBlueprintWindow</c> <b>discarded the item id</b>, so no AI row
    /// could be highlighted however the table drew.
    /// </summary>
    [Fact]
    public void TheClickedRowIsTheSelectedRowInTheDetailsPanel()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);
        store.ActiveAsset = FakeAsset.With(Var("Health"), Var("Ammo"));

        reg.MyBlueprint!.SelectItem(BlackboardMyBlueprintModel.SectionInputs, "Ammo");

        Assert.Equal("Ammo", reg.Details!.Variables.SelectedVariablePath);
        var view = reg.Details.Variables.Model.Build();
        Assert.True(view.IsSelected(view.AllRows.Single(r => r.ShortName == "Ammo")));
        Assert.False(view.IsSelected(view.AllRows.Single(r => r.ShortName == "Health")));
    }

    /// <summary>
    /// ⛔ <b>An unknown section CLEARS the panel</b> rather than leaving a stale list beside an
    /// unrelated selection — the same rule Blueprint's host follows for a graph or function row.
    /// </summary>
    [Fact]
    public void AnUnroutableSelectionClearsThePanel()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);
        store.ActiveAsset = FakeAsset.With(Var("Health"));

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);
        Assert.True(reg.Details!.ShowingVariables);

        reg.Details.ShowVariables(VariableOutlineSelection.None);

        Assert.False(reg.Details.ShowingVariables);
        Assert.Null(reg.Details.Heading);
    }

    /// <summary>⭐ Before any click the panel shows NOTHING — ⛔ not an empty table, which reads as
    /// "this asset has no variables".</summary>
    [Fact]
    public void BeforeAnyClick_ThePanelShowsNothing()
        => Assert.False(AsTheEditorBuildsIt("BTree", new EditorSelectionStore())
                        .Details!.ShowingVariables);

    // ══ the services the panel needs, asked of the panel ═════════════════════

    /// <summary>
    /// ⭐⭐ <b>Row 58 — the run-state source is installed by the registrar</b>, so the ONE Value column
    /// can switch meaning. ⛔ Not a composition-root argument: 📌 batches 79–82 each lost a surface to
    /// a seam of exactly that shape.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheDetailsPanel_HasItsRunStateSource(string perspective)
        => Assert.True(AsTheEditorBuildsIt(perspective, new EditorSelectionStore())
                       .Details!.Variables.HasRunStateSource);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 87's contract, applied to the new host.</b> 🔴 The twelfth instance was a Details
    /// table with no menu and no double-click. ⛔ Asked of <see cref="PerspectiveWorkspaceRegistrar.BoundTables"/>,
    /// which records what was ATTACHED — 📌 <c>R-67</c>.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheDetailsTable_IsGestureBound(string perspective)
    {
        var reg = AsTheEditorBuildsIt(perspective, new EditorSelectionStore(), withEditService: true);

        var table = ((IVariableTableHost)reg.Details!).VariableTable;
        Assert.NotNull(table);
        Assert.Contains(table!, reg.BoundTables);
        Assert.True(table!.HasEditGestures);
    }

    /// <summary>
    /// ⭐⭐ <b>The panel does NOT claim the Details surface</b>, and that is deliberate. 📌 The
    /// registrar's own rule: <i>"only CONTRIBUTORS are wired here — the Watch, the Inspector and
    /// Details itself must not claim, or a window that does not drive the panel would steal it."</i>
    /// ⚠ This window has ONE arm *(no node arm — <c>InspectorWindow</c> stays, <c>BP-295</c>)</i>, so
    /// there is nothing to arbitrate.
    /// </summary>
    [Fact]
    public void TheDetailsPanel_IsNotADetailsSurfaceClaimant()
        // ⭐ Over the TYPE, not `is` — the compiler proves an `is` against a sealed non-implementer
        //   statically (CS0184) and the rail would not compile the day someone adds the interface,
        //   which is precisely the day it should FAIL instead.
        => Assert.DoesNotContain(typeof(IDetailsSurfaceClaimant),
                                 typeof(DetailsWindow).GetInterfaces());

    /// <summary>⭐ The outline still claims, unchanged — the half that DOES drive the panel.</summary>
    [Fact]
    public void TheOutline_StillClaimsTheDetailsSurface()
    {
        var outline = AsTheEditorBuildsIt("HSM", new EditorSelectionStore()).MyBlueprint!;

        Assert.Equal(SelectionOrigin.VariableOutline,
                     ((IDetailsSurfaceClaimant)outline).DetailsOrigin);
    }

    /// <summary>
    /// ⚠ <b>The standalone table is NOT replaced.</b> ⛔ Retiring it is <c>U-16</c> / row 60 and is
    /// explicitly out of this batch's scope — ⭐ two LISTENERS of one gesture, not two mechanisms.
    /// </summary>
    [Fact]
    public void TheStandaloneVariablesTable_StillFollowsTheSameClick()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);
        store.ActiveAsset = FakeAsset.With(Var("Health"), State("Cursor"));

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);

        Assert.Equal(new[] { "Health" },
                     reg.Variables.Model.Build().AllRows.Select(r => r.ShortName));
        Assert.Equal(new[] { "Health" }, DetailsRowNames(reg));
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string[] DetailsRowNames(PerspectiveWorkspaceRegistrar reg)
        => reg.Details!.Variables.Model.Build().AllRows
              .Select(r => r.ShortName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static BlackboardVariableEntry Var(string n) => new(n, typeof(float), null);

    private static BlackboardVariableEntry State(string n)
        => new(n, typeof(int), null, Role: BlackboardVariableRole.State, Scope: WorkingStateScope.Node);

    private static BlackboardVariableEntry Global(string n)
        => new(n, typeof(int), null, Role: BlackboardVariableRole.State, Scope: WorkingStateScope.Behavior);

    private sealed class FakeAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        private FakeAsset(IEnumerable<BlackboardVariableEntry> vars) => _vars = vars.ToList();

        public static FakeAsset With(params BlackboardVariableEntry[] vars) => new(vars);

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "FakeAsset";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/fake.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
