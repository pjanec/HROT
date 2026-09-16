using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Track C reaches the RUNNING EDITOR — not just a test-built registrar.</b>
///
/// <para>🔴🔴 <b>The fifth instance of one pattern, and it was inside the batch that existed to fix the
/// pattern.</b> 📐 Batch 79 hosted five surfaces and gated the outline on a new <c>hostKind</c>
/// parameter — then wired <b>only the test caller</b>. <c>EditorSubsystem</c> passed it to none of its
/// three registrars, so in the running editor the outline was still never constructed and the
/// Variables window was never routed. ⛔ <b>Every Batch-79 rail was green throughout</b>, because each
/// one built its own registrar and passed the argument the production caller did not.</para>
///
/// <para>⭐⭐ <b>So these rails assert the DEFAULT path — a registrar built the way the composition
/// root builds one.</b> ⛔ None of them passes <c>hostKind</c>, sets a section-source resolver, or
/// calls <c>Retarget</c>: if any of those is required, the editor is broken and these go red.</para>
///
/// <para>⭐ <b>And the fix is a removal, not a check.</b> The host kind is now DERIVED from the
/// perspective name, which the registrar already knows — ⇒ there is no argument left to forget.
/// The parameter survives only as an override.</para>
/// </summary>
public sealed class TrackCReachesTheEditorTests
{
    /// <summary>
    /// ⭐ Built exactly as <c>EditorSubsystem</c> builds one: perspective name and services, ⛔ and
    /// <b>no <c>hostKind</c></b>.
    /// </summary>
    private static PerspectiveWorkspaceRegistrar AsTheEditorBuildsIt(
        string perspective, EditorSelectionStore store)
        => new(
            perspectiveName: perspective,
            selectionStore:  store,
            catalog:         new AssetCatalog(),
            refactorService: new StubRefactor(),
            debugRegistry:   new DebugSessionRegistry());

    // ══ the derivation — the thing that removes the class of defect ══════════

    [Theory]
    [InlineData("BTree",     BlackboardHostKind.BTree)]
    [InlineData("HSM",       BlackboardHostKind.Hsm)]
    [InlineData("btree",     BlackboardHostKind.BTree)]   // ⭐ casing must not silently drop a panel
    [InlineData("hsm",       BlackboardHostKind.Hsm)]
    public void ThePerspectiveName_DeterminesTheHostKind(string name, BlackboardHostKind expected)
        => Assert.Equal(expected, PerspectiveWorkspaceRegistrar.HostKindOf(name));

    /// <summary>⛔ The other half: Blueprint has its own outline, and an unknown name gets none.</summary>
    [Theory]
    [InlineData("Blueprint")]
    [InlineData("Scenario")]
    public void OtherPerspectives_DeriveNoHostKind(string name)
        => Assert.Null(PerspectiveWorkspaceRegistrar.HostKindOf(name));

    // ══ the production default path ══════════════════════════════════════════

    /// <summary>
    /// 🔴 <b>RED before Batch 80</b>, and green throughout Batch 79's own suite — which is the point.
    /// A registrar built without <c>hostKind</c> must still get its outline.
    /// </summary>
    [Theory]
    [InlineData("BTree", BlackboardHostKind.BTree)]
    [InlineData("HSM",   BlackboardHostKind.Hsm)]
    public void ARegistrarBuiltWithoutAHostKind_StillGetsItsOutline(
        string perspective, BlackboardHostKind expected)
    {
        var reg = AsTheEditorBuildsIt(perspective, new EditorSelectionStore());

        Assert.NotNull(reg.MyBlueprint);
        Assert.Equal(expected, reg.MyBlueprint!.Host);
    }

    /// <summary>⛔ And Blueprint still gets none, by the same default path.</summary>
    [Fact]
    public void TheBlueprintPerspective_StillGetsNoOutline_OnTheDefaultPath()
        => Assert.Null(AsTheEditorBuildsIt("Blueprint", new EditorSelectionStore()).MyBlueprint);

    // ══ following the selection, with nobody calling Retarget ════════════════

    /// <summary>
    /// ⭐⭐ <b>The outline follows the active document by itself.</b> 🔴 Batch 79 left retargeting to
    /// the host and no host did it — so an outline that only works when someone remembers to call
    /// <c>Retarget</c> is the same defect one level up. ⛔ This test never calls it.
    /// </summary>
    [Fact]
    public void TheOutline_FollowsTheActiveAsset_WithoutAnyoneCallingRetarget()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);

        Assert.Null(reg.MyBlueprint!.Model);            // nothing active yet

        store.ActiveAsset = FakeAsset.With(Var("Health"), State("Cursor"));
        reg.MyBlueprint.SyncToSelection();

        Assert.NotNull(reg.MyBlueprint.Model);
        Assert.Equal(new[] { "Health" },
            reg.MyBlueprint.Model!.GetItems(BlackboardMyBlueprintModel.SectionInputs)
               .Select(i => i.DisplayName));
    }

    /// <summary>⭐ And it lets go when the document closes — a stale outline is worse than none.</summary>
    [Fact]
    public void TheOutline_ClearsWhenTheAssetGoesAway()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("HSM", store);

        store.ActiveAsset = FakeAsset.With(Var("Health"));
        reg.MyBlueprint!.SyncToSelection();
        Assert.NotNull(reg.MyBlueprint.Model);

        store.ActiveAsset = null;
        reg.MyBlueprint.SyncToSelection();
        Assert.Null(reg.MyBlueprint.Model);
    }

    // ══ routing, with nobody setting a resolver ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Selection routes the table on the DEFAULT path.</b> 🔴 Batch 79 made this depend on the
    /// host calling <c>SetSectionSourceResolver</c>, and no production caller did — so the routing was
    /// inert in the editor while its rail was green. ⛔ This test never sets a resolver.
    /// </summary>
    [Fact]
    public void SelectingASection_FiltersTheTable_WithNoResolverSupplied()
    {
        var store = new EditorSelectionStore();
        var reg   = AsTheEditorBuildsIt("BTree", store);
        store.ActiveAsset = FakeAsset.With(Var("Health"), Var("Ammo"), State("Cursor"));

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);
        Assert.Equal(new[] { "Ammo", "Health" }, SortedNames(reg));

        reg.MyBlueprint.SelectSection(BlackboardMyBlueprintModel.SectionWorkingState);
        Assert.Equal(new[] { "Cursor" }, SortedNames(reg));
    }

    /// <summary>
    /// ⭐⭐ <b>The source FILTERS.</b> ⚠ Measured while wiring this: <c>SectionVariableRowSource</c>
    /// tags every row with a section string and filters by <b>nothing</b> — routing through it would
    /// have shown the whole blackboard under every heading. ⛔ Without this arm the routing test above
    /// would pass for a source that ignores its section entirely.
    /// </summary>
    [Fact]
    public void TheSectionSource_ReturnsOnlyThatSectionsVariables()
    {
        var asset  = FakeAsset.With(Var("Health"), State("Cursor"), Global("Wave"));
        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionAssetGlobals);

        Assert.Equal(new[] { "Wave" }, source.GetRows().Select(r => r.ShortName));
    }

    /// <summary>
    /// ⚠ <b>No entity at authoring time ⇒ <c>(pending)</c>, not <c>&lt;unreadable&gt;</c>.</b>
    /// ⛔ Rendering a decode failure that never happened would send a designer looking for a bug in
    /// their type.
    /// </summary>
    [Fact]
    public void AnAuthoringRow_ReadsAsPending_NotUnreadable()
    {
        var asset  = FakeAsset.With(Var("Health"));
        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionInputs);
        var row    = Assert.Single(source.GetRows());

        Assert.False(row.HasEverBeenWritten);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     new VariableValueFormatter(RawValueDecoder.Instance).Cell(row));
    }

    /// <summary>
    /// ⭐ One classification, not two: an editor-owned variable is node-owned in the TABLE as well as
    /// in the outline, and the precedence has a single home.
    /// </summary>
    [Fact]
    public void AnEditorOwnedVariable_IsNodeOwnedInTheTable()
    {
        var asset  = FakeAsset.With(new BlackboardVariableEntry("Auto", typeof(int), null, IsAutoManaged: true));
        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionInputs);

        var row = Assert.Single(source.GetRows());
        Assert.Equal(VariableRowKind.NodeOwned, row.RowKind);
        Assert.False(row.CanEverBeWritten);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string[] SortedNames(PerspectiveWorkspaceRegistrar reg)
        => reg.Variables.Model.Build().AllRows
              .Select(r => r.ShortName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static BlackboardVariableEntry Var(string n)
        => new(n, typeof(float), null);

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
