using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 95 (<c>95a</c>) — "Properties…" and "Edit value…" OPEN A DIALOG, on all three hosts.</b>
///
/// <para>🔴🔴 <b>The defect, measured before building.</b>
/// <c>PerspectiveWorkspaceRegistrar.ResolveEntry</c> began
/// <c>if (store.ActiveAsset is not IBlackboardManagedAsset asset) return null;</c>, and
/// 📐 <c>IBlackboardManagedAsset</c> is implemented by <c>HsmAsset</c> and <c>BehaviorTreeAsset</c> and
/// by <b>nothing else</b> — <c>BlueprintAsset</c> implements none of it. ⇒
/// <c>VariableEditGestureBinder.Open</c> hit its <c>if (entry is null) return;</c> <b>every time</b> on
/// the Blueprint perspective, so ⛔ <b>neither dialog could ever open there</b> — which is also why the
/// visual check's <c>C</c> and <c>D</c> could not be run at all.</para>
///
/// <para>⭐⭐⭐ <b>Why this rail is shaped the way it is.</b> 📌 <c>M-22</c>'s correction:
/// <i>"'is it connected?' is not 'does anything flow?'"</i> — Batch 84's rail asserts
/// <c>HasEditGestures</c>, which was TRUE on Blueprint throughout, because the binder WAS attached and
/// its resolver simply could not answer. ⇒ ⭐ <b>this one raises the real gesture on a real row and
/// asserts a SESSION OPENS</b>, which is the only claim the designer cares about.</para>
///
/// <para>⛔⛔ <b>Nothing here builds a registrar.</b> 📌 <c>R-67</c>: <i>"a rail that builds its own
/// composition root cannot see a composition-root defect."</i> The subsystem under test is the real
/// <see cref="EditorSubsystem"/>; the registrar, its edit service, its binder, its launcher, its
/// selection store and its run-state source are all the ones production built.</para>
///
/// <para>⚠ <b>What IS a fake, stated rather than hidden</b> *(handoff §6)*: the <c>WindowManager</c>'s
/// icon atlas is a zero handle (no GPU in a test host), and the ROWS are built here rather than by an
/// outline click — 🔴 <b>that is the one layer the defect could still hide in</b>, and it is the layer
/// <see cref="TheRowCarriesItsDeclarationTests"/> covers from the other side by driving the production
/// row sources themselves.</para>
/// </summary>
public sealed class TheDialogOpensOnEveryHostTests
{
    // ── the real composition root ────────────────────────────────────────────

    private static PerspectiveWorkspaceRegistrar RegistrarOf(string perspective)
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));

        var reg = editor.RegistrarFor(perspective);
        Assert.NotNull(reg);
        return reg!;
    }

    // ── the rows each host actually produces ─────────────────────────────────

    /// <summary>
    /// ⭐ A blueprint's <c>Variables</c> section, built exactly as <c>BlueprintMyBlueprintWindow</c>
    /// builds it: <see cref="SectionVariableRowSource"/> over a <see cref="BlueprintVariableSchemaSource"/>.
    /// </summary>
    private static (IVariableRowSource Source, object Asset) BlueprintRows()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "DialogHost",
            Dispatch = BlueprintDispatchKind.Instance,
            Header   = new Header(),
        };
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        });

        return (new SectionVariableRowSource(
                    assetId:   asset.AssetId,
                    assetName: asset.Name,
                    entity:    default,
                    section:   Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintModel.SectionVariables,
                    schema:    new BlueprintVariableSchemaSource(
                                   asset, VariableKind.Variable, onChanged: () => { })),
                asset);
    }

    /// <summary>
    /// ⭐ A BTree/HSM section, built exactly as the registrar's own default <c>_sectionSource</c> builds
    /// it: <see cref="BlackboardSectionRowSource"/> over the active <c>IBlackboardManagedAsset</c>.
    /// </summary>
    private static (IVariableRowSource Source, object Asset) AiRows(string perspective)
    {
        var entry = new BlackboardVariableEntry("Health", typeof(int), Comment: null);

        // ⚠⚠ THE ASSET HERE IS A STAND-IN, stated rather than hidden (handoff §6).
        //    ⛔ HsmAsset's constructor is internal (DTO-mapper only) and BehaviorTreeAsset's needs a
        //    whole Fbt blob — neither is what these two arms are testing.
        //    ⭐ What the old resolver type-tested is the INTERFACE, and the real HsmAsset,
        //    BehaviorTreeAsset and this stand-in satisfy it identically. ⇒ the layer the stand-in
        //    hides is "do the two real AI assets still implement IBlackboardManagedAsset" — which
        //    the golden corpus pins elsewhere, and which 95a does not touch.
        //    ⭐⭐ The ROW SOURCE is the real shared one, and the REGISTRAR is production's.
        IEditableAsset asset = new StandInManagedAsset(
            perspective == "btree" ? AssetKind.BTree : AssetKind.Hsm, entry);

        return (new BlackboardSectionRowSource(
                    asset:   () => (IBlackboardManagedAsset)asset,
                    assetId: asset.AssetId,
                    section: BlackboardMyBlueprintModel.SectionOf(entry)),
                asset);
    }

    /// <summary>⚠ The BTree/HSM stand-in — see <see cref="AiRows"/> for exactly what it hides.</summary>
    private sealed class StandInManagedAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        public StandInManagedAsset(AssetKind kind, params BlackboardVariableEntry[] vars)
        { Kind = kind; _vars = vars.ToList(); }

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "DialogHost";
        public AssetKind Kind { get; }
        public string SourceFilePath => "/dialog-host.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
    }

    private static (IVariableRowSource Source, object Asset) RowsFor(string perspective)
        => perspective == "blueprint" ? BlueprintRows() : AiRows(perspective);

    // ══ THE RAIL ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The one that matters: a designer picks "Properties…" and a dialog opens.</b>
    ///
    /// <para>🔴 <b>RED before this batch on <c>blueprint</c></b> — and green on the other two, which is
    /// stated rather than hidden: the AI hosts DID work, because their assets satisfy the type test the
    /// old resolver made. ⭐ The point of railing all three is that the fix must not cost them that.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void PropertiesOpensASession_OnEveryPerspective(string perspective)
    {
        var reg = RegistrarOf(perspective);
        var (source, asset) = RowsFor(perspective);

        // ⭐ Production's own state: the perspective's store points at the open document.
        reg.SelectionStore.ActiveAsset = asset as IEditableAsset;

        reg.Variables.ShowSection("s", source);
        var row = Assert.Single(source.GetRows());

        reg.Variables.Control.RaisePropertiesRequested(row);

        Assert.NotNull(reg.EditGestures);
        Assert.NotNull(reg.EditGestures!.ActiveSession);
        Assert.Equal(VariableEditAction.Properties, reg.EditGestures.LastAction);
    }

    /// <summary>
    /// ⭐⭐ <b>And the VALUE gesture too</b> — the two menu items are the two <c>EditScope</c>s, and a fix
    /// that reached only one of them would leave half the dialog dead on Blueprint.
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void EditValueOpensASession_OnEveryPerspective(string perspective)
    {
        var reg = RegistrarOf(perspective);
        var (source, asset) = RowsFor(perspective);

        reg.SelectionStore.ActiveAsset = asset as IEditableAsset;
        reg.Variables.ShowSection("s", source);
        var row = Assert.Single(source.GetRows());

        reg.Variables.Control.RaiseEditValueRequested(row);

        Assert.NotNull(reg.EditGestures!.ActiveSession);
        Assert.Equal(VariableEditAction.EditValue, reg.EditGestures.LastAction);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE anti-vacuity assertion for Blueprint, stated as the defect's own shape.</b>
    ///
    /// <para>📐 <c>BlueprintAsset</c> is not an <c>IBlackboardManagedAsset</c> — and the dialog opens
    /// anyway. ⛔ If someone "fixes" this later by making <c>BlueprintAsset</c> implement that interface
    /// (option (a), which the handoff flags as suspect because <c>BlackboardVariables</c> is the AI
    /// vocabulary and blueprint declarations are <c>VariableDecl</c> with a <c>Guid Id</c>), this
    /// assertion fails and the change has to be argued rather than slipped in.</para>
    /// </summary>
    [Fact]
    public void TheBlueprintAssetIsStillNotABlackboardManagedAsset()
    {
        var (_, asset) = BlueprintRows();

        Assert.IsType<BlueprintAsset>(asset);
        Assert.False(asset is IBlackboardManagedAsset,
            "BlueprintAsset must NOT be made to answer the AI blackboard vocabulary — 95a resolves " +
            "the declaration from the ROW instead, which is what lets the two models stay separate.");
    }

    /// <summary>
    /// ⭐⭐ <b>And a row whose variable is GONE still opens nothing.</b> ⛔ Fail-closed is the property
    /// the old resolver had for the right reason, and 95a must not trade it away: the fix widens WHICH
    /// rows can answer, never WHETHER an unanswerable row opens a dialog over a guess.
    /// </summary>
    [Fact]
    public void ARowThatCanNameNoDeclarationStillOpensNothing()
    {
        var reg = RegistrarOf("blueprint");

        // ⭐ A hand-built row: no declaration arm, and an active asset that cannot answer either.
        var orphan = new VariableRow(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "s", "Ghost", "DialogHost"),
            ShortName: "Ghost", TypeText: "int", ClrType: typeof(int),
            ReadValue: () => Array.Empty<byte>());

        reg.Variables.ShowSection("s", new FixedVariableRowSource(new[] { orphan }));
        reg.Variables.Control.RaisePropertiesRequested(orphan);

        Assert.Null(reg.EditGestures!.ActiveSession);
    }
}
