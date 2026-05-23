using System.Collections.Generic;
using NodeEditor.Core.Action;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using NodeEditor.UI.MiniEditors;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Bundles all fake host services in a single IEditorHostServices implementation.</summary>
public sealed class FakeHostServices : IEditorHostServices
{
    public FakeGraphModel     Graph       { get; }
    public FakeCommandSink    CommandSink_  { get; }
    public FakeNodeCatalog    NodeCatalog_  { get; }
    public FakeTypeSystem     TypeSystem_   { get; }
    public FakeLinkValidator  Validator     { get; }
    public FakeMyBlueprintModel MyBlueprint { get; private set; }
    public FakeInputSource    Input_        { get; }
    public PickerRegistry     PickerRegistry_ { get; }
    public ToastQueue         ToastQueue_   { get; } = new();
    public IPinDefaultValueEditorRegistry EditorRegistry { get; }

    // IEditorHostServices
    public INodeCatalog        NodeCatalog { get; }
    public ITypeSystem         TypeSystem  { get; }
    public ILinkValidator      LinkValidator { get; }
    IGraphCommandSink IEditorHostServices.CommandSink => CommandSink_;
    public IPickerRegistry     Pickers     { get; }
    public IClipboard          Clipboard   { get; }
    public IIconProvider       Icons       { get; }
    public IDiagnosticsSink?   Diagnostics { get; }
    public IDebugSession?      Debug       { get; set; } // mutable so scenarios can attach one
    public IInputSource        Input       { get; }
    public IEditorTheme        Theme       { get; }

    public FakeHostServices(FakeGraphModel graph, Dictionary<float, nint>? fonts = null)
    {
        Graph          = graph;
        NodeCatalog_   = new FakeNodeCatalog();
        EditorRegistry = PinDefaultValueEditorRegistry.CreateWithBuiltins();
        TypeSystem_    = new FakeTypeSystem(EditorRegistry);
        CommandSink_   = new FakeCommandSink(graph, NodeCatalog_, TypeSystem_);
        Validator      = new FakeLinkValidator(graph);
        MyBlueprint    = new FakeMyBlueprintModel();
        Input_         = new FakeInputSource();
        PickerRegistry_ = new PickerRegistry();
        PickerRegistry_.SetServices(new FakeIconProvider(), new FakeEditorTheme());
        var nodePicker = new FakeNodePickerSource(NodeCatalog_);
        PickerRegistry_.Register("nodes.all", nodePicker);
        PickerRegistry_.Register("nodes.by-pin", nodePicker);

        // Assign interface properties
        NodeCatalog  = NodeCatalog_;
        TypeSystem   = TypeSystem_;
        LinkValidator = Validator;
        Pickers      = PickerRegistry_;
        Clipboard    = new FakeClipboard();
        Icons        = new FakeIconProvider();
        Diagnostics  = new FakeDiagnosticsSink();
        Input        = Input_;
        Theme        = new FakeEditorTheme(fonts ?? new Dictionary<float, nint>());
    }

    /// <summary>Replace the My Blueprint model (used by multi-graph scenarios).</summary>
    public void OverrideMyBlueprint(FakeMyBlueprintModel model) => MyBlueprint = model;

    // ---- Custom canvas renderers (registered by scenarios that need them) ----

    /// <summary>Mutable list populated by scenarios before the canvas view is created.</summary>
    public List<ICustomCanvasRenderer> CustomRenderers { get; } = new();

    IReadOnlyList<ICustomCanvasRenderer> IEditorHostServices.CustomCanvasRenderers => CustomRenderers;
}
