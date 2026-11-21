using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 98 (<c>98a</c>) — OK LANDS on a Blueprint variable while PLANNING.</b>
///
/// <para>🔴🔴 <b>The defect, measured.</b> <c>PerspectiveWorkspaceRegistrar.DeclarationOwnerOf</c>
/// resolves the write target as <c>store.ActiveAsset is IBlackboardManagedAsset asset ? asset : null</c>
/// — ⛔ <b>and <c>BlueprintAsset</c> does not implement that interface.</b> ⇒ in <b>PLANNING</b>, the
/// ordinary authoring state, <c>TargetFor</c> chooses <c>InitialValue</c>,
/// <c>CommitInitialValue</c> hit <c>if (asset is null) return RefusedNoDeclarationOwner;</c>, and
/// <b>OK refused on every Blueprint variable, every time.</b></para>
///
/// <para>⭐⭐ <b>The asymmetry was in the SAME FILE and had been NAMED.</b> <c>:836</c>
/// <c>ResolveEntry</c> asks the ROW first — <c>95a</c>'s fix — and <c>:826</c>
/// <c>DeclarationOwnerOf</c> does not. 📌 <c>BP-355</c> says so in as many words: <i>"the same
/// vocabulary mismatch <c>95a</c> fixed for READING, unfixed for WRITING."</i></para>
///
/// <para>⭐⭐⭐ <b>WHICH LAYER EACH RAIL FAKES</b> *(📌 <c>M-22</c>, <c>M-29</c>)*: everything below runs
/// the <b>real</b> <see cref="EditorSubsystem"/> composition root, its <b>real</b> registrar and
/// binder, the <b>real</b> <see cref="BlueprintMyBlueprintWindow"/> row source and a <b>real</b>
/// <see cref="BlueprintAsset"/>. ⚠ <b>Faked: the DRAW layer only</b> — 📌 <c>R-21</c>/<c>R-62</c>, the
/// gesture is raised by calling <c>OnEditValue</c> because no headless rail can drive ImGui.
/// ⛔ <b>Nothing about the declaration, the write or the dirty flag is faked</b>, which is the point:
/// the previous rails on this path all asserted a delegate was non-null.</para>
/// </summary>
public sealed class TheBlueprintPlanningEditLandsTests : IDisposable
{
    private readonly IconAtlas _atlas = new(IntPtr.Zero, 16f, 16f);
    private readonly string    _tempDir =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bp98a-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        _atlas.Dispose();
    }

    // ══ the money rail ═══════════════════════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before <c>98a</c>: <c>RefusedNoDeclarationOwner</c>.</b>
    /// ⭐⭐ One gesture, one Accept, and <b>the DECLARATION'S JSON CHANGED</b> — ⛔ not that a delegate
    /// is non-null, and ⛔ not that the dialog opened. 📌 <c>M-22</c>.
    /// </summary>
    [Fact]
    public void APlanningEdit_LandsInTheDeclaration()
    {
        var s = Scene();

        // ⭐ The authored default before the edit — measured, not assumed.
        Assert.Equal("1", DeclOf(s.Asset).DefaultValueJson);

        s.Registrar.EditGestures!.OnEditValue(s.Row);
        var outcome = s.Registrar.EditGestures!.Accept();

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        // ⛔⛔ THE DESTRUCTIVE CASE, and it is why this assertion is "1" and not "landed".
        // 🔴 Measured while building 98a: BlueprintVariableSchemaSource.Entries never projected
        //    DefaultValueJson, so the session opened at the TYPE's default (0) rather than the
        //    variable's (1). ⚠ Harmless while OK refused; once 98a made the write land, an OK with
        //    NOTHING TYPED silently overwrote the authored 1 with 0. ⇒ an unedited round trip must
        //    be a NO-OP in value terms.
        Assert.Equal("1", DeclOf(s.Asset).DefaultValueJson);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A CHANGED value reaches the declaration</b> — the assertion that separates <i>"the
    /// commit ran"</i> from <i>"the commit wrote what the designer typed"</i>.
    ///
    /// <para>⚠ The dialog itself cannot be typed into headlessly *(<c>R-21</c>)*, so the edit is made
    /// on the open session's document — the same object <c>DrawLeafNode</c> mutates through
    /// <c>node.Binding.SetBoxed</c>. ⭐ <b>That is the layer faked, and it is one layer.</b></para>
    /// </summary>
    [Fact]
    public void TheValueTheDesignerTyped_IsWhatLands()
    {
        var s = Scene();

        s.Registrar.EditGestures!.OnEditValue(s.Row);
        var session = s.Registrar.EditGestures!.ActiveSession;
        Assert.NotNull(session);

        // ⭐ 97a's ScalarEditBox<int> wrapper is what makes a scalar's root writable at all; this
        //   drives it through the SAME binding the drawer uses.
        session!.Document.Root.Children[0].Binding!.SetBoxed(42);

        Assert.Equal(VariableEditCommit.Outcome.Ok, s.Registrar.EditGestures!.Accept());
        Assert.Equal("42", DeclOf(s.Asset).DefaultValueJson);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>AND THE DOCUMENT IS MARKED DIRTY</b> — the second half of <c>98a</c>, and on its own it
    /// is the difference between an edit the designer can save and one that dies on close.
    ///
    /// <para>🔴🔴 <b>The silent default, in its textbook form.</b>
    /// <c>BlueprintMyBlueprintWindow.ResolveVariableSelection</c> built its schema source with
    /// <c>onChanged: () =&gt; { }</c> while the SAME window computed a real <c>markDirty</c> from the
    /// SAME editable asset ~260 lines above. 📌 <c>CLAUDE.md</c>: <i>"a production caller that HAS a
    /// dependency must PASS it."</i> ⚠ It was harmless while the source was read-only — which is
    /// exactly why it survived.</para>
    /// </summary>
    [Fact]
    public void APlanningEdit_MarksTheDocumentDirty()
    {
        var s = Scene();
        Assert.False(s.File.IsDirty, "the harness must start clean or this rail proves nothing");

        s.Registrar.EditGestures!.OnEditValue(s.Row);
        Assert.Equal(VariableEditCommit.Outcome.Ok, s.Registrar.EditGestures!.Accept());

        Assert.True(s.File.IsDirty,
            "the edit landed in memory and the document was never marked dirty — it dies on close.");
    }

    // ══ the premise this rests on, pinned ════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The two vocabularies are still SEPARATE, and that is the design.</b>
    ///
    /// <para>📌 This batch's handoff §4 forbids <i>"widening <c>IBlackboardManagedAsset</c> to cover
    /// <c>BlueprintAsset</c>"</i>, and <c>95a</c>/<c>R-108</c> both keep them apart. ⇒ ⭐ this rail
    /// asserts the premise rather than the fix: if a later batch widens the interface, <b>this goes
    /// red</b> and whoever did it must justify it — ⛔ instead of the row arm silently becoming
    /// redundant.</para>
    /// </summary>
    [Fact]
    public void ABlueprintAssetIsStillNotABlackboardManagedAsset()
        => Assert.False(Scene().Asset is IBlackboardManagedAsset);

    /// <summary>
    /// ⭐⭐ <b>The write-back rides on the ROW, not on the composition root</b> — and this says why.
    ///
    /// <para>📐 Measured: Blueprint's schema sources are constructed inside
    /// <c>BlueprintMyBlueprintWindow</c>, <b>per outline selection</b> and long after
    /// <c>CreateRegistrar</c> returned. ⇒ a seam supplied at the composition root could answer for the
    /// asset-scoped sections and <b>not</b> for Local Variables, which follows the canvas by delegate.
    /// ⭐ Same measurement <c>95a</c> made for the READ.</para>
    /// </summary>
    [Fact]
    public void EveryRowFromTheOutline_CarriesItsOwnWriteBack()
    {
        var s = Scene();
        Assert.NotNull(s.Row.WriteDefault);
    }

    // ══ the honest refusals ══════════════════════════════════════════════════

    /// <summary>
    /// ⛔ <b>A row with no write-back and no asset still refuses by NAME.</b> ⭐ The fallback arm is
    /// not dead: a hand-constructed row lands there, and it must keep saying
    /// <c>RefusedNoDeclarationOwner</c> rather than the row-kind lie <c>BP-355</c> removed.
    /// </summary>
    [Fact]
    public void ARowWithNeitherArm_StillNamesTheMissingOwner()
    {
        using var session = new StructEdit.Reflection.ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), StructEdit.Core.EditScope.WholeComponent);

        var bare = new VariableRow(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "vars", "Count", "A"),
            ShortName: "Count",
            TypeText:  "int",
            ClrType:   typeof(int),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   VariableRowKind.Normal);

        Assert.Equal(
            VariableEditCommit.Outcome.RefusedNoDeclarationOwner,
            VariableEditCommit.Commit(session, asset: null, bare, typeof(int),
                                      VariableRunState.Planning));
    }

    /// <summary>
    /// ⛔ <b>A write-back that REFUSES is reported, not swallowed.</b> 📌 <c>BP1664</c>: a macro
    /// graph's locals belong to the host after splicing, so
    /// <c>BlueprintLocalVariableSchemaSource.IsReadOnly</c> is <c>true</c> there and the row's writer
    /// answers <c>false</c>. ⭐ Answering <c>true</c> would report a write the source then discards.
    /// </summary>
    [Fact]
    public void AWriteBackThatRefuses_IsReported()
    {
        using var session = new StructEdit.Reflection.ComponentEditServiceBuilder().Build()
            .Open(1, typeof(int), StructEdit.Core.EditScope.WholeComponent);

        var refusing = new VariableRow(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "vars", "Count", "A"),
            ShortName: "Count",
            TypeText:  "int",
            ClrType:   typeof(int),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   VariableRowKind.Normal,
            WriteDefault: _ => false);

        Assert.Equal(
            VariableEditCommit.Outcome.RefusedNoDeclarationOwner,
            VariableEditCommit.Commit(session, asset: null, refusing, typeof(int),
                                      VariableRunState.Planning));
    }

    // ── the harness ─────────────────────────────────────────────────────────

    private sealed record Rig(
        PerspectiveWorkspaceRegistrar Registrar, BlueprintAsset Asset,
        BlueprintFileAsset File, VariableRow Row);

    /// <summary>
    /// ⭐ The REAL composition root, the REAL outline window and a REAL blueprint on disk.
    /// ⚠ Only the draw layer is absent — see the class remarks.
    /// </summary>
    private Rig Scene()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "Count4",
            Dispatch = BlueprintDispatchKind.AiPrimitive,
            Graphs   = new List<Graph>(),
            Header   = new Header(),
        };
        BlueprintDocumentFactory.CreateVariable(asset, "Count", "System.Int32");
        DeclOf(asset).DefaultValueJson = "1";

        var path = Path.Combine(_tempDir, $"{asset.Name}.bp.json");
        System.IO.File.WriteAllText(path, "{}");
        var file = new BlueprintFileAsset(asset.AssetId, asset.Name, path);

        var window = new BlueprintMyBlueprintWindow();
        window.Retarget(file, asset, null, new EditorCommandsImpl(), null, () => Guid.Empty);

        var selection = window.ResolveVariableSelection(
            new MyBlueprintItem(
                ItemId: $"var:{Guid.NewGuid():D}",
                SectionId: BlueprintMyBlueprintModel.SectionVariables,
                DisplayName: "Count",
                CategoryPath: null, IconKey: null, BadgeText: null, AccentColor: null,
                Children: null, IsRenamable: true, IsDeletable: true, IsHostDefined: false,
                Tooltip: null));

        var row = Assert.Single(selection.Source!.GetRows());

        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(_atlas));
        var registrar = editor.RegistrarFor("Blueprint")!;

        // ⭐ PLANNING is the default headless state: EditorSubsystem's isSimUp reads
        //   _previewController?.IsInPreviewMode ?? false. ⛔ Asserted, not assumed — the whole item is
        //   about which arm TargetFor picks.
        registrar.Variables.SyncRunState();
        Assert.Equal(VariableRunState.Planning, registrar.Variables.RunState);

        return new Rig(registrar, asset, file, row);
    }

    private static VariableDecl DeclOf(BlueprintAsset asset)
        => asset.Declarations.Of(DeclarationKind.Variable)
                .First(d => d.Name == "Count").AsVariableDecl!;
}
