using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Diagnostics.Breakpoints;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

// Aliases to disambiguate generic tuples in the stub.
using MountedPredicate = (Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint, Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate Compiled);
using MountedScanner  = (Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint, Hrot.Diagnostics.Breakpoints.CompiledEventScanner Scanner);

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// Unit tests for BTreeBreakpointContextMenuProvider and SetBreakpointManager wiring
/// on BTreeEditorHostServices (UBP-P10T7).
/// </summary>
public sealed class BTreeBreakpointWiringTests
{
    // ── Stub: IDataBreakpointManager ────────────────────────────────────────

    private sealed class StubBreakpointManager : IDataBreakpointManager
    {
        public BreakpointId Add(Breakpoint breakpoint) => BreakpointId.Invalid;
        public BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                                          int occurrenceThreshold = 0, string displayName = "",
                                          Guid? sourceElementId = null) => BreakpointId.Invalid;
        public void Remove(BreakpointId id) { }
        public void SetEnabled(BreakpointId id, bool enabled) { }
        public void UpdateCondition(BreakpointId id, SearchPredicateDto? condition) { }
        public void MarkAsWatch(BreakpointId id, bool isWatch) { }
        public void SaveWatches(string path) { }
        public void LoadWatches(string path) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void StageMutation(Entity entity, Type componentType, object componentValue) { }
        public void OnHit(Breakpoint bp, Entity entity) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void OnExternalHit(string tag, Entity entity) { }
        public event Action<Breakpoint, Entity>? OnBreakpointHit { add { } remove { } }
        public event Action<bool>? OnPauseStateChanged { add { } remove { } }
        public bool IsPaused => false;
        public ISimulationView ActiveView => throw new NotSupportedException();
        public long PausedTick => 0L;
        public int PendingMutationsCount => 0;
        public IReadOnlyList<Breakpoint> AllBreakpoints => Array.Empty<Breakpoint>();
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }
        public IReadOnlyList<MountedPredicate> MountedComponentPredicates => Array.Empty<MountedPredicate>();
        public IReadOnlyList<MountedScanner>   MountedEventScanners       => Array.Empty<MountedScanner>();
    }

    // ── Stubs: NodeEditor infrastructure ────────────────────────────────────

    private sealed class StubGraph : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067
        public INodeModel?  FindNode(NodeId id) => null;
        public IPinModel?   FindPin(PinId id)   => null;
        public ILinkModel?  FindLink(LinkId id) => null;
    }

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
        public bool TryGet(string key, out IconHandle handle)
        { handle = default; return false; }
    }

    private sealed class StubInputSource : IInputSource
    {
        public Vector2 MousePosition => Vector2.Zero;
        public Vector2 MouseDelta    => Vector2.Zero;
        public float   WheelDelta    => 0f;
        public bool IsMouseDown(MouseButton btn)                        => false;
        public bool IsMousePressed(MouseButton btn)                     => false;
        public bool IsMouseReleased(MouseButton btn)                    => false;
        public bool IsMouseDoubleClicked(MouseButton btn)               => false;
        public bool IsKeyDown(EditorKey k)                              => false;
        public bool IsKeyPressed(EditorKey k, bool allowRepeat = false) => false;
        public bool IsKeyReleased(EditorKey k)                          => false;
        public KeyModifiers Modifiers => default;
        public ReadOnlySpan<char> TextThisFrame => ReadOnlySpan<char>.Empty;
    }

    private sealed class StubEditorTheme : IEditorTheme
    {
        public Vector4 BackgroundColor        => Vector4.Zero;
        public Vector4 GridMinorColor         => Vector4.Zero;
        public Vector4 GridMajorColor         => Vector4.Zero;
        public Vector4 SelectionAccent        => Vector4.Zero;
        public Vector4 PrimarySelectionAccent => Vector4.Zero;
        public Vector4 ErrorColor             => Vector4.Zero;
        public Vector4 WarningColor           => Vector4.Zero;
        public Vector4 TextDefault            => Vector4.Zero;
        public Vector4 TextMuted              => Vector4.Zero;
        public Vector4 GetCategoryHeaderColor(NodeCategory category) => Vector4.Zero;
        public float NodeCornerRadius    => 0f;
        public float NodeBorderThickness => 0f;
        public float NodeHeaderHeight    => 0f;
        public float PinGlyphSize        => 0f;
        public float WireThicknessExec   => 0f;
        public float WireThicknessData   => 0f;
        public nint GetFontForSize(float targetPixelSize) => IntPtr.Zero;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BTreeEditorHostServices MakeServices()
    {
        var asset = new BehaviorTreeAsset(Guid.NewGuid(), "T", "/T.cs", true, "BB", "Ctx", EmptyBlob());
        var graph = new StubGraph();
        return new BTreeEditorHostServices(
            new BTreeNodeCatalog(),
            new BTreeTypeSystem(),
            new BTreeLinkValidator(graph),
            new BTreeCommandSink(asset, graph),
            new StubPickerRegistry(),
            new StubClipboard(),
            new StubIconProvider(),
            diagnostics: null,
            new StubInputSource(),
            new StubEditorTheme());
    }

    // ── Test 1: Context menu provider returns breakpoint items ──────────────

    [Fact]
    public void BTree_ContextMenu_ShowsBreakpointItems_WhenManagerWired()
    {
        var provider = new BTreeBreakpointContextMenuProvider(new StubBreakpointManager());

        var items = provider.GetItemsFor(
            Guid.NewGuid().ToString("D"),
            new CustomElementHit("key", CustomElementKind.Standalone, default));

        Assert.Contains(items, i => i.Label == "Break on Activation (Enter)");
    }

    // ── Test 2: RendererId matches the gutter renderer ID ──────────────────

    [Fact]
    public void BTree_ContextMenu_RendererIdMatchesGutterRenderer()
    {
        var provider = new BTreeBreakpointContextMenuProvider(new StubBreakpointManager());

        Assert.Equal("btree.breakpoint_gutter", provider.RendererId);
    }

    // ── Test 3: SetBreakpointManager wires the gutter renderer ─────────────

    [Fact]
    public void BTree_GutterRenderer_ManagerWired_IsReady()
    {
        var services = MakeServices();
        services.SetBreakpointManager(new StubBreakpointManager());

        Assert.NotNull(services.BpGutterRenderer);
        // D-BP-03: CountManagerBreakpoints must not throw when asset is the real asset.
        Assert.Equal(0, services.BpGutterRenderer!.CountManagerBreakpoints());
    }

    // ── Test 4: SetBreakpointManager(null) clears the gutter renderer ───────

    [Fact]
    public void BTree_GutterRenderer_ClearedWhenManagerNull()
    {
        var services = MakeServices();
        services.SetBreakpointManager(new StubBreakpointManager());
        services.SetBreakpointManager(null);

        Assert.Null(services.BpGutterRenderer);
    }
}
