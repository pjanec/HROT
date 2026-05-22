using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Bundles all fake host services in a single IEditorHostServices implementation.</summary>
public sealed class FakeHostServices : IEditorHostServices
{
    public FakeGraphModel     Graph       { get; }
    public FakeCommandSink    CommandSink_  { get; }
    public FakeNodeCatalog    NodeCatalog_  { get; }
    public FakeTypeSystem     TypeSystem_   { get; }
    public FakeLinkValidator  Validator     { get; }
    public FakeMyBlueprintModel MyBlueprint { get; }
    public FakeInputSource    Input_        { get; }
    public PickerRegistry     PickerRegistry_ { get; }

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

    public FakeHostServices(FakeGraphModel graph)
    {
        Graph          = graph;
        NodeCatalog_   = new FakeNodeCatalog();
        TypeSystem_    = new FakeTypeSystem();
        CommandSink_   = new FakeCommandSink(graph, NodeCatalog_);
        Validator      = new FakeLinkValidator(graph);
        MyBlueprint    = new FakeMyBlueprintModel();
        Input_         = new FakeInputSource();
        PickerRegistry_ = new PickerRegistry();
        PickerRegistry_.SetServices(new FakeIconProvider(), new FakeEditorTheme());

        // Assign interface properties
        NodeCatalog  = NodeCatalog_;
        TypeSystem   = TypeSystem_;
        LinkValidator = Validator;
        Pickers      = PickerRegistry_;
        Clipboard    = new FakeClipboard();
        Icons        = new FakeIconProvider();
        Diagnostics  = new FakeDiagnosticsSink();
        Input        = Input_;
        Theme        = new FakeEditorTheme();
    }
}
