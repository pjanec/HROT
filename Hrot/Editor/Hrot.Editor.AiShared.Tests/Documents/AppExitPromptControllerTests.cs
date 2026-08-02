using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
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
