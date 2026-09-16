using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>The outline gets its HOST SERVICES — the tenth instance of one pattern, one level down.</b>
///
/// <para>🔴 <b>What the user's first visual check found</b>, verbatim: <i>"for btree/hsm MyBlueprint
/// panel is present, but reads <b>Editor host service not available for this perspective yet</b>. same
/// for hsm and btree."</i></para>
///
/// <para>📐 <b>The measured cause was a closed loop, two lines apart.</b> <c>SyncToSelection</c> passed
/// <c>Retarget</c> the window's own <c>_hostServices</c>/<c>_commands</c> fields — and <c>Retarget</c>
/// was the <b>only writer</b> of those fields. ⇒ ⛔ nothing outside the window could ever supply them,
/// so <c>_panel</c> was null forever and the placeholder drew on every frame.</para>
///
/// <para>⭐⭐ <b>Batch 80 fixed "nobody CONSTRUCTS the outline". This fixes "nobody FEEDS it."</b>
/// And as in Batch 80 the fix is a <b>derivation, not an argument</b>: the services live on the active
/// document's <c>AiCanvasContext</c>, and the registrar is <b>already handed</b> this perspective's
/// canvas window through <c>RegisterExtraWindow</c>. ⇒ ⛔ nothing new for <c>EditorSubsystem</c> to
/// pass, therefore nothing to forget.</para>
///
/// <para>⭐ <b>The rail that would have caught it</b> is <see cref="AiMyBlueprintWindow.HasPanel"/>,
/// which already existed and was documented as <i>"also a rail surface"</i> — ⚠ <b>and which no test
/// ever asserted was TRUE.</b> That is the hole these rails close.</para>
/// </summary>
public sealed class TheOutlineGetsItsServicesTests
{
    // ══ the production path, end to end ══════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before this batch, on both AI perspectives</b> — this is the user's report as an
    /// assertion. ⛔ Nothing here calls <c>Retarget</c> or supplies a service: the registrar is built
    /// the way <c>EditorSubsystem</c> builds one, and the canvas window is registered the way
    /// <c>EditorSubsystem</c> registers it.
    /// </summary>
    [Theory]
    [InlineData("BTree", AssetKind.BTree)]
    [InlineData("HSM",   AssetKind.Hsm)]
    public void TheOutline_GetsAPanel_OnThePathTheEditorActuallyTakes(string perspective, AssetKind kind)
    {
        var h = Harness.AsTheEditorBuildsIt(perspective, kind);

        Assert.False(h.Outline.HasPanel);          // no document open yet — the honest empty state
        h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();

        Assert.True(h.Outline.HasPanel);
        Assert.NotNull(h.Outline.Model);
    }

    /// <summary>
    /// ⭐⭐ The forwarding rail for the one dependency, <b>asserted on the CONSTRUCTED OBJECT</b> —
    /// ⛔ not on the registrar's source (2026-08-16 rule). Registering the canvas window is what
    /// installs the resolver; the composition root passes nothing extra.
    /// </summary>
    [Theory]
    [InlineData("BTree", AssetKind.BTree)]
    [InlineData("HSM",   AssetKind.Hsm)]
    public void RegisteringTheCanvasWindow_InstallsTheResolver(string perspective, AssetKind kind)
    {
        var h = Harness.AsTheEditorBuildsIt(perspective, kind, registerCanvas: false);
        Assert.False(h.Outline.HasCanvasContextResolver);

        h.RegisterCanvasWindow();
        Assert.True(h.Outline.HasCanvasContextResolver);
    }

    // ══ the negatives — a missing service must not become a crash ════════════

    /// <summary>
    /// ⭐ With no canvas registered the window still tracks its asset and simply has no panel.
    /// ⛔ A throw here would turn a degraded perspective into a dead editor.
    /// </summary>
    [Fact]
    public void WithNoCanvasWindow_TheOutlineStillTracksTheAsset_AndDoesNotThrow()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree, registerCanvas: false);
        h.Store.ActiveAsset = h.Asset;

        h.Outline.SyncToSelection();

        Assert.NotNull(h.Outline.Model);
        Assert.False(h.Outline.HasPanel);
        Assert.Null(h.Outline.ActiveHostServices);
    }

    /// <summary>⭐ And a registered canvas with no active document is the same honest empty state.</summary>
    [Fact]
    public void WithACanvasButNoDocument_ThereIsNoPanel()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);
        h.Outline.SyncToSelection();

        Assert.False(h.Outline.HasPanel);
        Assert.Null(h.Outline.ActiveHostServices);
    }

    // ══ order independence, and the document switch ══════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Either order works.</b> The services arrive later than the window in the running editor
    /// (they are built per document), so a one-shot read at first-asset time would leave the panel
    /// null forever — ⛔ which is precisely how the defect presented.
    /// </summary>
    [Fact]
    public void ServicesArrivingAfterTheAsset_StillProduceAPanel()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);

        h.Store.ActiveAsset = h.Asset;
        h.Outline.SyncToSelection();
        Assert.False(h.Outline.HasPanel);          // asset known, no document yet

        h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();
        Assert.True(h.Outline.HasPanel);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The obvious sequel, gated.</b> Services are per DOCUMENT; a window that reads them once
    /// keeps the first document's host after a switch. ⛔ Re-evaluate on change, not once.
    /// </summary>
    [Fact]
    public void SwitchingDocuments_RebuildsThePanelOverTheNewDocumentsServices()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);

        var first = h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();
        var firstHost = h.Outline.ActiveHostServices;
        Assert.NotNull(firstHost);

        var second = h.OpenDocumentWithACanvas();
        Assert.NotSame(first, second);
        h.Outline.SyncToSelection();

        Assert.True(h.Outline.HasPanel);
        Assert.NotSame(firstHost, h.Outline.ActiveHostServices);
    }

    /// <summary>⭐ Closing the last document gives the panel up rather than drawing a dead one.</summary>
    [Fact]
    public void ClosingTheDocument_GivesUpThePanel()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);
        var doc = h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();
        Assert.True(h.Outline.HasPanel);

        h.Documents.Close(doc);
        h.Outline.SyncToSelection();

        Assert.False(h.Outline.HasPanel);
        Assert.Null(h.Outline.ActiveHostServices);
    }

    // ══ Retarget's contract — the loop must not be re-closable ═══════════════

    /// <summary>
    /// 🔴 <b>The regression guard for the loop itself.</b> A host calling <c>Retarget(vars, null,
    /// null)</c> must NOT erase services the canvas context supplied — ⛔ clearing on null is exactly
    /// what kept the window feeding itself its own nulls.
    /// </summary>
    [Fact]
    public void RetargetWithNullServices_DoesNotEraseTheDerivedOnes()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);
        h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();
        var derived = h.Outline.ActiveHostServices;
        Assert.NotNull(derived);

        h.Outline.Retarget(() => h.Asset.BlackboardVariables, null, null);

        Assert.Same(derived, h.Outline.ActiveHostServices);
        Assert.True(h.Outline.HasPanel);
    }

    /// <summary>⭐ And an explicit host still wins — the parameter survives as an override.</summary>
    [Fact]
    public void ExplicitServices_OverrideTheDerivedOnes()
    {
        var h = Harness.AsTheEditorBuildsIt("BTree", AssetKind.BTree);
        h.OpenDocumentWithACanvas();
        h.Outline.SyncToSelection();

        var mine = new StubHostServices();
        h.Outline.Retarget(() => h.Asset.BlackboardVariables, mine, new EditorCommandsImpl());

        Assert.Same(mine, h.Outline.ActiveHostServices);
    }

    // ══ the perspective that has no outline ══════════════════════════════════

    /// <summary>
    /// ⛔ Blueprint keeps <c>BlueprintMyBlueprintWindow</c> and gets no <c>AiMyBlueprintWindow</c>;
    /// registering its canvas must be a no-op rather than a null dereference.
    /// </summary>
    [Fact]
    public void TheBlueprintPerspective_RegistersItsCanvasWithoutAnOutline()
    {
        var h = Harness.AsTheEditorBuildsIt("Blueprint", AssetKind.Blueprint, registerCanvas: false);
        Assert.Null(h.Registrar.MyBlueprint);

        h.RegisterCanvasWindow();                  // ⛔ must not throw
        Assert.Contains(h.Registrar.RegisteredWindows, w => w is AiGraphCanvasWindow);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Harness — built the way EditorSubsystem builds one, and no other way.
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class Harness
    {
        public required PerspectiveWorkspaceRegistrar Registrar  { get; init; }
        public required EditorSelectionStore          Store      { get; init; }
        public required AiDocumentManager             Documents  { get; init; }
        public required WindowManager                 Windows    { get; init; }
        public required AiGraphCanvasWindow           Canvas     { get; init; }
        public required FakeAsset                     Asset      { get; init; }
        public required AssetKind                     Kind       { get; init; }

        public AiMyBlueprintWindow Outline => Registrar.MyBlueprint!;

        /// <summary>
        /// ⭐ <c>EditorSubsystem</c>'s own sequence: construct the registrar with no host kind and no
        /// resolver, <c>RegisterWindows</c>, then <c>RegisterExtraWindow</c> with the canvas.
        /// </summary>
        public static Harness AsTheEditorBuildsIt(
            string perspective, AssetKind kind, bool registerCanvas = true)
        {
            var store     = new EditorSelectionStore();
            var documents = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
            var windows   = new WindowManager(new IconAtlas(new IntPtr(1), 256f, 256f, 16f));

            var registrar = new PerspectiveWorkspaceRegistrar(
                perspectiveName: perspective,
                selectionStore:  store,
                catalog:         new AssetCatalog(),
                refactorService: new StubRefactor(),
                debugRegistry:   new DebugSessionRegistry());
            registrar.RegisterWindows(windows);

            var h = new Harness
            {
                Registrar = registrar,
                Store     = store,
                Documents = documents,
                Windows   = windows,
                Canvas    = new AiGraphCanvasWindow(perspective, documents, new NoRenderSeam()),
                Asset     = FakeAsset.With(kind, Var("Health"), State("Cursor")),
                Kind      = kind,
            };

            if (registerCanvas) h.RegisterCanvasWindow();
            return h;
        }

        public void RegisterCanvasWindow() => Registrar.RegisterExtraWindow(Windows, Canvas);

        /// <summary>
        /// ⭐ Opens a document with its own <see cref="AiCanvasContext"/> — the shape a real document
        /// factory produces (<c>BTreeDocumentFactory</c> / <c>HsmDocumentFactory</c>): a GraphView
        /// whose <c>Host</c> is the per-document services bag, plus that document's commands.
        /// </summary>
        public AiDocument OpenDocumentWithACanvas()
        {
            var asset = FakeAsset.With(Kind, Var("Health"), State("Cursor"));
            var doc   = Documents.Open(asset);

            var host = new StubHostServices();
            var ctx  = new AiCanvasContext(
                new GraphView(new StubGraphModel(), host.CommandSink, host.LinkValidator,
                              host.TypeSystem, host.NodeCatalog, host),
                Kind.ToString())
            { Commands = new EditorCommandsImpl() };

            doc.ViewState     = ctx;
            Store.ActiveAsset = asset;
            return doc;
        }
    }

    // ── entries ─────────────────────────────────────────────────────────────

    private static BlackboardVariableEntry Var(string n) => new(n, typeof(float), null);

    private static BlackboardVariableEntry State(string n)
        => new(n, typeof(int), null, Role: BlackboardVariableRole.State, Scope: WorkingStateScope.Node);

    // ── stubs ───────────────────────────────────────────────────────────────

    private sealed class NoRenderSeam : ICanvasRenderSeam
    {
        public void Render(GraphView view) { }
    }

    private sealed class StubGraphModel : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "Stub";
        public GraphKindDescriptor Kind => new("stub", "Stub", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => null;
        public IPinModel?  FindPin(PinId id)   => null;
        public ILinkModel? FindLink(LinkId id) => null;
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
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) =>
            Array.Empty<NodeCatalogEntry>();
    }

    /// <summary>⭐ One instance per document, exactly as a real host services bag is.</summary>
    private sealed class StubHostServices : IEditorHostServices
    {
        private readonly StubCommandSink _cmd = new();
        private readonly StubValidator   _val = new();
        private readonly StubTypeSystem  _ts  = new();
        private readonly StubNodeCatalog _cat = new();

        public INodeCatalog      NodeCatalog   => _cat;
        public ITypeSystem       TypeSystem    => _ts;
        public ILinkValidator    LinkValidator => _val;
        public IGraphCommandSink CommandSink   => _cmd;
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers =>
            Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    private sealed class FakeAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        private FakeAsset(AssetKind kind, IEnumerable<BlackboardVariableEntry> vars)
        { Kind = kind; _vars = vars.ToList(); }

        public static FakeAsset With(AssetKind kind, params BlackboardVariableEntry[] vars)
            => new(kind, vars);

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "FakeAsset";
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    private sealed class StubRefactor : IRefactorService
    {
        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k) => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id) => Array.Empty<AssetReferenceInfo>();
        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o) =>
            new(f, t, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyRename(RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o) =>
            new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
