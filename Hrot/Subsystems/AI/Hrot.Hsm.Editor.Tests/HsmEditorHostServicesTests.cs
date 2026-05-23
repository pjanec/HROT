using System;
using System.Collections.Generic;
using System.Numerics;
using Fhsm.Compiler;
using FluentAssertions;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmEditorHostServicesTests
{
    // ---- helpers ----

    private static HsmAsset MakeDummyAsset()
    {
        var builder  = new HsmBuilder("Dummy");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), "Dummy", "", false, "");
    }

    private static HsmEditorHostServices MakeServices(
        IReadOnlyList<ICustomCanvasRenderer>? renderers = null,
        IDebugSession? debug = null)
    {
        var asset = MakeDummyAsset();
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
            new StubEditorTheme(),
            debug,
            renderers);
    }

    // ---- stubs ----

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
        {
            handle = default;
            return false;
        }
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

    private sealed class StubDebugSession : IDebugSession
    {
        public bool IsAttached => false;
        public bool IsPaused   => false;
        public NodeId?              CurrentlyExecutingNode => null;
        public IReadOnlySet<NodeId> RecentlyExecutedNodes  => new HashSet<NodeId>();
        public IReadOnlySet<NodeId> Breakpoints            => new HashSet<NodeId>();
        public IReadOnlySet<PinId>  WatchedPins            => new HashSet<PinId>();
        public void ToggleBreakpoint(NodeId node) { }
        public void ToggleWatch(PinId pin) { }
        public void Continue()  { }
        public void StepOver()  { }
        public void StepInto()  { }
        public void StepOut()   { }
        public object? GetWatchValue(PinId pin) => null;
        public event Action? StateChanged { add { } remove { } }
    }

    private sealed class StubRenderer : ICustomCanvasRenderer
    {
        public string           Id   => "stub";
        public CanvasRenderPass Pass => default;
        public void Render(ICanvasRenderContext ctx) { }
    }

    // ---- tests ----

    [Fact]
    public void Properties_return_injected_values()
    {
        var asset      = MakeDummyAsset();
        var catalog    = new HsmNodeCatalog();
        var typeSystem = new HsmTypeSystem();
        var validator  = new HsmLinkValidator(asset);
        var cmdSink    = new HsmCommandSink(asset);
        var pickers    = new StubPickerRegistry();
        var clipboard  = new StubClipboard();
        var icons      = new StubIconProvider();
        var input      = new StubInputSource();
        var theme      = new StubEditorTheme();

        var svc = new HsmEditorHostServices(
            catalog, typeSystem, validator, cmdSink,
            pickers, clipboard, icons, null, input, theme);

        svc.NodeCatalog  .Should().BeSameAs(catalog);
        svc.TypeSystem   .Should().BeSameAs(typeSystem);
        svc.LinkValidator.Should().BeSameAs(validator);
        svc.CommandSink  .Should().BeSameAs(cmdSink);
        svc.Pickers      .Should().BeSameAs(pickers);
        svc.Clipboard    .Should().BeSameAs(clipboard);
        svc.Icons        .Should().BeSameAs(icons);
        svc.Input        .Should().BeSameAs(input);
        svc.Theme        .Should().BeSameAs(theme);
        svc.Diagnostics  .Should().BeNull();
    }

    [Fact]
    public void CustomRenderers_defaults_to_empty_when_null_passed()
    {
        var svc = MakeServices(renderers: null);
        svc.CustomCanvasRenderers.Count.Should().Be(0);
    }

    [Fact]
    public void SetDebugSession_updates_debug_property()
    {
        var svc = MakeServices(debug: null);
        svc.Debug.Should().BeNull();

        var session = new StubDebugSession();
        svc.SetDebugSession(session);
        svc.Debug.Should().BeSameAs(session);
    }

    [Fact]
    public void Implements_IEditorHostServices()
    {
        var svc = MakeServices();
        svc.Should().BeAssignableTo<IEditorHostServices>();
    }

    [Fact]
    public void CustomRenderers_returns_provided_list()
    {
        var renderer = new StubRenderer();
        var list     = new List<ICustomCanvasRenderer> { renderer };
        var svc      = MakeServices(renderers: list);
        svc.CustomCanvasRenderers.Count.Should().Be(1);
        svc.CustomCanvasRenderers[0].Should().BeSameAs(renderer);
    }
}
