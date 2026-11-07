using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Input;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Input;
using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;
using ImGuiApi = ImGuiNET.ImGui;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.ImGui.Input;

/// <summary>
/// ⭐⭐ B-10 K1 (docs/UX/DESIGN_Input_And_Commands.md §5) — the ONE key router: the keymap's conflicts, the map's keyboard focus, and the
/// real frame — ImGui's focus, read once per window, decides which window's keys run (CE-4282: a focused panel takes keys from the map;
/// a graph canvas gets its keys only while focused).
/// </summary>
[Collection("ImGui Sequential")]
public sealed class KeyRouterTests : IDisposable
{
    public KeyRouterTests() => KeyboardFocus.MapHasKeyboard = true;
    public void Dispose() => KeyboardFocus.MapHasKeyboard = true;

    private static EditorCommandsImpl CommandsWith(EditorKey key, KeyModifiers mods, Action run, string id = "test.find")
    {
        var commands = new EditorCommandsImpl();
        commands.Register(new EditorCommandDescriptor(id, id, null, null, null, new KeyBinding(key, mods), () => true), _ => run());
        return commands;
    }

    private sealed class Keyed : IKeyScopeWindow
    {
        public Keyed(string kind, IEditorCommands? commands) { KeyScopeKind = kind; KeyCommands = commands; }
        public string KeyScopeKind { get; }
        public IEditorCommands? KeyCommands { get; }
    }

    private sealed class Pressing : IInputSource
    {
        public EditorKey Key;
        public KeyModifiers Modifiers { get; set; }
        public Vector2 MousePosition => Vector2.Zero;
        public Vector2 MouseDelta => Vector2.Zero;
        public float WheelDelta => 0f;
        public bool IsMouseDown(MouseButton btn) => false;
        public bool IsMousePressed(MouseButton btn) => false;
        public bool IsMouseReleased(MouseButton btn) => false;
        public bool IsMouseDoubleClicked(MouseButton btn) => false;
        public bool IsKeyDown(EditorKey k) => k == Key;
        public bool IsKeyPressed(EditorKey k, bool allowRepeat = false) => k == Key;
        public bool IsKeyReleased(EditorKey k) => false;
        public ReadOnlySpan<char> TextThisFrame => ReadOnlySpan<char>.Empty;
    }

    /// <summary>
    /// ⭐ Acceptance ⑤ — the keymap refuses (reports) one chord bound to two actions where both apply; the same chord in an INNER scope is
    /// shadowing, not a conflict. ✅ Red-proof: Validate ignoring the scope ⇒ the shadowing pair is reported too.
    /// </summary>
    [Fact]
    public void Validate_ReportsAChordBoundTwiceInOneScope_ButNotAnInnerScopeShadowingAnOuterOne()
    {
        var map = new Keymap();
        var ctrlS = new KeyBinding(EditorKey.S, KeyModifiers.Ctrl);
        map.Declare(new KeymapEntry(ctrlS, InputScope.Global, null, ActionRef.Command("shell.save")));
        map.Declare(new KeymapEntry(ctrlS, InputScope.FocusedWindow, "canvas", ActionRef.Command("graph.save")));   // shadowing
        Assert.Empty(map.Validate());

        map.Declare(new KeymapEntry(ctrlS, InputScope.Global, null, ActionRef.Global(42)));                            // a real clash
        var conflict = Assert.Single(map.Validate());
        Assert.Equal(ActionRef.Command("shell.save"), conflict.First.Action);
        Assert.Equal(ActionRef.Global(42), conflict.Second.Action);

        Assert.False(map.Declare(new KeymapEntry(ctrlS, InputScope.Global, null, ActionRef.Command("shell.save"))));   // re-declaring is a no-op
    }

    /// <summary>
    /// ⭐ The scope chain: a focused window's binding shadows the Global one; with nothing of its own on the chord, Global runs (Ctrl+S from
    /// anywhere). ✅ Red-proof: running Global before the focused window ⇒ <c>global</c> is 1 on the first press.
    /// </summary>
    [Fact]
    public void TheFocusedWindowShadowsGlobal_AndGlobalRunsWhenTheWindowHasNothing()
    {
        int own = 0, global = 0, other = 0;
        var input = new Pressing { Key = EditorKey.Z, Modifiers = KeyModifiers.Ctrl };
        var router = new KeyRouter(input) { GlobalCommands = CommandsWith(EditorKey.Z, KeyModifiers.Ctrl, () => global++, "shell.undo") };
        var canvas = new Keyed("canvas", CommandsWith(EditorKey.Z, KeyModifiers.Ctrl, () => own++, "graph.undo"));
        var panel = new Keyed("panel", CommandsWith(EditorKey.Q, KeyModifiers.None, () => other++, "panel.q"));

        router.Dispatch(new KeyFrame(false, false, canvas));
        Assert.Equal((1, 0), (own, global));
        router.Dispatch(new KeyFrame(false, false, panel));
        Assert.Equal((1, 1, 0), (own, global, other));
        router.Dispatch(new KeyFrame(false, false, Focused: null));
        Assert.Equal((1, 2), (own, global));
    }

    /// <summary>
    /// ⭐ Acceptance ③ (the state half) — a click on the map gives it the keyboard; a click on a panel takes it back; no click keeps it.
    /// ✅ Red-proof: Dispatch not writing <see cref="KeyboardFocus.MapHasKeyboard"/> ⇒ the second assertion stays true.
    /// </summary>
    [Fact]
    public void AClickOnTheMapGivesItTheKeyboard_AClickOnAPanelTakesItBack()
    {
        var router = new KeyRouter(new Pressing());
        router.Dispatch(new KeyFrame(false, false, null, MouseClicked: true, ClickOnUi: false));
        Assert.True(KeyboardFocus.MapHasKeyboard);
        router.Dispatch(new KeyFrame(false, false, null, MouseClicked: true, ClickOnUi: true));
        Assert.False(KeyboardFocus.MapHasKeyboard);
        router.Dispatch(new KeyFrame(false, false, null));
        Assert.False(KeyboardFocus.MapHasKeyboard);
        router.Dispatch(new KeyFrame(false, false, null, MouseClicked: true, ClickOnUi: false));
        Assert.True(KeyboardFocus.MapHasKeyboard);
    }

    /// <summary>⛔ Hot path (R-2084) — a steady frame (a focused window, keys bound, nothing pressed) allocates nothing.</summary>
    [Fact]
    public void ASteadyFrame_AllocatesNothing()
    {
        var input = new Pressing();
        var router = new KeyRouter(input) { GlobalCommands = CommandsWith(EditorKey.S, KeyModifiers.Ctrl, () => { }, "shell.save") };
        var canvas = new Keyed("canvas", CommandsWith(EditorKey.F, KeyModifiers.Ctrl, () => { }));
        var frame = new KeyFrame(false, false, canvas);
        for (int i = 0; i < 3; i++) router.Dispatch(frame);   // first frames declare

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) router.Dispatch(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── the real frame: ImGui's focus, the WindowManager's router ──────────────────────────────────────────────────────────────────────────

    private sealed class KeyedWindow : ManagedWindow, IKeyScopeWindow
    {
        public KeyedWindow(string id, Vector2 pos, IEditorCommands commands) : base(id, id, "any", WindowScope.Global)
        {
            _pos = pos;
            KeyCommands = commands;
            IsOpen = true;
        }
        private readonly Vector2 _pos;
        public Vector2 Min, Max;
        public string KeyScopeKind => Id;
        public IEditorCommands? KeyCommands { get; }
        protected override void BeforeBegin()
        {
            ImGuiApi.SetNextWindowPos(_pos, ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(new Vector2(200, 150), ImGuiCond.Always);
        }
        protected override void DrawClientArea()
        {
            Min = ImGuiApi.GetWindowPos();
            Max = Min + ImGuiApi.GetWindowSize();
            ImGuiApi.Text(Id);
        }
    }

    private static void Frame(ImGuiTestFixture imgui, WM wm)
    {
        imgui.NewFrame();
        wm.Render();
        imgui.Render();
    }

    /// <summary>
    /// ⭐⭐ Acceptance ② and ③ on a REAL ImGui frame through <c>WindowManager.Render</c>: two keyed windows drawn, Ctrl+F reaches only the
    /// one ImGui says is focused; a click on empty space (the map) clears ImGui's focus and gives the map the keyboard, and then neither
    /// window gets the keys; a click on a window takes the keyboard back from the map.
    /// ✅ Red-proof: <c>ManagedWindow</c> not reading <c>IsWindowFocused</c> after Begin ⇒ <c>b</c> stays 0.
    /// </summary>
    [Fact]
    public void OnARealFrame_OnlyTheFocusedWindowGetsItsKeys_AndAClickOnTheMapTakesThemAway()
    {
        using var imgui = new ImGuiTestFixture();
        using var atlas = new IconAtlas(new IntPtr(1), 256f, 256f, 16f);
        var wm = new WM(atlas);
        int a = 0, b = 0;
        var winA = new KeyedWindow("win_a", new Vector2(40, 60), CommandsWith(EditorKey.F, KeyModifiers.Ctrl, () => a++));
        var winB = new KeyedWindow("win_b", new Vector2(300, 60), CommandsWith(EditorKey.F, KeyModifiers.Ctrl, () => b++));
        wm.RegisterWindow(winA);
        wm.RegisterWindow(winB);
        var io = ImGuiApi.GetIO();

        Frame(imgui, wm);
        Frame(imgui, wm);

        // A click inside B focuses it, and the click was on the UI ⇒ the map loses the keyboard.
        var inB = (winB.Min + winB.Max) * 0.5f;
        io.AddMousePosEvent(inB.X, inB.Y);
        io.AddMouseButtonEvent(0, true);
        Frame(imgui, wm);
        io.AddMouseButtonEvent(0, false);
        Frame(imgui, wm);
        Assert.True(winB.HasKeyFocus);
        Assert.False(winA.HasKeyFocus);
        Assert.False(KeyboardFocus.MapHasKeyboard);

        // Ctrl+F ⇒ B only.
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.F, true);
        Frame(imgui, wm);
        io.AddKeyEvent(ImGuiKey.F, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Frame(imgui, wm);
        Assert.Equal((0, 1), (a, b));

        // A click on empty space — the map — clears ImGui's focus and gives the map the keyboard.
        io.AddMousePosEvent(900, 700);
        io.AddMouseButtonEvent(0, true);
        Frame(imgui, wm);
        io.AddMouseButtonEvent(0, false);
        Frame(imgui, wm);
        Assert.True(KeyboardFocus.MapHasKeyboard);
        Assert.False(winA.HasKeyFocus || winB.HasKeyFocus);

        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.F, true);
        Frame(imgui, wm);
        io.AddKeyEvent(ImGuiKey.F, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Frame(imgui, wm);
        Assert.Equal((0, 1), (a, b));   // nobody's: the map has the keys
    }
}
