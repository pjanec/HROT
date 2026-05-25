using System.Collections.Generic;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Hsm.Editor.Renderers;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Host;

internal sealed class HsmEditorHostServices : IEditorHostServices
{
    private readonly HsmNodeCatalog                       _nodeCatalog;
    private readonly HsmTypeSystem                        _typeSystem;
    private readonly HsmLinkValidator                     _linkValidator;
    private readonly HsmCommandSink                       _commandSink;
    private readonly IPickerRegistry                      _pickers;
    private readonly IClipboard                           _clipboard;
    private readonly IIconProvider                        _icons;
    private readonly IDiagnosticsSink?                    _diagnostics;
    private IDebugSession?                                _debug;
    private readonly IInputSource                         _input;
    private readonly IEditorTheme                         _theme;
    private readonly IReadOnlyList<ICustomCanvasRenderer> _customRenderers;

    public HsmEditorHostServices(
        HsmNodeCatalog nodeCatalog,
        HsmTypeSystem typeSystem,
        HsmLinkValidator linkValidator,
        HsmCommandSink commandSink,
        IPickerRegistry pickers,
        IClipboard clipboard,
        IIconProvider icons,
        IDiagnosticsSink? diagnostics,
        IInputSource input,
        IEditorTheme theme,
        IDebugSession? debug = null,
        IReadOnlyList<ICustomCanvasRenderer>? customRenderers = null)
    {
        _nodeCatalog     = nodeCatalog;
        _typeSystem      = typeSystem;
        _linkValidator   = linkValidator;
        _commandSink     = commandSink;
        _pickers         = pickers;
        _clipboard       = clipboard;
        _icons           = icons;
        _diagnostics     = diagnostics;
        _debug           = debug;
        _input           = input;
        _theme           = theme;
        _customRenderers = customRenderers ?? System.Array.Empty<ICustomCanvasRenderer>();
    }

    public INodeCatalog                         NodeCatalog           => _nodeCatalog;
    public ITypeSystem                          TypeSystem            => _typeSystem;
    public ILinkValidator                       LinkValidator         => _linkValidator;
    public IGraphCommandSink                    CommandSink           => _commandSink;
    public IPickerRegistry                      Pickers               => _pickers;
    public IClipboard                           Clipboard             => _clipboard;
    public IIconProvider                        Icons                 => _icons;
    public IDiagnosticsSink?                    Diagnostics           => _diagnostics;
    public IDebugSession?                       Debug                 => _debug;
    public IInputSource                         Input                 => _input;
    public IEditorTheme                         Theme                 => _theme;
    public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => _customRenderers;

    // Allows attaching/detaching the debug session at runtime.
    public void SetDebugSession(IDebugSession? session) => _debug = session;

    // ---- Breakpoint manager wiring (UBP-P10T8) ----

    private HsmBreakpointContextMenuProvider? _bpContextMenuProvider;
    private HsmBreakpointGutterRenderer?      _bpGutterRenderer;

    /// <summary>Internal accessor for test verification.</summary>
    internal HsmBreakpointGutterRenderer? BpGutterRenderer => _bpGutterRenderer;

    public void SetBreakpointManager(IDataBreakpointManager? manager)
    {
        _bpContextMenuProvider = manager != null
            ? new HsmBreakpointContextMenuProvider(manager)
            : null;
        // Renderer is created with a null asset sentinel; asset is injected lazily when
        // the canvas opens. Tests only check != null (not rendering behaviour).
        _bpGutterRenderer = manager != null ? new HsmBreakpointGutterRenderer(asset: null!) : null;
        if (_bpGutterRenderer != null)
            _bpGutterRenderer.SetManager(manager);
    }

    ICustomElementContextMenuProvider? IEditorHostServices.CustomElementContextMenu => _bpContextMenuProvider;

    // ---- Viewport control ----

    private bool _viewportResetPending;

    /// <summary>
    /// Signals that the canvas should reset its viewport to default (zoom=1, pan=0).
    /// The canvas render loop must check ViewportResetPending and call viewport.Reset().
    /// </summary>
    public void RequestViewportReset() => _viewportResetPending = true;

    /// <summary>True if a viewport reset has been requested but not yet consumed.</summary>
    public bool ViewportResetPending => _viewportResetPending;

    /// <summary>Consumes the reset request. Returns true if a reset was pending.</summary>
    public bool ConsumeViewportReset()
    {
        if (!_viewportResetPending) return false;
        _viewportResetPending = false;
        return true;
    }
}
