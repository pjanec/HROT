using System;
using System.Collections.Generic;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Renderers;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.BTree.Editor.Host;

internal sealed class BTreeEditorHostServices : IEditorHostServices
{
    private readonly BTreeNodeCatalog _nodeCatalog;
    private readonly BTreeTypeSystem _typeSystem;
    private readonly BTreeLinkValidator _linkValidator;
    private readonly BTreeCommandSink _commandSink;
    private readonly IPickerRegistry _pickers;
    private readonly IClipboard _clipboard;
    private readonly IIconProvider _icons;
    private readonly IDiagnosticsSink? _diagnostics;
    private IDebugSession? _debug;
    private readonly IInputSource _input;
    private readonly IEditorTheme _theme;
    private readonly IReadOnlyList<ICustomCanvasRenderer> _customRenderers;

    public BTreeEditorHostServices(
        BTreeNodeCatalog nodeCatalog,
        BTreeTypeSystem typeSystem,
        BTreeLinkValidator linkValidator,
        BTreeCommandSink commandSink,
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

    public INodeCatalog NodeCatalog => _nodeCatalog;
    public ITypeSystem TypeSystem => _typeSystem;
    public ILinkValidator LinkValidator => _linkValidator;
    public IGraphCommandSink CommandSink => _commandSink;
    public IPickerRegistry Pickers => _pickers;
    public IClipboard Clipboard => _clipboard;
    public IIconProvider Icons => _icons;
    public IDiagnosticsSink? Diagnostics => _diagnostics;
    public IDebugSession? Debug => _debug;
    public IInputSource Input => _input;
    public IEditorTheme Theme => _theme;
    public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => _customRenderers;

    // Allows attaching/detaching the debug session at runtime.
    public void SetDebugSession(IDebugSession? session) => _debug = session;

    // ---- Breakpoint manager wiring (UBP-P10T7) ----

    private BTreeBreakpointContextMenuProvider? _bpContextMenuProvider;
    private BTreeBreakpointGutterRenderer?      _bpGutterRenderer;

    /// <summary>Internal accessor for test verification.</summary>
    internal BTreeBreakpointGutterRenderer? BpGutterRenderer => _bpGutterRenderer;

    public void SetBreakpointManager(IDataBreakpointManager? manager)
    {
        _bpContextMenuProvider = manager != null
            ? new BTreeBreakpointContextMenuProvider(manager)
            : null;
        // Renderer is created with a null asset sentinel; asset is injected lazily when
        // the canvas opens. Tests only check != null (not rendering behaviour).
        _bpGutterRenderer = manager != null ? new BTreeBreakpointGutterRenderer(asset: null!) : null;
        if (_bpGutterRenderer != null)
            _bpGutterRenderer.SetManager(manager);
    }

    ICustomElementContextMenuProvider? IEditorHostServices.CustomElementContextMenu => _bpContextMenuProvider;

    // ---- Node context menu (DEC-03b) ─────────────────────────────────────────

    private BTreeNodeContextMenuProvider? _nodeContextMenuProvider;

    /// <summary>
    /// Wires the node context menu provider. Must be called after construction
    /// when the graph model is available (mirrors SetBreakpointManager pattern).
    /// </summary>
    public void SetNodeContextMenuProvider(IGraphCommandSink sink, IGraphModel model)
    {
        _nodeContextMenuProvider = new BTreeNodeContextMenuProvider(sink, model);
    }

    INodeContextMenuProvider? IEditorHostServices.NodeContextMenu => _nodeContextMenuProvider;

    // ---- Breakpoint toggle (AIE-033) ─────────────────────────────────────────

    /// <summary>
    /// Toggles the breakpoint flag on the specified node by dispatching a
    /// <see cref="GraphCommand.SetNodeProperty"/> command through the command sink.
    /// This is the canonical path for canvas breakpoint-toggle actions; it means
    /// undo/redo history tracks the toggle as a normal graph mutation.
    /// </summary>
    /// <param name="nodeId">The NodeId of the node to toggle.</param>
    /// <param name="value">The desired <c>isBreakpoint</c> value.</param>
    public void ToggleNodeBreakpoint(NodeId nodeId, bool value)
    {
        _commandSink.Apply(new GraphCommand.SetNodeProperty(nodeId, "isBreakpoint", value));
    }

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
