using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.AiEditor.Persistence;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 90 — the live projection reaches the ROW SOURCES *(<c>BP-334</c>)*.</b>
///
/// <para>🔴🔴 <b>The defect these exist to make impossible.</b> 📐
/// <c>grep -rn "readRaw:" --include=*.cs Hrot/ | grep -v Tests</c> → <b>NOTHING</b>: three production
/// sites built the Details row sources and <b>not one passed a reader</b>. ⇒ every Value cell read
/// <c>(pending)</c>, on every host, for as long as the Details panel has existed.</para>
///
/// <para>⭐⭐⭐ <b>These ask the CONSTRUCTED objects, never a call site.</b> 📌 <b><c>R-67</c></b>: <i>"a
/// rail that builds its own composition root cannot see a composition-root defect"</i> — and ⚠ <b>the
/// Blueprint registrar is the one that has forgotten a service FOUR times.</b> ⇒ every rail below
/// builds a registrar the way <c>EditorSubsystem</c> does, hands it a provider, and then reads the
/// CELL TEXT that comes out the other end.</para>
///
/// <para>⛔ <b>Not one of them passes a resolver, a projection host, or a row source.</b> If any of
/// those were required for production wiring, these would be red.</para>
/// </summary>
public sealed class TheLiveProjectionReachesTheRowSourcesTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    public void Dispose() => _atlas.Dispose();

    private static VariableValueFormatter Formatter() => new(RawValueDecoder.Instance);

    /// <summary>⭐ Built exactly as the composition root builds one.</summary>
    private static PerspectiveWorkspaceRegistrar AsTheEditorBuildsIt(
        string perspective, EditorSelectionStore store, ILiveBlackboardValueProvider? provider)
        => new(
            perspectiveName:   perspective,
            selectionStore:    store,
            catalog:           new AssetCatalog(),
            refactorService:   new StubRefactor(),
            debugRegistry:     new DebugSessionRegistry(),
            liveValueProvider: provider);

    // ══ site 1 — the AI section source gets BYTES (90c) ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE <c>90c</c> rail.</b> An outline click on BTree/HSM produces rows whose CELL TEXT is
    /// the live value. 🔴 RED before this batch: <c>_sectionSource</c> passed no <c>readRaw</c>, so the
    /// cell read <c>(pending)</c> however the sim was running.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void AnAiSectionSourceRendersLiveValues(string perspective)
    {
        var store = new EditorSelectionStore();
        var asset = FakeAsset.With(Var("Health"), Var("Ammo"));
        store.ActiveAsset = asset;

        var reg = AsTheEditorBuildsIt(perspective, store,
            new FakeProvider { Bytes = new() { ["Health"] = BitConverter.GetBytes(7f) } });

        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);

        var f    = Formatter();
        var rows = reg.Details!.Variables.Model.Build().AllRows.ToDictionary(r => r.ShortName);

        Assert.Equal("7", f.Cell(rows["Health"]));
        // ⛔ Guide C9 — absent from the live map ⇒ (pending), NOT a decoded zero.
        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(rows["Ammo"]));
    }

    /// <summary>⛔ The negative control: no provider ⇒ every AI cell is <c>(pending)</c>. ⚠ This is the
    /// state the editor shipped in, and it is why nothing looked broken.</summary>
    [Fact]
    public void WithNoProviderTheAiCellsArePending()
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = FakeAsset.With(Var("Health"));

        var reg = AsTheEditorBuildsIt("BTree", store, provider: null);
        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);

        var f = Formatter();
        Assert.All(reg.Details!.Variables.Model.Build().AllRows,
            r => Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(r)));
    }

    /// <summary>
    /// ⭐⭐ <b>A provider that is not a projection is not a failure.</b> ⚠ The type-test must degrade to
    /// <c>(pending)</c>, ⛔ never throw — a headless host may pass a string-only provider.
    /// </summary>
    [Fact]
    public void AStringOnlyProviderLeavesTheCellsPending()
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = FakeAsset.With(Var("Health"));

        var reg = AsTheEditorBuildsIt("BTree", store, new StringOnlyProvider());

        Assert.Null(reg.LiveProjection);
        reg.MyBlueprint!.SelectSection(BlackboardMyBlueprintModel.SectionInputs);
        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     Formatter().Cell(reg.Details!.Variables.Model.Build().AllRows.Single()));
    }

    /// <summary>⭐ The registrar exposes what it resolved, so the type-test itself is railable.</summary>
    [Fact]
    public void TheRegistrarResolvesAProjectionFromTheProviderItWasGiven()
    {
        var reg = AsTheEditorBuildsIt("BTree", new EditorSelectionStore(), new FakeProvider());

        Assert.NotNull(reg.LiveProjection);
    }

    // ══ the R-67 install — the registrar hands it to the Blueprint outline ═══

    /// <summary>
    /// ⭐⭐⭐ <b>The <c>90b</c> composition-root rail.</b> <c>BlueprintMyBlueprintWindow</c> BUILDS the
    /// blueprint row sources, so it needs the projection — and it arrives through
    /// <c>RegisterExtraWindow</c>, in the registrar's ONE pass. ⛔ <b>Asked of the CONSTRUCTED window</b>,
    /// not of <c>EditorSubsystem</c>'s source: 📌 <c>R-67</c>, and this registrar has forgotten a
    /// service four times.
    /// </summary>
    [Fact]
    public void TheRegistrarInstallsTheProjectionOnTheBlueprintOutline()
    {
        var wm  = new WindowManager(_atlas);
        var reg = AsTheEditorBuildsIt("Blueprint", new EditorSelectionStore(), new FakeProvider());
        reg.RegisterWindows(wm);

        var outline = new BlueprintMyBlueprintWindow();
        Assert.False(outline.HasLiveProjection);      // ⭐ RED before the production call runs

        reg.RegisterExtraWindow(wm, outline);         // ⭐⭐ the PRODUCTION path

        Assert.True(outline.HasLiveProjection);
    }

    /// <summary>⛔ And with no provider the outline is told so explicitly — ⭐ "asked, and there is
    /// none" must be distinguishable from "never asked", which is the bug this line prevents.</summary>
    [Fact]
    public void WithNoProviderTheOutlineIsStillAskedAndHasNone()
    {
        var wm  = new WindowManager(_atlas);
        var reg = AsTheEditorBuildsIt("Blueprint", new EditorSelectionStore(), provider: null);
        reg.RegisterWindows(wm);

        var outline = new BlueprintMyBlueprintWindow();
        reg.RegisterExtraWindow(wm, outline);

        Assert.False(outline.HasLiveProjection);
    }

    // ── doubles ─────────────────────────────────────────────────────────────

    /// <summary>⭐ Implements BOTH interfaces, exactly as the two production providers now do.</summary>
    private sealed class FakeProvider : ILiveBlackboardValueProvider, ILiveVariableProjection
    {
        public Dictionary<string, byte[]>? Bytes   { get; set; }
        public Dictionary<string, object>? Objects { get; set; }

        public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
            => new Dictionary<string, string>();

        public IReadOnlyDictionary<string, byte[]>?  GetLiveBytes(IEditableAsset asset)   => Bytes;
        public IReadOnlyDictionary<string, object>?  GetLiveObjects(IEditableAsset asset) => Objects;
    }

    /// <summary>⛔ The pre-Batch-90 shape: strings only, no projection.</summary>
    private sealed class StringOnlyProvider : ILiveBlackboardValueProvider
    {
        public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
            => new Dictionary<string, string> { ["Health"] = "7" };
    }

    private static BlackboardVariableEntry Var(string n) => new(n, typeof(float), null);

    private sealed class FakeAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        private FakeAsset(IEnumerable<BlackboardVariableEntry> vars) => _vars = vars.ToList();
        public static FakeAsset With(params BlackboardVariableEntry[] vars) => new(vars);

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "Alpha";
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
