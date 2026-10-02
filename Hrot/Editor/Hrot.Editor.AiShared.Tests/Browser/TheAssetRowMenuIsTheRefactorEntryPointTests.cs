using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NodeEditor.Core.Interfaces;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Browser;

/// <summary>
/// ⭐⭐⭐ <b>THE ASSET ROW'S RIGHT-CLICK MENU — refactor gestures live where you point at an asset.</b>
/// 🔒 <b>User ruling, <c>2026-08-22</c>:</b> <i>"go to definition and rename and find references, these
/// all sound like context menu items of a blueprint graph node and not anything to put to a details
/// panel view."</i> … <i>"asset related context menu items then, still nothing for a details panel
/// view."</i> … <i>"picker should not have that menu."</i>
///
/// <para>📄 <b>And the design says the same thing</b> — <c>AI_Editor_Shared_Infrastructure.md</c> §16.1:
/// <i>"Find References (read-only) … Used by <b>the right-click menu</b>, the Find Results window, and
/// indirectly by the rename preview."</i> ⇒ ⭐ this is §16.1's own home for operations <b>1</b> and
/// <b>4</b>, not a new idea.</para>
///
/// <para>⚠ <b>What these rails do NOT prove</b> *(📌 <c>R-21</c>/<c>R-62</c>)*: that the menu appears on
/// screen. ⭐ They prove the MODEL — which host offers commands, that the panel invokes them, and that
/// a rename produces a PREVIEW rather than applying. ⛔ The pixels stay with the visual check.</para>
/// </summary>
public sealed class TheAssetRowMenuIsTheRefactorEntryPointTests
{
    // ══ the opt-in — the user's "picker should not have that menu" ═══════════

    /// <summary>
    /// ⭐⭐⭐ <b>Commands are OPT-IN, and the default is NONE.</b>
    /// ⚠ That default is what makes the picker correct <b>by omission</b> rather than by someone
    /// remembering to opt out — 📌 the same shape as every silent default this programme has filed,
    /// pointed the safe way round.
    /// </summary>
    [Fact]
    public void AHostThatAsksForNothing_GetsNoRowCommands()
        => Assert.Empty(new AssetBrowserPanelOptions().RowCommands);

    /// <summary>
    /// ⛔⛔ <b>THE PICKER MUST NOT OFFER THEM</b>, and this asks the CONSTRUCTED modal rather than the
    /// source that builds it — 📌 <c>R-67</c>.
    ///
    /// <para>⚠ <b>Why this rail is not vacuous:</b> the picker and the docked browser share ONE
    /// <c>AssetBrowserPanel</c>. ⇒ a menu added to the panel without the opt-in would appear in BOTH,
    /// and *"Rename…"* mid-pick is a different job wearing the same widget. 📐 This is the difference
    /// the user's ruling turns on.</para>
    /// </summary>
    [Fact]
    public void ThePickerModal_OffersNoRowCommands()
    {
        var modal = new AssetPickerModal(new AssetCatalog(), new NoIcons());

        // ⭐ Opened the way a picker is opened — with the options a PICKER supplies.
        modal.Open(new AssetBrowserPanelOptions { Kinds = AssetKindFilter.All },
                   callback: _ => { });

        Assert.NotNull(modal.Panel);
        Assert.Empty(modal.Panel!.Options.RowCommands);
    }

    // ══ the panel invokes what it is given ═══════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>A command reaches its asset.</b> ⛔ Invoked through the record the panel holds, not through
    /// ImGui — 📌 <c>R-21</c>: the click is a draw; the CONSEQUENCE is this.
    /// </summary>
    [Fact]
    public void ACommandIsInvokedWithTheRowsAsset()
    {
        IEditableAsset? seen = null;
        var command = new AssetRowCommand("Find References", a => seen = a);
        var asset   = new FakeAsset("Patrol");

        command.Invoke(asset);

        Assert.Same(asset, seen);
    }

    /// <summary>
    /// ⭐⭐ <b>A disabled command carries a REASON.</b> 📌 The user's <c>2026-08-17</c> ruling: <i>"showing
    /// explanatory tooltip would be better than allowing user to click the button and then saying that
    /// it is not possible."</i> ⛔ The record makes a reasonless refusal expressible, so this pins that
    /// the SHAPE supports it — ⚠ and the two shipped commands are unconditional, which this says too.
    /// </summary>
    [Fact]
    public void ADisabledCommandCanExplainItself()
    {
        var asset = new FakeAsset("Patrol");
        var command = new AssetRowCommand(
            "Delete", _ => { },
            IsEnabled:      _ => false,
            DisabledReason: a => $"'{a.Name}' is referenced by 3 assets.");

        Assert.False(command.IsEnabled!(asset));
        Assert.Contains("Patrol", command.DisabledReason!(asset), StringComparison.Ordinal);
    }

    // ══ rename PREVIEWS, it never applies ════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>§16.2's split, asserted: OK produces a PREVIEW.</b>
    /// 📄 <i>"The preview lists every file that would be modified … The user reviews and either applies
    /// or cancels."</i> ⇒ ⛔ this dialog must NEVER call <c>ApplyRename</c>; the Find Results window owns
    /// that gesture. ⚠ The retired Inspector header behaved the same way, and this pins that it still does.
    /// </summary>
    [Fact]
    public void RenameProducesAPreview_AndNeverApplies()
    {
        var refactor  = new RecordingRefactor();
        RefactorPreview? shown = null;
        var modal = new AssetRenameModal(refactor, p => shown = p);

        Assert.True(modal.Open("Patrol"));
        Assert.True(modal.IsOpen);

        Assert.True(modal.Commit("PatrolV2"));

        Assert.Equal(("Patrol", "PatrolV2"), refactor.Previewed);
        Assert.NotNull(shown);
        Assert.False(modal.IsOpen);
        Assert.Equal(0, refactor.Applied);
    }

    /// <summary>⛔ Cancel previews nothing. ⚠ 📌 <c>B100c</c>: closing ENDS the session, so a later
    /// frame cannot re-read a half-open state.</summary>
    [Fact]
    public void CancellingPreviewsNothing()
    {
        var refactor = new RecordingRefactor();
        var modal    = new AssetRenameModal(refactor, _ => Assert.Fail("cancel must not preview"));

        modal.Open("Patrol");
        modal.Close();

        Assert.False(modal.IsOpen);
        Assert.Null(refactor.Previewed);
    }

    /// <summary>⛔ A blank new name is a no-op that still closes — the retired header's behaviour, kept.
    /// ⚠ It must not preview a rename to nothing.</summary>
    [Fact]
    public void ABlankNewName_PreviewsNothing_ButStillCloses()
    {
        var refactor = new RecordingRefactor();
        var modal    = new AssetRenameModal(refactor, _ => Assert.Fail("blank must not preview"));

        modal.Open("Patrol");

        Assert.False(modal.Commit("   "));
        Assert.False(modal.IsOpen);
        Assert.Null(refactor.Previewed);
    }

    /// <summary>⛔ The refactor service is REQUIRED — 📌 <c>M-15</c>: a rename that skips it dangles
    /// every reference, so there is no null arm to fall into.</summary>
    [Fact]
    public void TheRenameModalRefusesToExistWithoutARefactorService()
        => Assert.Throws<ArgumentNullException>(
            () => new AssetRenameModal(null!, _ => { }));

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class RecordingRefactor : IRefactorService
    {
        public (string From, string To)? Previewed { get; private set; }
        public int Applied { get; private set; }

        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o)
        {
            Previewed = (f, t);
            return new RefactorPreview(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        }

        public RefactorResult ApplyRename(RefactorPreview p)
        {
            Applied++;
            return new RefactorResult(true, Array.Empty<string>(), null);
        }

        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public Task<RefactorPreview> PreviewRenameAsync(string f, string t, RefactorOptions o, CancellationToken ct = default)
            => Task.FromResult(PreviewRename(f, t, o));
        public Task<RefactorResult> ApplyRenameAsync(RefactorPreview p, CancellationToken ct = default)
            => Task.FromResult(ApplyRename(p));
    }

    private sealed class NoIcons : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(1, 16, 16);
            return true;
        }
    }

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name) => Name = name;
        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           { get; }
        public AssetKind Kind           => AssetKind.Blueprint;
        public string    SourceFilePath => "/fake.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }
}
