using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 96 (§3b) — OK actually WRITES, and a refusal names its real cause.</b>
///
/// <para>🔴🔴 <b>The seventh instance of <c>R-67</c>, two lines from the sixth.</b> 📐 Measured:
/// <c>VariableEditGestureBinder</c>'s <c>assetOf</c> parameter had <b>ZERO production call sites</b> —
/// only two tests ever passed one — so <c>_assetOf?.Invoke(row)</c> was <c>null</c> on every OK,
/// <c>CommitInitialValue</c> hit <c>if (asset is null)</c>, and ⛔ <b>the write the whole dialog
/// exists for had never landed in production, on any host.</b></para>
///
/// <para>⛔⛔ <b>And the refusal it produced was a LIE.</b> It returned <c>RefusedReadOnly</c, whose
/// message is <i>"This row cannot be written — it is node-owned, a passthrough, or stale"</i> — about
/// an ordinary variable that is none of those. 📌 The user reported exactly that. ⇒ ⭐ the missing
/// owner now has its own outcome and its own sentence.</para>
///
/// <para>⚠ <b>This answers the handoff's open question</b> — <i>"is the user's <c>Count</c> genuinely
/// <c>RowKind != Normal</c> or <c>IsStale</c>? If a hand-authored blueprint <c>int</c> classifies as
/// node-owned, the CLASSIFIER is the defect"</i>. 📐 <b>Neither.</b> The classifier is correct; the
/// cause was a third one the question did not offer, and it was a missing argument.</para>
///
/// <para>⛔ <b>Which object each input comes from</b> *(handoff §1)*: the registrar, its binder, its
/// launcher and its selection store are <b>the real <see cref="EditorSubsystem"/>'s</b>; the row comes
/// from the production <see cref="BlackboardSectionRowSource"/>; ⚠ the ASSET is
/// <see cref="TestManagedAsset"/>, which carries its own note on what that hides.</para>
/// </summary>
public sealed class TheEditActuallyLandsTests
{
    /// <summary>⭐ A DTO-typed variable. ⚠ NOT a scalar, and that is deliberate — see
    /// <see cref="AScalarVariablesEditGoesNowhere"/> for why a scalar cannot be driven at all.</summary>
    public struct Settings
    {
        public int   Count;
        public float Speed;
    }

    private static PerspectiveWorkspaceRegistrar RegistrarOf(string perspective)
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor.RegistrarFor(perspective)!;
    }

    /// <summary>⭐ An active AI document with one authored <c>int</c>, and the row the table draws for
    /// it — both through the production shapes.</summary>
    private static (PerspectiveWorkspaceRegistrar Reg, TestManagedAsset Asset, VariableRow Row)
        Scene(string perspective)
    {
        var entry = new BlackboardVariableEntry(
            "Health", typeof(Settings), Comment: null, DefaultValueJson: "{\u0022Count\u0022:1}");
        var asset = new TestManagedAsset(
            perspective == "btree" ? AssetKind.BTree : AssetKind.Hsm, entry);

        var reg = RegistrarOf(perspective);
        reg.SelectionStore.ActiveAsset = asset;

        var source = new BlackboardSectionRowSource(
            asset:   () => asset,
            assetId: asset.AssetId,
            section: BlackboardMyBlueprintModel.SectionOf(entry));

        reg.Variables.ShowSection("s", source);
        return (reg, asset, source.GetRows().Single());
    }

    // ══ THE RAIL ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The designer edits a value and clicks OK, and the declaration CHANGES.</b>
    ///
    /// <para>🔴 <b>RED before this batch on every host</b> — <c>RefusedReadOnly</c>, and the JSON
    /// untouched. 📌 <c>M-22</c>: this asserts a VALUE LANDED, ⛔ not that an argument was passed.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    public void OkWritesTheDeclaredDefault(string perspective)
    {
        var (reg, asset, row) = Scene(perspective);

        reg.Variables.Control.RaiseEditValueRequested(row);
        Assert.NotNull(reg.EditGestures!.ActiveSession);

        // ⭐ The designer types a new value — through the node's binding, which is exactly what
        //   ComponentEditDrawer's leaf editor writes to (`node.Binding?.SetBoxed(value)`).
        FieldNode(reg.EditGestures.ActiveSession!, "Count").Binding!.SetBoxed(99);

        var outcome = reg.EditGestures.Accept();

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.Contains("99", Assert.Single(asset.BlackboardVariables).DefaultValueJson!);
    }

    /// <summary>
    /// ⭐⭐ <b>A row from a DIFFERENT asset never touches the OPEN document.</b>
    ///
    /// <para>⚠ The Watch mixes rows from arbitrary assets — 📌 <c>VariableRow</c>'s own doc: <i>"in
    /// Watch there is no single one"</i>. ⛔ Resolving the owner as <i>"whatever is open"</i> would land
    /// a designer's edit in the wrong asset, silently, with an <c>Ok</c> saying it worked.</para>
    ///
    /// <para>⚠⚠ <b>CHANGED by Batch 98 (<c>98a</c>), and the change is a WIDENING of what is asserted —
    /// stated because rewriting another batch's rail needs justifying.</b> This used to assert
    /// <c>RefusedNoDeclarationOwner</c> and <i>"neither asset was written"</i>. 📐 The refusal was a
    /// LIMITATION, not the safety property: the owner could only ever be <c>store.ActiveAsset</c>, so a
    /// stray row had nowhere to go. ⭐ <c>98a</c> gave the row its OWN write-back, so a stray row can now
    /// resolve its own declaration — ⛔ and writing there is CORRECT, not a leak.</para>
    ///
    /// <para>⭐⭐ <b>The property is unchanged and is what this now asserts:</b> the OPEN document is
    /// untouched. ⭐ <see cref="APinnedRowWhoseAssetIsNoLongerOpen_Refuses"/> covers the hazard the old
    /// wording was really guarding, and covers it BETTER — it exercises the production shape, where the
    /// source's asset delegate follows the active document.</para>
    /// </summary>
    [Fact]
    public void ARowFromAnotherAssetDoesNotWriteIntoTheOpenOne()
    {
        var (reg, openAsset, _) = Scene("btree");

        var otherEntry = new BlackboardVariableEntry(
            "Health", typeof(Settings), Comment: null, DefaultValueJson: "{\u0022Count\u0022:1}");
        var otherAsset = new TestManagedAsset(AssetKind.BTree, otherEntry);
        var strayRow = new BlackboardSectionRowSource(
                asset:   () => otherAsset,
                assetId: otherAsset.AssetId,
                section: BlackboardMyBlueprintModel.SectionOf(otherEntry))
            .GetRows().Single();

        reg.Variables.Control.RaiseEditValueRequested(strayRow);
        FieldNode(reg.EditGestures!.ActiveSession!, "Count").Binding!.SetBoxed(99);

        Assert.Equal(VariableEditCommit.Outcome.Ok, reg.EditGestures.Accept());

        // ⭐⭐ THE property: the open document is untouched.
        Assert.DoesNotContain("99", Assert.Single(openAsset.BlackboardVariables).DefaultValueJson!);
        // ⭐ And the edit landed where it belongs — the row's OWN asset.
        Assert.Contains("99", Assert.Single(otherAsset.BlackboardVariables).DefaultValueJson!);
    }

    /// <summary>
    /// ⛔⛔ <b>THE PRODUCTION HAZARD, and it is the one the old wording was really guarding.</b>
    ///
    /// <para>📐 <c>BlackboardSectionRowSource</c> resolves its asset <b>PER CALL</b> — right for
    /// BUILDING rows *(the active document changes under it)* and ⛔ wrong for writing one back. ⇒ a row
    /// pinned from asset <c>A</c>, edited after the designer switched to document <c>B</c>, would write
    /// <c>A</c>'s variable name INTO <c>B</c>. ⚠ Silently, and with an <c>Ok</c>.</para>
    ///
    /// <para>⭐ The guard is the SAME one Batch 96 put in <c>DeclarationOwnerOf</c>: the resolved asset
    /// must still be the row's own. 📐 Removing it turns this red.</para>
    /// </summary>
    [Fact]
    public void APinnedRowWhoseAssetIsNoLongerOpen_Refuses()
    {
        var (reg, openAsset, _) = Scene("btree");

        var pinnedEntry = new BlackboardVariableEntry(
            "Health", typeof(Settings), Comment: null, DefaultValueJson: "{\u0022Count\u0022:1}");
        var pinnedAsset = new TestManagedAsset(AssetKind.BTree, pinnedEntry);

        // ⭐ The production shape: the source follows the ACTIVE document, and the designer has since
        //   switched away from the asset this row was pinned from.
        var active = pinnedAsset;
        var row = new BlackboardSectionRowSource(
                asset:   () => active,
                assetId: pinnedAsset.AssetId,
                section: BlackboardMyBlueprintModel.SectionOf(pinnedEntry))
            .GetRows().Single();

        var switchedTo = new TestManagedAsset(AssetKind.BTree, pinnedEntry);
        active = switchedTo;   // ⚠ the switch, after the row was built

        reg.Variables.Control.RaiseEditValueRequested(row);
        FieldNode(reg.EditGestures!.ActiveSession!, "Count").Binding!.SetBoxed(99);

        Assert.Equal(VariableEditCommit.Outcome.RefusedNoDeclarationOwner,
                     reg.EditGestures.Accept());
        Assert.DoesNotContain("99", Assert.Single(switchedTo.BlackboardVariables).DefaultValueJson!);
        Assert.DoesNotContain("99", Assert.Single(pinnedAsset.BlackboardVariables).DefaultValueJson!);
        Assert.DoesNotContain("99", Assert.Single(openAsset.BlackboardVariables).DefaultValueJson!);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A missing owner no longer blames the ROW.</b> 📌 The user's rule: <i>"same information
    /// value, no false expectations"</i> — ⛔ a refusal that misnames its own cause sends the designer
    /// to fix the wrong thing, which is worse than a silent one.
    /// </summary>
    [Fact]
    public void TheMissingOwnerRefusalDoesNotClaimTheRowIsNodeOwned()
    {
        var modal = new VariableEditModal(
            new VariableEditGestureBinder(
                new VariableEditLauncher(new StructEdit.Reflection.ComponentEditServiceBuilder().Build()),
                entryResolver: _ => null,
                runState:      () => VariableRunState.Planning),
            () => VariableRunState.Planning);

        // ⭐ The two messages must not be the same sentence — and the owner one must not name the row.
        var owner    = MessageFor(modal, VariableEditCommit.Outcome.RefusedNoDeclarationOwner);
        var readOnly = MessageFor(modal, VariableEditCommit.Outcome.RefusedReadOnly);

        Assert.NotNull(owner);
        Assert.NotEqual(readOnly, owner);
        Assert.DoesNotContain("node-owned", owner!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stale",      owner!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>FLIPPED, Batch 97 (<c>97a</c>) — A SCALAR VARIABLE'S EDIT NOW LANDS.</b>
    ///
    /// <para>🔴🔴 <b>What this rail asserted before</b> *(Batch 96, on purpose)*: a scalar's document
    /// root had <c>Binding == null</c> — 📐 <c>ReflectionEditDocumentBuilder.CreateLeafBinding</c>
    /// opens <c>if (fi == null &amp;&amp; pi == null) return null;</c>, a binding needs a MEMBER, and a
    /// ROOT has none — so <c>DrawLeafNode</c>'s <c>node.Binding?.SetBoxed(value)</c> silently discarded
    /// the typing and <c>Commit()</c> could only return the seed. ⚠ <b>That was the user's exact
    /// case</b> — <c>Count</c>, a plain <c>int</c>.</para>
    ///
    /// <para>⭐⭐ <b>Now:</b> <c>DefaultValueAuthoring.OpenSession</c> opens a leaf-kind variable over
    /// <c>ScalarEditBox&lt;T&gt;</c>, whose public FIELD gives the root a BOUND CHILD. ⛔ <c>StructEdit</c>
    /// is untouched. ⭐ The assertion is now <b>the designer's sentence</b>: open, type, OK, and the
    /// declaration changes.</para>
    ///
    /// <para>⛔ <b>Whose object:</b> the registrar, binder, launcher and selection store are the real
    /// <see cref="EditorSubsystem"/>'s; the row comes from the production
    /// <see cref="BlackboardSectionRowSource"/>; ⚠ the asset is <see cref="TestManagedAsset"/>.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    public void AScalarVariablesEditLands(string perspective)
    {
        var entry = new BlackboardVariableEntry("Health", typeof(int), Comment: null, DefaultValueJson: "1");
        var asset = new TestManagedAsset(
            perspective == "btree" ? AssetKind.BTree : AssetKind.Hsm, entry);

        var reg = RegistrarOf(perspective);
        reg.SelectionStore.ActiveAsset = asset;

        var source = new BlackboardSectionRowSource(
            asset:   () => asset,
            assetId: asset.AssetId,
            section: BlackboardMyBlueprintModel.SectionOf(entry));
        reg.Variables.ShowSection("s", source);

        reg.Variables.Control.RaiseEditValueRequested(source.GetRows().Single());
        Assert.NotNull(reg.EditGestures!.ActiveSession);

        // ⭐ The designer types — through the node's binding, which is what DrawLeafNode writes to.
        //   🔴 Before 97a this node did not exist: the root WAS the leaf and carried no binding.
        FieldNode(reg.EditGestures.ActiveSession!, nameof(Hrot.Editor.AiShared.Inspector.ScalarEditBox<int>.Value))
            .Binding!.SetBoxed(99);

        Assert.Equal(VariableEditCommit.Outcome.Ok, reg.EditGestures.Accept());

        // ⭐⭐⭐ THE SCALAR, not the wrapper. ⛔ `{"Value":99}` here would mean the box escaped into the
        //    asset, and every later reader of that declaration would fail to hydrate it.
        Assert.Equal("99", Assert.Single(asset.BlackboardVariables).DefaultValueJson);
    }

    /// <summary>⭐ The one field node the DTO rails drive — the same node <c>DrawLeafNode</c> would
    /// draw an input for.</summary>
    private static StructEdit.Core.EditNode FieldNode(StructEdit.Core.IEditSession session, string name)
        => session.Document.Root.Children.Single(c => c.Name == name);

    /// <summary>⭐ Reads <c>RefusalMessage</c> for an outcome without an ImGui frame — the field the
    /// modal sets on a refused <c>Ok()</c> is private, so the rail sets it the same way <c>Ok()</c>
    /// would.</summary>
    private static string? MessageFor(VariableEditModal modal, VariableEditCommit.Outcome outcome)
    {
        typeof(VariableEditModal)
            .GetField("_refusal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(modal, outcome);
        return modal.RefusalMessage;
    }
}
