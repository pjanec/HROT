using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Transform;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-76 — <c>Expand Node</c>, and the corrupting path it replaces.
///
/// <para>
/// ⭐⭐ <b>The row read as "two boolean expressions are wrong".</b> It was not. The greyed gate was the
/// only thing keeping a corrupting path unreachable: the forward reached
/// <c>BlueprintCommandSink</c>'s <c>default:</c> arm and reported success while doing nothing, and
/// the shared menu paired it with an "exact inverse" that removed two node ids predicted from
/// <b>the NodeEdit demo's fake backend</b> (<c>{node}_exp1</c>/<c>_exp2</c>). Ungating it alone would
/// have shipped an undo that deletes nodes the designer never asked about.
/// </para>
///
/// <para>
/// ⭐ The round-trip runs the other way now: Batch 33 locked <b>collapse → expand → equal</b>; this
/// locks <b>expand → collapse → equal</b>, with the same comparator already proven non-vacuous.
/// </para>
/// </summary>
public sealed class ExpandNodeTests
{
    // ── fixture ─────────────────────────────────────────────────────────────

    private sealed class CapturingIndicators : IEditorIndicators
    {
        public List<EditorNotification> Notifications { get; } = new();
        public EditorStatusSnapshot Snapshot => default;
        public event Action? Changed { add { } remove { } }
        public void Notify(EditorNotification n) => Notifications.Add(n);
    }

    private sealed record Sut(
        EditorCommandsImpl Commands, GraphView View, BlueprintAsset Asset, Graph Host,
        BlueprintCommandSink Sink, CapturingIndicators Indicators,
        MacroCallNode Call, Node Entry, Node Return, List<Guid> SwitchedTo);

    private static Pin P(string name, string dir, bool isExec) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = new BlueprintTypeRef(),
    };

    private static Link W(Node f, Pin fp, Node t, Pin tp) => new()
    {
        FromNodeId = f.Id, FromPinId = fp.Id, ToNodeId = t.Id, ToPinId = tp.Id,
    };

    /// <summary>Host: Entry → call(macro) → Return. Macro body: Entry′ → Body → Return′.</summary>
    private static Sut MakeSut()
    {
        // ── the macro ──
        var mEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var mOut   = P("Out", "Out", true); mEntry.Pins.Add(mOut);
        var body   = new PrintStringNode { Id = Guid.NewGuid() };
        var bIn    = P("In", "In", true); var bOut = P("Out", "Out", true);
        body.Pins.AddRange(new[] { bIn, bOut });
        var mRet   = new ReturnNode { Id = Guid.NewGuid() };
        var mRetIn = P("In", "In", true); mRet.Pins.Add(mRetIn);

        var macro = new Graph
        {
            Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro,
            Nodes = { mEntry, body, mRet },
            Links = { W(mEntry, mOut, body, bIn), W(body, bOut, mRet, mRetIn) },
        };

        // ── the host ──
        var hEntry = new EventEntryNode { Id = Guid.NewGuid() };
        var hOut   = P("Out", "Out", true); hEntry.Pins.Add(hOut);
        var call   = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        var cIn    = P("In", "In", true); var cOut = P("Out", "Out", true);
        call.Pins.AddRange(new[] { cIn, cOut });
        var hRet   = new ReturnNode { Id = Guid.NewGuid() };
        var hRetIn = P("In", "In", true); hRet.Pins.Add(hRetIn);

        var host = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { hEntry, call, hRet },
            Links = { W(hEntry, hOut, call, cIn), W(call, cOut, hRet, hRetIn) },
        };

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ExpandAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
            Graphs = { host, macro },
        };

        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, host);
        var catalog    = new BlueprintNodeCatalog(registry) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };
        var sink = new BlueprintCommandSink(
            asset, host, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var hostServices = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, hostServices.CommandSink, hostServices.LinkValidator,
            hostServices.TypeSystem, hostServices.NodeCatalog, hostServices);

        var indicators = new CapturingIndicators();
        var switched   = new List<Guid>();
        var commands   = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterExpandCommands(
            commands, view, asset, () => host, goToGraph: switched.Add, indicators);

        return new Sut(commands, view, asset, host, sink, indicators, call, hEntry, hRet, switched);
    }

    private static void Select(Sut sut, params Node[] nodes)
    {
        sut.View.Selection.Clear();
        foreach (var n in nodes) sut.View.Selection.Add(SelectionEntry.OfNode(new NodeId(n.Id)));
    }

    // ────────────────────────────────────────────────────────────────────────
    // The silent-success guard
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The test that would have caught the <c>default:</c> arm.</b> Asserted on the GRAPH: before
    /// this batch the command returned <c>Success = true</c> with nothing changed, so a
    /// <c>Assert.True(result.Success)</c>-shaped test would have been green on the defect.
    /// </summary>
    [Fact]
    public void Sink_ExpandNode_ActuallySplicesTheBody()
    {
        var sut = MakeSut();

        var result = sut.Sink.Apply(new GraphCommand.ExpandNode(new NodeId(sut.Call.Id)));

        Assert.True(result.Success, result.Message);
        Assert.Empty(sut.Host.Nodes.OfType<MacroCallNode>());          // the call is gone…
        Assert.Single(sut.Host.Nodes.OfType<PrintStringNode>());       // …and the body is here
        // The exec chain is continuous: the host entry now reaches the spliced body directly.
        var spliced = sut.Host.Nodes.OfType<PrintStringNode>().Single();
        Assert.Contains(sut.Host.Links, l => l.FromNodeId == sut.Entry.Id && l.ToNodeId == spliced.Id);
        Assert.Contains(sut.Host.Links, l => l.FromNodeId == spliced.Id && l.ToNodeId == sut.Return.Id);
    }

    /// <summary>⚠ A refusal is a FAILED result carrying a reason, not a quiet success.</summary>
    [Fact]
    public void Sink_ExpandingANonMacroCall_FailsAndSaysWhy()
    {
        var sut = MakeSut();

        var result = sut.Sink.Apply(new GraphCommand.ExpandNode(new NodeId(sut.Entry.Id)));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Single(sut.Host.Nodes.OfType<MacroCallNode>());   // untouched
    }

    [Fact]
    public void Sink_ExpandingACallWithNoResolvableTarget_Fails()
    {
        var sut = MakeSut();
        sut.Call.TargetGraphId = Guid.NewGuid().ToString();   // points at nothing

        var result = sut.Sink.Apply(new GraphCommand.ExpandNode(new NodeId(sut.Call.Id)));

        Assert.False(result.Success);
        Assert.Single(sut.Host.Nodes.OfType<MacroCallNode>());
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The round-trip, the other way
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>expand → collapse → canonically equal.</b> Batch 33 locked the mirror of this
    /// (collapse → expand). Together they say the two transforms are inverses in both directions,
    /// which neither statement gives alone — and <c>CanonicalGraphShape</c> was built as a reusable
    /// comparator precisely so this would cost nothing.
    /// </summary>
    [Fact]
    public void ExpandThenCollapse_IsCanonicallyEqual()
    {
        var sut = MakeSut();
        var before = CanonicalGraphShape.Describe(sut.Host);

        sut.Sink.Apply(new GraphCommand.ExpandNode(new NodeId(sut.Call.Id)));

        // Collapse exactly what the expansion spliced in — the body node.
        var spliced  = sut.Host.Nodes.OfType<PrintStringNode>().Single();
        var analysis = CollapseAnalysis.Analyse(
            sut.Host, new[] { spliced.Id }, CollapseTarget.Macro);
        Assert.False(analysis.IsRefused,
            string.Join("; ", analysis.Refusals.Select(r => r.Code)));

        var edit = CollapseEmitter.Emit(sut.Host, analysis.Plan!, CollapseTarget.Macro, "AimFire2");

        Assert.Equal(before, CanonicalGraphShape.Describe(edit.RewrittenHost));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Undo — identity, not shape
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>One entry, and the call node comes back with its ORIGINAL node and pin ids.</b> The
    /// inverse that shipped predicted <c>_exp1</c>/<c>_exp2</c>; a snapshot cannot be wrong about what
    /// it captured. Pin ids matter because <c>DeterministicIds.PinId</c> derives from the node id, so
    /// breakpoints and debug-map entries follow them.
    /// </summary>
    [Fact]
    public void Command_Undo_IsOneEntry_AndRestoresTheCallNodesIdentity()
    {
        var sut = MakeSut();
        var beforeShape = CanonicalGraphShape.Describe(sut.Host);
        var callPinIds  = sut.Call.Pins.Select(p => p.Id).OrderBy(x => x).ToList();

        Select(sut, sut.Call);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.ExpandNode);

        Assert.Equal(1, sut.View.Undo.UndoCount);
        Assert.Empty(sut.Host.Nodes.OfType<MacroCallNode>());

        Assert.True(sut.View.Undo.Undo());

        Assert.Equal(beforeShape, CanonicalGraphShape.Describe(sut.Host));
        var restored = Assert.Single(sut.Host.Nodes.OfType<MacroCallNode>());
        Assert.Same(sut.Call, restored);                                  // ⭐ the object itself
        Assert.Equal(callPinIds, restored.Pins.Select(p => p.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public void Command_Redo_ReturnsToTheExpandedState()
    {
        var sut = MakeSut();
        Select(sut, sut.Call);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.ExpandNode);
        var expanded = CanonicalGraphShape.Describe(sut.Host);

        sut.View.Undo.Undo();
        Assert.True(sut.View.Undo.Redo());

        Assert.Equal(expanded, CanonicalGraphShape.Describe(sut.Host));
    }

    /// <summary>
    /// ⚠ <c>Pin.LinkedToIds</c> is a denormalised mirror; undo restores node objects the forward
    /// rewrote it on. Same failure this programme has now hit three times.
    /// </summary>
    [Fact]
    public void Command_Undo_RebuildsTheLinkedToIdMirror()
    {
        var sut = MakeSut();
        Select(sut, sut.Call);
        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.ExpandNode);
        sut.View.Undo.Undo();

        var live = new HashSet<Guid>(sut.Host.Nodes.SelectMany(n => n.Pins).Select(p => p.Id));
        foreach (var pin in sut.Host.Nodes.SelectMany(n => n.Pins))
            foreach (var linked in pin.LinkedToIds)
                Assert.Contains(linked, live);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Q26-B2 — offered, and refusing out loud
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The item stays ENABLED for a node that cannot be expanded, and refuses on invoke. Greying it
    /// is what produced BP-76 in the first place, and it would put blueprint vocabulary back into
    /// shared UI.
    /// </summary>
    [Fact]
    public void Command_IsEnabled_ForANodeThatCannotBeExpanded_AndRefusesOnInvoke()
    {
        var sut = MakeSut();
        Select(sut, sut.Entry);

        Assert.True(sut.Commands.Get(NodeEditor.Core.CommandCatalog.ExpandNode)!.IsEnabled());

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.ExpandNode);

        Assert.Single(sut.Host.Nodes.OfType<MacroCallNode>());       // nothing happened…
        var n = Assert.Single(sut.Indicators.Notifications);          // …and it said so
        Assert.Equal(MacroExpander.RefusalCodes.NotAMacroCall, n.Id);
    }

    [Fact]
    public void Command_IsDisabled_WhenTheSelectionIsNotExactlyOneNode()
    {
        var sut = MakeSut();
        sut.View.Selection.Clear();
        Assert.False(sut.Commands.Get(NodeEditor.Core.CommandCatalog.ExpandNode)!.IsEnabled());

        Select(sut, sut.Entry, sut.Return);
        Assert.False(sut.Commands.Get(NodeEditor.Core.CommandCatalog.ExpandNode)!.IsEnabled());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Go to Definition
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GoToDefinition_OnAMacroCall_NavigatesToTheMacroGraph()
    {
        var sut = MakeSut();
        Select(sut, sut.Call);

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.GoToDefinition);

        Assert.Equal(Guid.Parse(sut.Call.TargetGraphId), Assert.Single(sut.SwitchedTo));
    }

    [Fact]
    public void GoToDefinition_OnAFunctionGraphCall_NavigatesToTheTargetGraph()
    {
        var sut = MakeSut();
        var callee = new Graph { Id = Guid.NewGuid(), Name = "Helper", Kind = GraphKind.Function };
        sut.Asset.Graphs.Add(callee);
        var fc = new FunctionCallNode { Id = Guid.NewGuid(), TargetGraphId = callee.Id.ToString() };
        sut.Host.Nodes.Add(fc);
        Select(sut, fc);

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.GoToDefinition);

        Assert.Equal(callee.Id, Assert.Single(sut.SwitchedTo));
    }

    /// <summary>
    /// ⚠ A <c>CallCustomEvent</c> resolves by <b>name</b>, not by a graph id, and My Blueprint already
    /// navigates to a custom event's body on double-click. Re-deriving that pairing here would be a
    /// second copy of a rule the compiler also holds (<c>Event_{Name}</c>), so it refuses and says
    /// where to go instead of navigating somewhere plausible.
    /// </summary>
    [Fact]
    public void GoToDefinition_OnACustomEventCall_RefusesAndPointsAtMyBlueprint()
    {
        var sut = MakeSut();
        var ce = new CallCustomEventNode { Id = Guid.NewGuid() };
        sut.Host.Nodes.Add(ce);
        Select(sut, ce);

        sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.GoToDefinition);

        Assert.Empty(sut.SwitchedTo);
        var n = Assert.Single(sut.Indicators.Notifications);
        Assert.Contains("My Blueprint", n.Body);
    }

    private sealed class StubHostServices : IEditorHostServices
    {
        public StubHostServices(INodeCatalog catalog, ITypeSystem typeSystem,
            ILinkValidator validator, IGraphCommandSink sink)
        {
            NodeCatalog = catalog; TypeSystem = typeSystem; LinkValidator = validator; CommandSink = sink;
        }

        public INodeCatalog      NodeCatalog   { get; }
        public ITypeSystem       TypeSystem    { get; }
        public ILinkValidator    LinkValidator { get; }
        public IGraphCommandSink CommandSink   { get; }
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
