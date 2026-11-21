using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// BCP-BATCH-02-FIX behavioral tests.
/// <list type="bullet">
///   <item>Task 1: the canvas calls <see cref="IPickerRegistry.DrawFrame"/> once per frame
///     and pumps command hotkeys via <see cref="EditorHotkeyDispatcher"/>.</item>
///   <item>Task 2: the window title reflects the active asset name.</item>
/// </list>
/// All headless — no ImGui context.
/// </summary>
public sealed class BcpBatch02FixCanvasTests
{
    // ── Spies / fakes ──────────────────────────────────────────────────────────

    /// <summary>Spy picker registry that counts <see cref="DrawFrame"/> calls.</summary>
    private sealed class SpyPickerRegistry : IPickerRegistry
    {
        public int DrawFrameCount;
        public void Register<TItem>(string sourceKey, IPickerSource<TItem> source) { }
        public IPickerSource<TItem>? Get<TItem>(string sourceKey) => null;
        public void Open(string sourceKey, Vector2 screenPos, Action<object> onPick,
            Action? onCancel = null, IReadOnlyDictionary<string, object?>? context = null) { }
        public void DrawFrame() => DrawFrameCount++;
    }

    /// <summary>
    /// Input source that reports a single configurable key chord as "pressed this frame".
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

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name)
        {
            Kind = kind; Name = name; AssetId = Guid.NewGuid();
        }
        public Guid    AssetId        { get; }
        public string  Name           { get; }
        public AssetKind Kind         { get; }
        public string  SourceFilePath => "/fake";
        public bool    IsDirty        => false;
        public bool    IsEditorOwned  => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── GraphView stub plumbing (reused minimal stubs) ──────────────────────────

    private sealed class StubGraphModel : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "Stub";
        public GraphKindDescriptor Kind => new("stub", "Stub", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel?  FindNode(NodeId id) => null;
        public IPinModel?   FindPin(PinId id)   => null;
        public ILinkModel?  FindLink(LinkId id) => null;
#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067
    }

    private sealed class StubCommandSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId from, PinId to) =>
            new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
        { info = new TypeDisplayInfo("?", null, null); return false; }
        public Vector4 GetPinColor(TypeKey key) => Vector4.One;
        public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Circle;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
        public bool AreCompatible(TypeKey from, TypeKey to) => false;
        public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
    }

    private sealed class StubNodeCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHostServices : IEditorHostServices
    {
        private readonly StubCommandSink _cmd = new();
        private readonly StubValidator   _val = new();
        private readonly StubTypeSystem  _ts  = new();
        private readonly StubNodeCatalog _cat = new();
        public INodeCatalog     NodeCatalog  => _cat;
        public ITypeSystem      TypeSystem   => _ts;
        public ILinkValidator   LinkValidator => _val;
        public IGraphCommandSink CommandSink => _cmd;
        public IPickerRegistry  Pickers     => null!;
        public IClipboard       Clipboard   => null!;
        public IIconProvider    Icons       => null!;
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession?   Debug       => null;
        public IInputSource     Input       => null!;
        public IEditorTheme     Theme       => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    private static GraphView MakeGraphView()
    {
        var host = new StubHostServices();
        return new GraphView(new StubGraphModel(), host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);
    }

    private sealed class NoopSeam : ICanvasRenderSeam
    {
        public void Render(GraphView view) { }
    }

    private static (AiGraphCanvasWindow win, AiDocument doc, SpyPickerRegistry pickers)
        OpenWindow(string kind, IInputSource? input, string assetName = "MyAsset")
    {
        var assetKind = kind == "BTree" ? AssetKind.BTree
                      : kind == "HSM"  ? AssetKind.Hsm
                      : AssetKind.Blueprint;

        var pickers = new SpyPickerRegistry();
        var dm  = new AiDocumentManager(_ => { });
        var win = new AiGraphCanvasWindow(kind, dm, new NoopSeam(), pickers, input);

        var doc = dm.Open(new FakeAsset(assetKind, assetName));
        return (win, doc, pickers);
    }

    // ── Task 1: picker DrawFrame is pumped once per frame ───────────────────────

    [Fact]
    public void Canvas_PumpsPickerDrawFrame_OncePerFrame()
    {
        var (win, doc, pickers) = OpenWindow("BTree", input: null);
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "BTree");

        Assert.Equal(0, pickers.DrawFrameCount);

        win.SimulateDrawClientArea();
        win.SimulateDrawClientArea();
        win.SimulateDrawClientArea();

        Assert.Equal(3, pickers.DrawFrameCount);
    }

    [Fact]
    public void Canvas_DoesNotPumpPicker_WhenNoActiveDocument()
    {
        var pickers = new SpyPickerRegistry();
        var dm  = new AiDocumentManager(_ => { });
        var win = new AiGraphCanvasWindow("BTree", dm, new NoopSeam(), pickers, input: null);

        // No document open at all.
        win.SimulateDrawClientArea();

        Assert.Equal(0, pickers.DrawFrameCount);
    }

    // ── Task 1: command hotkeys — ⭐ B-10 K1: through the WindowManager's KeyRouter, only while the canvas is focused ────────
    // ⛔ SUPERSEDED: the per-canvas EditorHotkeyDispatcher (deleted) and its three direct rails; these are the same claims, on the router.

    private static void RouterWithFind(out EditorCommandsImpl commands, Action onFind)
    {
        commands = new EditorCommandsImpl();
        new CommandRegistration(commands).Add(
            NodeEditor.Core.CommandCatalog.FindInGraph, "Find in Graph", "Find",
            _ => onFind(),
            defaultKey: new KeyBinding(EditorKey.F, KeyModifiers.Ctrl));
    }

    [Fact]
    public void Router_RunsTheFocusedCanvasCommand_OnItsChord()
    {
        int invoked = 0;
        RouterWithFind(out var commands, () => invoked++);
        var (win, doc, _) = OpenWindow("Blueprint", new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl));
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Commands = commands };
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl));

        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(TextInput: false, PopupOpen: false, Focused: win));
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void Router_DoesNotRun_WhenModifiersDiffer()
    {
        int invoked = 0;
        RouterWithFind(out var commands, () => invoked++);
        var (win, doc, _) = OpenWindow("Blueprint", input: null);
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Commands = commands };
        // Ctrl+Shift+F pressed, but the command is bound to Ctrl+F only.
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl | KeyModifiers.Shift));

        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, win));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void Router_AFocusedCanvasWithNoDocument_IsNoOp()
    {
        var (win, _, _) = OpenWindow("Blueprint", input: null);   // a document, but no context ⇒ no commands
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl));
        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, win));   // must not throw
        Assert.Equal(0, router.FiredLastFrame);
    }

    /// <summary>
    /// ⭐⭐ B-10 K1 acceptance ② (CE-4282) — two canvases drawn, ONE focused: the chord reaches only the focused one. 🔴 Before: every drawn
    /// canvas pumped its own hotkeys, so one Ctrl+F / Delete / Esc / Ctrl+Z fired in each. ✅ Red-proof: the router running every drawn key
    /// scope instead of the focused one ⇒ the other canvas's count is 1.
    /// </summary>
    [Fact]
    public void Router_OnlyTheFocusedCanvasGetsItsKeys_NotEveryDrawnOne()
    {
        int a = 0, b = 0;
        RouterWithFind(out var commandsA, () => a++);
        RouterWithFind(out var commandsB, () => b++);
        var (winA, docA, _) = OpenWindow("Blueprint", input: null);
        docA.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Commands = commandsA };
        var (winB, docB, _) = OpenWindow("BTree", input: null);
        docB.ViewState = new AiCanvasContext(MakeGraphView(), "BTree") { Commands = commandsB };
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl));

        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, winB));
        Assert.Equal((0, 1), (a, b));
        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, winA));
        Assert.Equal((1, 1), (a, b));
        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, Focused: null));   // the map or a plain panel focused
        Assert.Equal((1, 1), (a, b));
    }

    /// <summary>⭐ B-10 K1 acceptance ① — a text field has the keyboard ⇒ no keymap action fires.</summary>
    [Fact]
    public void Router_RunsNothing_WhileTypingInATextField()
    {
        int invoked = 0;
        RouterWithFind(out var commands, () => invoked++);
        var (win, doc, _) = OpenWindow("Blueprint", input: null);
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Commands = commands };
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.F, KeyModifiers.Ctrl));

        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(TextInput: true, PopupOpen: false, Focused: win));
        Assert.Equal(0, invoked);
        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(TextInput: false, PopupOpen: true, Focused: win));   // a popup: the same
        Assert.Equal(0, invoked);
    }

    /// <summary>⭐ B-10 K1 acceptance ⑥ — the canvas commands' DefaultKeys ARE the keymap's rows (one declaration source).</summary>
    [Fact]
    public void Router_DeclaresTheFocusedCanvasCommandKeys_IntoTheKeymap()
    {
        RouterWithFind(out var commands, () => { });
        var (win, doc, _) = OpenWindow("Blueprint", input: null);
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Commands = commands };
        var router = new Fdp.Presentation.Input.KeyRouter(new FakeInputSource(EditorKey.Unknown, KeyModifiers.None));

        router.Dispatch(new Fdp.Presentation.Input.KeyFrame(false, false, win));
        Assert.True(router.Keymap.TryFind(Fdp.Toolkit.Input.InputScope.FocusedWindow, win.KeyScopeKind,
                                          new KeyBinding(EditorKey.F, KeyModifiers.Ctrl), out var row));
        Assert.Equal(NodeEditor.Core.CommandCatalog.FindInGraph, row.Action.CommandId);
    }

    // ── MULTI-TAB: window title is the container name, not the active asset ──────

    [Fact]
    public void Title_IsContainerName_NotActiveAsset()
    {
        var (win, doc, _) = OpenWindow("Blueprint", input: null, assetName: "PatrolBehavior");
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint");

        win.SimulateDrawClientArea();

        // Tabs carry per-blueprint names; the window title is the stable container name so its
        // close [x] doesn't read as "close this blueprint".
        Assert.Equal("Blueprint Canvas", win.Title);
        Assert.DoesNotContain("PatrolBehavior", win.Title);
    }

    [Fact]
    public void Title_KeepsStableId()
    {
        var (win, doc, _) = OpenWindow("Blueprint", input: null, assetName: "PatrolBehavior");
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint");

        win.SimulateDrawClientArea();

        // ManagedWindow forms its ImGui name as "{Title}###{Id}"; the stable Id preserves dock
        // identity, and the title stays the container name.
        Assert.Equal("ai_canvas_blueprint", win.Id);
        Assert.Equal("Blueprint Canvas", win.Title);
    }

    [Fact]
    public void Title_EmptyState_WhenNoDocument()
    {
        var pickers = new SpyPickerRegistry();
        var dm  = new AiDocumentManager(_ => { });
        var win = new AiGraphCanvasWindow("Blueprint", dm, new NoopSeam(), pickers, input: null);

        win.SimulateDrawClientArea();

        Assert.Equal("Blueprint Canvas", win.Title);
    }
}
