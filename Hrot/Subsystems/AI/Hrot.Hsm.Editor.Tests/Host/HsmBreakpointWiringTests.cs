using System;
using System.Collections.Generic;
using System.Numerics;
using Fhsm.Compiler;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

// Aliases to disambiguate generic tuples in the stub.
using MountedPredicate = (Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint, Hrot.Diagnostics.Breakpoints.CompiledComponentPredicate Compiled);
using MountedScanner  = (Hrot.Diagnostics.Breakpoints.Breakpoint Breakpoint, Hrot.Diagnostics.Breakpoints.CompiledEventScanner Scanner);

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// Unit tests for HsmBreakpointContextMenuProvider and SetBreakpointManager wiring
/// on HsmEditorHostServices (UBP-P10T8).
/// </summary>
public sealed class HsmBreakpointWiringTests
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

    // ── Stubs: NodeEditor infrastructure (reused from HsmEditorHostServicesTests) ──

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

    // ── Helper ───────────────────────────────────────────────────────────────

    private static HsmEditorHostServices MakeServices()
    {
        var builder  = new HsmBuilder("Test");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        var asset    = HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), "Test", "", false, "");

        return new HsmEditorHostServices(
            new HsmNodeCatalog(),
            new HsmTypeSystem(),
            new HsmLinkValidator(asset),
            new HsmCommandSink(asset),
            new StubPickerRegistry(),
            new StubClipboard(),
            new StubIconProvider(),
            null,
            new StubInputSource(),
            new StubEditorTheme());
    }

    // ── Test 1: Context menu returns breakpoint items ───────────────────────

    [Fact]
    public void Hsm_ContextMenu_ShowsBreakpointItems_WhenManagerWired()
    {
        var provider = new HsmBreakpointContextMenuProvider(new StubBreakpointManager());

        var items = provider.GetItemsFor(
            Guid.NewGuid().ToString("D"),
            new CustomElementHit("key", CustomElementKind.Standalone, default));

        Assert.Contains(items, i => i.Label == "Break on Enter");
    }

    // ── Test 2: RendererId matches the gutter renderer ID ──────────────────

    [Fact]
    public void Hsm_ContextMenu_RendererIdMatchesGutterRenderer()
    {
        var provider = new HsmBreakpointContextMenuProvider(new StubBreakpointManager());

        Assert.Equal("hsm.breakpoint_gutter", provider.RendererId);
    }

    // ── Test 3: SetBreakpointManager wires the gutter renderer ─────────────

    [Fact]
    public void Hsm_GutterRenderer_ManagerWired_IsReady()
    {
        var services = MakeServices();
        services.SetBreakpointManager(new StubBreakpointManager());

        Assert.NotNull(services.BpGutterRenderer);
        // D-BP-03: CountBreakpoints must not throw when asset is the real asset.
        var (stateDots, transDots) = services.BpGutterRenderer!.CountBreakpoints();
        Assert.Equal(0, stateDots + transDots);
    }

    // ── Test 4: SetBreakpointManager(null) clears the gutter renderer ───────

    [Fact]
    public void Hsm_GutterRenderer_ClearedWhenManagerNull()
    {
        var services = MakeServices();
        services.SetBreakpointManager(new StubBreakpointManager());
        services.SetBreakpointManager(null);

        Assert.Null(services.BpGutterRenderer);
    }
}
