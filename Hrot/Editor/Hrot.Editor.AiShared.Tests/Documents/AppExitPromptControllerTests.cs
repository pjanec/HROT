using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.UI.Common.RunMode;
using System.Linq;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// Headless tests for <see cref="AppExitPromptController"/> — the state machine behind the app-exit
/// "unsaved changes" prompt. The ImGui modal (EditorSubsystem) is a thin view over these seams.
/// </summary>
public sealed class AppExitPromptControllerTests
{
    private static AiDocumentManager MakeDocManager() => new(_ => { });

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name = "Doc", AssetKind kind = AssetKind.Blueprint)
        { Name = name; Kind = kind; AssetId = System.Guid.NewGuid(); }
        public System.Guid AssetId { get; }
        public string    Name           { get; }
        public AssetKind Kind           { get; }
        public string    SourceFilePath => "/fake.bp.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
#pragma warning disable CS0067
        public event System.Action? Changed;
#pragma warning restore CS0067
    }

    private static (AppExitPromptController ctrl, AiDocumentManager dm, System.Func<int> saves) Make()
    {
        var dm = MakeDocManager();
        int saveCalls = 0;
        var ctrl = new AppExitPromptController(dm, () => saveCalls++);
        return (ctrl, dm, () => saveCalls);
    }

    // ── RequestExit ─────────────────────────────────────────────────────────────

    [Fact]
    public void RequestExit_NoDirtyDocuments_ReturnsTrue_NoPrompt()
    {
        var (ctrl, dm, _) = Make();
        dm.Open(new FakeAsset()); // clean

        Assert.True(ctrl.RequestExit());   // safe to exit now
        Assert.False(ctrl.IsPrompting);
        Assert.False(ctrl.ExitApproved);
    }

    [Fact]
    public void RequestExit_DirtyDocument_ReturnsFalse_AndPrompts()
    {
        var (ctrl, dm, _) = Make();
        var doc = dm.Open(new FakeAsset());
        doc.MarkDirty();

        Assert.False(ctrl.RequestExit());  // deferred
        Assert.True(ctrl.IsPrompting);
        Assert.False(ctrl.ExitApproved);
    }

    [Fact]
    public void RequestExit_WhilePrompting_IsIdempotent()
    {
        var (ctrl, dm, _) = Make();
        dm.Open(new FakeAsset()).MarkDirty();

        Assert.False(ctrl.RequestExit());
        Assert.False(ctrl.RequestExit()); // second [X] press — still deferred, still prompting
        Assert.True(ctrl.IsPrompting);
    }

    [Fact]
    public void RequestExit_AfterApproved_ReturnsTrue()
    {
        var (ctrl, dm, _) = Make();
        dm.Open(new FakeAsset()).MarkDirty();
        ctrl.RequestExit();
        ctrl.ResolveDiscardAndExit();

        Assert.True(ctrl.RequestExit());   // already approved → exit
    }

    // ── DirtyDocuments ──────────────────────────────────────────────────────────

    [Fact]
    public void DirtyDocuments_ListsOnlyDirtyOnes_InOpenOrder()
    {
        var (ctrl, dm, _) = Make();
        var a = dm.Open(new FakeAsset("A"));
        var b = dm.Open(new FakeAsset("B"));
        var c = dm.Open(new FakeAsset("C"));
        a.MarkDirty();
        c.MarkDirty();

        var dirty = ctrl.DirtyDocuments;
        Assert.Equal(2, dirty.Count);
        Assert.Same(a, dirty[0]);
        Assert.Same(c, dirty[1]);
        Assert.DoesNotContain(b, dirty);
    }

    // ── ⭐ CE-1438 (R-3066 audit #5) — the prompt IS a RunModeConfirmation ──────────────────────

    /// <summary>
    /// The exit prompt is the ONE confirmation shape — a <see cref="RunModeConfirmation"/> listing the dirty documents, buttons <i>Save All &amp; Exit / Discard &amp; Exit / Cancel</i> (Cancel last: Esc / ✕ press it).
    /// Pressing a BUTTON of the model (what the one modal helper does) behaves exactly as the <c>Resolve*</c> methods: Save saves once and approves, Discard approves, Cancel keeps the editor open; each clears the prompt.
    /// 🔴 Red-proof: the Save button's action wired to the Discard method ⇒ <c>saves()</c> stays 0; the Cancel button approving exit ⇒ <c>ExitApproved</c>.
    /// </summary>
    [Fact]
    public void CE1438_ThePrompt_IsARunModeConfirmation_AndItsButtonsDoWhatTheResolveMethodsDo()
    {
        var (ctrl, dm, saves) = Make();
        dm.Open(new FakeAsset("Alpha", AssetKind.Blueprint)).MarkDirty();
        Assert.Null(ctrl.Pending);
        Assert.False(ctrl.RequestExit());

        var model = Assert.IsType<RunModeConfirmation>(ctrl.Pending);
        Assert.Equal(new[] { "Save All & Exit", "Discard & Exit", "Cancel" }, model.Buttons.Select(b => b.Label));
        Assert.Contains("Alpha", model.Text);
        Assert.Contains("1 document with unsaved changes", model.Text);
        Assert.False(ctrl.RequestExit());
        Assert.Same(model, ctrl.Pending);                       // a second [X] keeps the one prompt

        model.Resolve(model.Buttons[0]);                         // Save All & Exit
        Assert.Equal(1, saves());
        Assert.True(ctrl.ExitApproved);
        Assert.Null(ctrl.Pending);
        Assert.False(ctrl.IsPrompting);

        var (discard, dm2, saves2) = Make();
        dm2.Open(new FakeAsset()).MarkDirty();
        discard.RequestExit();
        discard.Pending!.Resolve(discard.Pending.Buttons[1]);    // Discard & Exit
        Assert.Equal(0, saves2());
        Assert.True(discard.ExitApproved);

        var (cancel, dm3, saves3) = Make();
        dm3.Open(new FakeAsset()).MarkDirty();
        cancel.RequestExit();
        cancel.Pending!.Cancel();                                // Esc / ✕ / Cancel
        Assert.Equal(0, saves3());
        Assert.False(cancel.ExitApproved);
        Assert.False(cancel.IsPrompting);
    }

    // ── Resolve* ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveSaveAndExit_InvokesSaveAll_ApprovesExit_ClosesPrompt()
    {
        var (ctrl, dm, saves) = Make();
        dm.Open(new FakeAsset()).MarkDirty();
        ctrl.RequestExit();

        ctrl.ResolveSaveAndExit();

        Assert.Equal(1, saves());          // Save-All invoked exactly once
        Assert.True(ctrl.ExitApproved);
        Assert.False(ctrl.IsPrompting);
    }

    [Fact]
    public void ResolveDiscardAndExit_ApprovesExit_WithoutSaving()
    {
        var (ctrl, dm, saves) = Make();
        dm.Open(new FakeAsset()).MarkDirty();
        ctrl.RequestExit();

        ctrl.ResolveDiscardAndExit();

        Assert.Equal(0, saves());          // never saved
        Assert.True(ctrl.ExitApproved);
        Assert.False(ctrl.IsPrompting);
    }

    [Fact]
    public void ResolveCancel_KeepsOpen_NothingSaved_NotApproved()
    {
        var (ctrl, dm, saves) = Make();
        dm.Open(new FakeAsset()).MarkDirty();
        ctrl.RequestExit();

        ctrl.ResolveCancel();

        Assert.Equal(0, saves());
        Assert.False(ctrl.ExitApproved);   // stays running
        Assert.False(ctrl.IsPrompting);
    }
}
