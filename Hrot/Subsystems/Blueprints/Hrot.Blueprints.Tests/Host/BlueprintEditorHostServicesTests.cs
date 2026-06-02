using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Visuals;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Behavioral tests for <see cref="BlueprintEditorHostServices"/> (AIE-045).
/// All tests are headless (no ImGui, no Raylib).
/// </summary>
public sealed class BlueprintEditorHostServicesTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (BlueprintEditorHostServices svc,
                    BlueprintGraphModel         model,
                    BlueprintCommandSink        sink)
        MakeSut(
            IReadOnlyList<ICustomCanvasRenderer>? customRenderers = null,
            IDebugSession?                        debugSession    = null)
    {
        var asset  = BlueprintAssetBuilder.Instance("SvcTest").WithGraph("Main", GraphKind.Event, _ => { }).Build();
        var graph  = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editService = new EditService();
        var commandSink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editService,
            markDirty: _ => { });

        var svc = new BlueprintEditorHostServices(
            catalog,
            typeSystem,
            validator,
            commandSink,
            new StubPickerRegistry(),
            new StubClipboard(),
            new StubIconProvider(),
            diagnostics:         null,
            input:               new StubInputSource(),
            theme:               new StubEditorTheme(),
            debug:               debugSession,
            customRenderers:     customRenderers);

        return (svc, model, commandSink);
    }

    // ── full surface non-null ─────────────────────────────────────────────────

    [Fact]
    public void BlueprintEditorHostServices_FullSurface_NonNull()
    {
        var (svc, _, _) = MakeSut();

        Assert.NotNull(svc.NodeCatalog);
        Assert.NotNull(svc.TypeSystem);
        Assert.NotNull(svc.LinkValidator);
        Assert.NotNull(svc.CommandSink);
        Assert.NotNull(svc.Pickers);
        Assert.NotNull(svc.Clipboard);
        Assert.NotNull(svc.Icons);
        Assert.NotNull(svc.Input);
        Assert.NotNull(svc.Theme);
        // Diagnostics and Debug are nullable by design — we just confirm no exception.
        _ = svc.Diagnostics;
        _ = svc.Debug;
    }

    [Fact]
    public void BlueprintEditorHostServices_Implements_IEditorHostServices()
    {
        var (svc, _, _) = MakeSut();
        Assert.IsAssignableFrom<IEditorHostServices>(svc);
    }

    // ── GraphView constructs ──────────────────────────────────────────────────

    [Fact]
    public void BlueprintEditorHostServices_GraphView_Constructs()
    {
        var (svc, model, sink) = MakeSut();

        // Per the canvas assembly contract (DESIGN §2):
        // var view = new GraphView(model, host.CommandSink, host.Validator, host.TypeSystem, host.NodeCatalog, host);
        var view = new GraphView(model, svc.CommandSink, svc.LinkValidator, svc.TypeSystem, svc.NodeCatalog, svc);

        Assert.NotNull(view);
    }

    [Fact]
    public void BlueprintEditorHostServices_GraphView_ExposesProjectedNodes()
    {
        var asset = BlueprintAssetBuilder.Instance("WithNode").WithGraph("Main", GraphKind.Event, g => g.Entry()).Build();
        var graph  = asset.Graphs[0];
        var typeSystem  = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model       = new BlueprintGraphModel(asset, graph);
        var catalog     = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator   = new BlueprintLinkValidator(model, typeSystem);
        var history     = new CommandHistory();
        var editService = new EditService();
        var sink = new BlueprintCommandSink(asset, graph, model, catalog, validator, history, editService, _ => { });

        var svc = new BlueprintEditorHostServices(
            catalog, typeSystem, validator, sink,
            new StubPickerRegistry(), new StubClipboard(), new StubIconProvider(),
            null, new StubInputSource(), new StubEditorTheme());

        var view = new GraphView(model, svc.CommandSink, svc.LinkValidator, svc.TypeSystem, svc.NodeCatalog, svc);

        // The graph has 1 entry node — the model projects it.
        Assert.NotEmpty(view.Model.Nodes);
        Assert.Equal(graph.Nodes.Count, view.Model.Nodes.Count);
    }

    // ── custom renderers ──────────────────────────────────────────────────────

    [Fact]
    public void BlueprintEditorHostServices_CustomRenderers_IncludeBlueprintRenderers()
    {
        // Create with the real WhenFiringPulseRenderer (Blueprint custom renderer).
        var renderers = new List<ICustomCanvasRenderer>
        {
            new WhenFiringPulseRenderer(isDebugMode: false),
        };
        var (svc, _, _) = MakeSut(customRenderers: renderers);

        Assert.Single(svc.CustomCanvasRenderers);
        Assert.IsType<WhenFiringPulseRenderer>(svc.CustomCanvasRenderers[0]);
    }

    [Fact]
    public void BlueprintEditorHostServices_DefaultCustomRenderers_IsEmpty()
    {
        var (svc, _, _) = MakeSut(customRenderers: null);
        Assert.Empty(svc.CustomCanvasRenderers);
    }

    // ── debug session ─────────────────────────────────────────────────────────

    [Fact]
    public void BlueprintEditorHostServices_SetDebugSession_UpdatesDebug()
    {
        var (svc, _, _) = MakeSut();
        Assert.Null(svc.Debug);

        var session = new StubDebugSession();
        svc.SetDebugSession(session);

        Assert.Same(session, svc.Debug);
    }

    [Fact]
    public void BlueprintEditorHostServices_SetDebugSession_Null_ClearsDebug()
    {
        var (svc, _, _) = MakeSut(debugSession: new StubDebugSession());
        Assert.NotNull(svc.Debug);

        svc.SetDebugSession(null);

        Assert.Null(svc.Debug);
    }

    // ── stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubPickerRegistry : IPickerRegistry
    {
        public void Register<TItem>(string sourceKey, IPickerSource<TItem> source) { }
        public IPickerSource<TItem>? Get<TItem>(string sourceKey) => null;
        public void Open(string sourceKey, Vector2 screenPos, Action<object> onPick,
            Action? onCancel = null, IReadOnlyDictionary<string, object?>? context = null) { }
    }

    private sealed class StubClipboard : IClipboard
    {
        public string? GetText() => null;
        public void SetText(string text) { }
    }

    private sealed class StubIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle) { handle = default; return false; }
    }

    private sealed class StubInputSource : IInputSource
    {
        public Vector2 MousePosition => Vector2.Zero;
        public Vector2 MouseDelta    => Vector2.Zero;
        public float   WheelDelta    => 0f;
        public bool IsMouseDown(MouseButton btn)                            => false;
        public bool IsMousePressed(MouseButton btn)                         => false;
        public bool IsMouseDoubleClicked(MouseButton btn)                   => false;
        public bool IsMouseReleased(MouseButton btn)                        => false;
        public bool IsKeyDown(EditorKey key)                                => false;
        public bool IsKeyPressed(EditorKey key, bool allowRepeat = false)   => false;
        public bool IsKeyReleased(EditorKey key)                            => false;
        public KeyModifiers Modifiers                                        => KeyModifiers.None;
        public ReadOnlySpan<char> TextThisFrame                             => ReadOnlySpan<char>.Empty;
    }

    private sealed class StubEditorTheme : IEditorTheme
    {
        public System.Numerics.Vector4 BackgroundColor       => System.Numerics.Vector4.Zero;
        public System.Numerics.Vector4 GridMinorColor        => System.Numerics.Vector4.Zero;
        public System.Numerics.Vector4 GridMajorColor        => System.Numerics.Vector4.Zero;
        public System.Numerics.Vector4 SelectionAccent       => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 PrimarySelectionAccent => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 ErrorColor            => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 WarningColor          => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 TextDefault           => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 TextMuted             => System.Numerics.Vector4.One;
        public System.Numerics.Vector4 GetCategoryHeaderColor(NodeCategory c) => System.Numerics.Vector4.One;
        public float NodeCornerRadius                        => 4f;
        public float NodeBorderThickness                     => 1f;
        public float NodeHeaderHeight                        => 24f;
        public float PinGlyphSize                            => 10f;
        public float WireThicknessExec                       => 2f;
        public float WireThicknessData                       => 2f;
        public nint  GetFontForSize(float size)              => nint.Zero;
    }

    private sealed class StubDebugSession : IDebugSession
    {
        public bool               IsAttached             => false;
        public bool               IsPaused               => false;
        public NodeId?            CurrentlyExecutingNode => null;
        public IReadOnlySet<NodeId> RecentlyExecutedNodes => new System.Collections.Generic.HashSet<NodeId>();
        public IReadOnlySet<NodeId> Breakpoints           => new System.Collections.Generic.HashSet<NodeId>();
        public IReadOnlySet<PinId>  WatchedPins           => new System.Collections.Generic.HashSet<PinId>();
        public void ToggleBreakpoint(NodeId node) { }
        public void ToggleWatch(PinId pin) { }
        public void Continue() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public object? GetWatchValue(PinId pin) => null;
        public event System.Action? StateChanged;
    }
}
