using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// MTB-P2-T4 — ShellSaveCommands behavioral tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless tests verifying <see cref="ShellSaveCommands"/> pure decision logic
/// and the perspective-level hotkey dispatch via <see cref="EditorHotkeyDispatcher"/>.
/// All headless — no ImGui context required.
/// </summary>
public sealed class SaveCommandsTests
{
    // ── Fakes ───────────────────────────────────────────────────────────────────

    /// <summary>Minimal <see cref="IEditableAsset"/> for headless tests.</summary>
    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name, string? sourceFilePath = null)
        {
            Kind           = kind;
            Name           = name;
            AssetId        = Guid.NewGuid();
            SourceFilePath = sourceFilePath ?? "";
        }

        public Guid       AssetId        { get; }
        public string     Name           { get; }
        public AssetKind  Kind           { get; }
        public string     SourceFilePath { get; }
        public bool       IsDirty        => false;
        public bool       IsEditorOwned  => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    /// <summary>
    /// Input source that reports a single configurable key chord as "pressed this frame".
    /// Mirrors the pattern in <c>BcpBatch02FixCanvasTests.FakeInputSource</c>.
    /// </summary>
    private sealed class FakeInputSource : IInputSource
    {
        private readonly EditorKey _pressedKey;
        public KeyModifiers Modifiers { get; }

        public FakeInputSource(EditorKey pressedKey, KeyModifiers mods)
        {
            _pressedKey = pressedKey;
            Modifiers   = mods;
        }

        public Vector2 MousePosition => Vector2.Zero;
        public Vector2 MouseDelta    => Vector2.Zero;
        public float   WheelDelta    => 0f;
        public bool IsMouseDown(MouseButton btn)          => false;
        public bool IsMousePressed(MouseButton btn)       => false;
        public bool IsMouseReleased(MouseButton btn)      => false;
        public bool IsMouseDoubleClicked(MouseButton btn) => false;
        public bool IsKeyDown(EditorKey k)                => false;
        public bool IsKeyPressed(EditorKey k, bool allowRepeat = false) => k == _pressedKey;
        public bool IsKeyReleased(EditorKey k)            => false;
        public ReadOnlySpan<char> TextThisFrame           => ReadOnlySpan<char>.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static AiDocumentManager MakeManager()
        => new AiDocumentManager(perspectiveSwitchCallback: _ => { });

    private static (List<EditorCommandDescriptor> descriptors,
                    List<Action<EditorCommandContext>> actions)
        MakeRecordingRegister()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var actions = new List<Action<EditorCommandContext>>();
        return (descriptors, actions);
    }

    private static Action<EditorCommandDescriptor, Action<EditorCommandContext>>
        RecordingRegister(List<EditorCommandDescriptor> descriptors,
                          List<Action<EditorCommandContext>> actions)
    {
        return (d, a) => { descriptors.Add(d); actions.Add(a); };
    }

    /// <summary>Find a descriptor by id from the recorded set.</summary>
    private static EditorCommandDescriptor FindDescriptor(
        List<EditorCommandDescriptor> descriptors, string id)
    {
        var d = descriptors.Find(x => x.Id == id);
        Assert.NotNull(d);
        return d!;
    }

    /// <summary>Invoke a recorded action by matching id.</summary>
    private static void InvokeAction(
        List<EditorCommandDescriptor> descriptors,
        List<Action<EditorCommandContext>> actions,
        string id)
    {
        int idx = descriptors.FindIndex(x => x.Id == id);
        Assert.True(idx >= 0, $"Command '{id}' not found in recorded descriptors");
        actions[idx](default);
    }

    // ── Test 1: Save_WithSourcePath_WritesActiveDocument ────────────────────────

    /// <summary>
    /// Active doc has non-empty <c>SourceFilePath</c> → the matching per-kind
    /// delegate is called with that path, <c>MarkClean()</c> is invoked, and
    /// <c>requestSaveAs</c> is NOT called.
    /// </summary>
    [Fact]
    public void Save_WithSourcePath_WritesActiveDocument()
    {
        var mgr = MakeManager();
        var asset = new FakeAsset(AssetKind.Blueprint, "MyBlueprint", "/fake/path.bp.json");
        var doc = mgr.Open(asset);
        doc.MarkDirty();

        var (descriptors, actions) = MakeRecordingRegister();
        bool blueprintSaved = false;
        string? savedPath = null;
        IEditableAsset? savedAsset = null;
        bool saveAsCalled = false;

        ShellSaveCommands.Register(
            register:      RecordingRegister(descriptors, actions),
            docManager:    mgr,
            saveBlueprint: (a, p) => { blueprintSaved = true; savedAsset = a; savedPath = p; },
            saveBTree:     null,
            saveHsm:       null,
            saveScenario:  null,
            requestSaveAs: _ => saveAsCalled = true,
            report:        null);

        // Simulate Save
        InvokeAction(descriptors, actions, ShellSaveCommands.SaveId);

        Assert.True(blueprintSaved, "Blueprint save delegate should have been called");
        Assert.Equal("/fake/path.bp.json", savedPath);
        Assert.Same(asset, savedAsset);
        Assert.False(doc.IsDirty, "Doc should be marked clean after save");
        Assert.False(saveAsCalled, "requestSaveAs must NOT be called when SourceFilePath is set");
    }

    // ── Test 2: Save_EmptySourcePath_RoutesToSaveAs ─────────────────────────────

    /// <summary>
    /// Active doc with empty <c>SourceFilePath</c> → <c>requestSaveAs</c> is called
    /// with that doc and NO per-kind write delegate runs.
    /// </summary>
    [Fact]
    public void Save_EmptySourcePath_RoutesToSaveAs()
    {
        var mgr = MakeManager();
        var asset = new FakeAsset(AssetKind.BTree, "UnsavedBTree", sourceFilePath: "");
        var doc = mgr.Open(asset);
        doc.MarkDirty();

        var (descriptors, actions) = MakeRecordingRegister();
        bool btreeSaved = false;
        AiDocument? saveAsDoc = null;
        bool saveAsCalled = false;

        ShellSaveCommands.Register(
            register:      RecordingRegister(descriptors, actions),
            docManager:    mgr,
            saveBlueprint: null,
            saveBTree:     (_, _) => btreeSaved = true,
            saveHsm:       null,
            saveScenario:  null,
            requestSaveAs: d => { saveAsCalled = true; saveAsDoc = d; },
            report:        null);

        InvokeAction(descriptors, actions, ShellSaveCommands.SaveId);

        Assert.True(saveAsCalled, "requestSaveAs should be called when SourceFilePath is empty");
        Assert.Same(doc, saveAsDoc);
        Assert.False(btreeSaved, "Per-kind save delegate must NOT be called");
        Assert.True(doc.IsDirty, "Doc should still be dirty (Save-As not yet performed)");
    }

    // ── Test 3: SaveAll_SavesEveryDirtyDocument ─────────────────────────────────

    /// <summary>
    /// Several docs, some dirty/some clean → every DIRTY doc's delegate runs
    /// (and clean ones don't); verified via recording delegates.
    /// </summary>
    [Fact]
    public void SaveAll_SavesEveryDirtyDocument()
    {
        var mgr = MakeManager();

        // Open 3 documents — mark 2 dirty, leave 1 clean.
        var bp1 = new FakeAsset(AssetKind.Blueprint, "BP1", "/a/bp1.bp.json");
        var bt1 = new FakeAsset(AssetKind.BTree,     "BT1", "/a/bt1.btree.json");
        var hsm1 = new FakeAsset(AssetKind.Hsm,       "HSM1", "/a/hsm1.hsm.json");

        var docBp = mgr.Open(bp1);
        docBp.MarkDirty();
        var docBt = mgr.Open(bt1);
        // docBt stays clean
        var docHsm = mgr.Open(hsm1);
        docHsm.MarkDirty();

        // Recording delegates track which assets were saved.
        var savedBlueprintPaths = new List<string>();
        var savedBTreePaths     = new List<string>();
        var savedHsmPaths       = new List<string>();

        var (descriptors, actions) = MakeRecordingRegister();
        var saveAsCalls = 0;

        ShellSaveCommands.Register(
            register:      RecordingRegister(descriptors, actions),
            docManager:    mgr,
            saveBlueprint: (a, p) => savedBlueprintPaths.Add(p),
            saveBTree:     (a, p) => savedBTreePaths.Add(p),
            saveHsm:       (a, p) => savedHsmPaths.Add(p),
            saveScenario:  null,
            requestSaveAs: _ => saveAsCalls++,
            report:        null);

        // Invoke Save All.
        InvokeAction(descriptors, actions, ShellSaveCommands.SaveAllId);

        // Blueprint and HSM were dirty → saved.
        Assert.Single(savedBlueprintPaths);
        Assert.Equal("/a/bp1.bp.json", savedBlueprintPaths[0]);
        Assert.Single(savedHsmPaths);
        Assert.Equal("/a/hsm1.hsm.json", savedHsmPaths[0]);

        // BTree was clean → NOT saved.
        Assert.Empty(savedBTreePaths);

        // Clean docs remain clean; dirty docs are now clean.
        Assert.False(docBp.IsDirty, "Saved blueprint doc should be clean");
        Assert.False(docBt.IsDirty, "Untouched clean BTree doc should still be clean");
        Assert.False(docHsm.IsDirty, "Saved HSM doc should be clean");

        // requestSaveAs never called during Save All.
        Assert.Equal(0, saveAsCalls);
    }

    // ── Test 4: Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow ──────────────

    /// <summary>
    /// Register the commands, feed them to <see cref="EditorHotkeyDispatcher.ProcessThisFrame"/>
    /// with a fake <see cref="IInputSource"/> reporting Ctrl+S pressed →
    /// the <c>shell.save</c> command is invoked (and Ctrl+Shift+S → <c>shell.saveAll</c>,
    /// not <c>shell.save</c>).
    /// Mirrors <c>BcpBatch02FixCanvasTests.HotkeyDispatcher_InvokesBoundCommand_OnMatchingChord</c>.
    /// <para>
    /// <b>Differentiation:</b> <c>shell.save</c> calls the per-kind delegate directly without
    /// reporting; <c>shell.saveAll</c> goes through <c>SaveAllAiDocumentsCommand.Execute</c>
    /// which calls both the per-kind delegate AND <c>report</c> with a success message.
    /// We use <c>reportCalled</c> to confirm SaveAll was entered.
    /// </para>
    /// </summary>
    [Fact]
    public void Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow()
    {
        var mgr = MakeManager();
        var asset = new FakeAsset(AssetKind.Blueprint, "HotkeyBP", "/fake/hotkey.bp.json");
        var doc = mgr.Open(asset);
        doc.MarkDirty(); // needed so SaveAllAiDocumentsCommand processes it

        int writeCalled = 0;
        int saveAsCalled = 0;
        int reportCalled = 0;

        // Register into a real EditorCommandsImpl so the hotkey dispatcher can find them.
        var commands = new EditorCommandsImpl();
        ShellSaveCommands.Register(
            register:      commands.Register,
            docManager:    mgr,
            saveBlueprint: (_, _) => writeCalled++,
            saveBTree:     null,
            saveHsm:       null,
            saveScenario:  null,
            requestSaveAs: _ => saveAsCalled++,
            report:        _ => reportCalled++);

        // -- Ctrl+S → should invoke shell.save ─────────────────────────────────
        var ctrlSInput = new FakeInputSource(EditorKey.S, KeyModifiers.Ctrl);
        var dispatcher = new EditorHotkeyDispatcher(ctrlSInput);
        dispatcher.ProcessThisFrame(commands);

        // shell.save → per-kind delegate called directly, no report.
        Assert.Equal(1, writeCalled);
        Assert.Equal(0, saveAsCalled);
        Assert.Equal(0, reportCalled);

        // -- Ctrl+Shift+S → should invoke shell.saveAll, not shell.save ─────────
        // Re-mark dirty (shell.save call above cleared it) so SaveAll processes it again.
        doc.MarkDirty();

        var ctrlShiftSInput = new FakeInputSource(EditorKey.S, KeyModifiers.Ctrl | KeyModifiers.Shift);
        dispatcher = new EditorHotkeyDispatcher(ctrlShiftSInput);
        dispatcher.ProcessThisFrame(commands);

        // shell.saveAll → SaveAllAiDocumentsCommand.Execute → calls both the per-kind
        // delegate AND report. The writeCalled goes from 1→2 (incremented inside SaveAll).
        Assert.Equal(2, writeCalled);
        Assert.Equal(0, saveAsCalled);
        // Ctrl+Shift+S must invoke SaveAll, which calls report for success.
        Assert.Equal(1, reportCalled);
    }

    // ── Edge case: Null active document does not throw ──────────────────────────

    /// <summary>
    /// When no document is open, <c>IsEnabled</c> returns <c>false</c> and invoking
    /// Save/SaveAs is a safe no-op (does not crash).
    /// </summary>
    [Fact]
    public void Save_NoActiveDocument_IsNoOp()
    {
        var mgr = MakeManager(); // No documents open.

        var (descriptors, actions) = MakeRecordingRegister();
        bool saveCalled = false;
        bool saveAsCalled = false;

        ShellSaveCommands.Register(
            register:      RecordingRegister(descriptors, actions),
            docManager:    mgr,
            saveBlueprint: (_, _) => saveCalled = true,
            saveBTree:     null,
            saveHsm:       null,
            saveScenario:  null,
            requestSaveAs: _ => saveAsCalled = true,
            report:        null);

        // Invoke Save — must not crash.
        InvokeAction(descriptors, actions, ShellSaveCommands.SaveId);
        Assert.False(saveCalled, "Save delegate should not be called with no active document");
        Assert.False(saveAsCalled, "requestSaveAs should not be called with no active document");

        // Invoke SaveAs — must not crash.
        InvokeAction(descriptors, actions, ShellSaveCommands.SaveAsId);
        Assert.False(saveAsCalled, "requestSaveAs should not be called with no active document");

        // Verify IsEnabled returns false for Save when no document.
        var saveDesc = FindDescriptor(descriptors, ShellSaveCommands.SaveId);
        Assert.False(saveDesc.IsEnabled(), "Save should be disabled when no document is open");

        // Verify IsEnabled returns false for SaveAll when nothing is dirty.
        var saveAllDesc = FindDescriptor(descriptors, ShellSaveCommands.SaveAllId);
        Assert.False(saveAllDesc.IsEnabled(), "SaveAll should be disabled when nothing is dirty");
    }
}
