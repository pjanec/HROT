using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Behavioral tests for <see cref="EditService"/> (AIE-049).
/// All tests are headless (no ImGui).
/// </summary>
public sealed class EditServiceTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static BlueprintAsset MakeAsset() =>
        new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestAsset" };

    private static (EditService svc, CommandHistory history, List<BlueprintAsset> dirtyLog)
        MakeSut()
    {
        var history  = new CommandHistory();
        var dirtyLog = new List<BlueprintAsset>();
        var ctx      = new EditServiceContext(history, a => dirtyLog.Add(a));
        var svc      = new EditService { Context = ctx };
        return (svc, history, dirtyLog);
    }

    // ── MarkDirty ─────────────────────────────────────────────────────────────

    [Fact]
    public void EditService_MarkDirty_FlagsAsset()
    {
        var (svc, _, dirtyLog) = MakeSut();
        var asset = MakeAsset();

        svc.MarkDirty(asset);

        Assert.Single(dirtyLog);
        Assert.Same(asset, dirtyLog[0]);
    }

    [Fact]
    public void EditService_MarkDirty_NoContext_NoThrow()
    {
        var svc   = new EditService();   // no Context
        var asset = MakeAsset();

        var ex = Record.Exception(() => svc.MarkDirty(asset));

        Assert.Null(ex);
    }

    [Fact]
    public void EditService_MarkDirty_NullAsset_Throws()
    {
        var (svc, _, _) = MakeSut();
        Assert.Throws<ArgumentNullException>(() => svc.MarkDirty(null!));
    }

    // ── RecordPropertyEdit — apply ────────────────────────────────────────────

    [Fact]
    public void EditService_PropertyEdit_PushesUndoableCommand()
    {
        var (svc, history, dirtyLog) = MakeSut();
        var asset = MakeAsset();
        var value = 0;

        svc.RecordPropertyEdit(asset, "Set value", () => value = 42, () => value = 0);

        // apply was called
        Assert.Equal(42, value);
        // command is in history (CanUndo)
        Assert.True(history.CanUndo);
        // asset was marked dirty
        Assert.Single(dirtyLog);
        Assert.Same(asset, dirtyLog[0]);
    }

    [Fact]
    public void EditService_PropertyEdit_NoContext_AppliesWithoutHistory()
    {
        var svc   = new EditService();   // no Context
        var asset = MakeAsset();
        var value = 0;

        svc.RecordPropertyEdit(asset, "Set value", () => value = 99, () => value = 0);

        // apply was still called
        Assert.Equal(99, value);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EditService_Undo_RevertsPropertyEdit()
    {
        var (svc, history, _) = MakeSut();
        var asset = MakeAsset();
        var value = 0;

        svc.RecordPropertyEdit(asset, "Set value", () => value = 42, () => value = 0);
        Assert.Equal(42, value);

        history.Undo();

        Assert.Equal(0, value);
    }

    [Fact]
    public void EditService_Undo_ThenRedo_ReappliesEdit()
    {
        var (svc, history, _) = MakeSut();
        var asset = MakeAsset();
        var value = 0;

        svc.RecordPropertyEdit(asset, "Set value", () => value = 7, () => value = 0);
        history.Undo();
        Assert.Equal(0, value);

        history.Redo();
        Assert.Equal(7, value);
    }

    [Fact]
    public void EditService_MultipleEdits_UndoRevertsInOrder()
    {
        var (svc, history, _) = MakeSut();
        var asset = MakeAsset();
        var value = 0;

        svc.RecordPropertyEdit(asset, "Set 1", () => value = 1, () => value = 0);
        svc.RecordPropertyEdit(asset, "Set 2", () => value = 2, () => value = 1);
        svc.RecordPropertyEdit(asset, "Set 3", () => value = 3, () => value = 2);
        Assert.Equal(3, value);

        history.Undo();
        Assert.Equal(2, value);

        history.Undo();
        Assert.Equal(1, value);

        history.Undo();
        Assert.Equal(0, value);
    }

    // ── Context swap ─────────────────────────────────────────────────────────

    [Fact]
    public void EditService_ContextSwap_NewCommandsGoToNewHistory()
    {
        var svc = new EditService();
        var asset = MakeAsset();

        var h1 = new CommandHistory();
        svc.Context = new EditServiceContext(h1, _ => { });
        svc.RecordPropertyEdit(asset, "A", () => { }, () => { });
        Assert.True(h1.CanUndo);

        var h2 = new CommandHistory();
        svc.Context = new EditServiceContext(h2, _ => { });
        svc.RecordPropertyEdit(asset, "B", () => { }, () => { });
        // h2 has the new command; h1 still has its own command
        Assert.True(h2.CanUndo);
        Assert.Equal(1, h1.Count);  // old history untouched
        Assert.Equal(1, h2.Count);
    }
}
