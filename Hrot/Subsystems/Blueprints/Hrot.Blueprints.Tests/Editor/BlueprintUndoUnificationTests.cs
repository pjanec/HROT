using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-11 — one undo stack for property edits and structural edits alike.
///
/// <para>
/// Before this, the editor had three tiers: canvas edits recorded on NodeEdit's <see cref="UndoStack"/>
/// and were reachable by Ctrl+Z; <c>BlueprintCommandSink</c> property edits recorded on
/// <see cref="CommandHistory"/>, whose <c>Undo</c> had <b>zero non-test callers</b>; and drawer edits
/// recorded nothing at all. The unit tests of the day all drove <c>history.Undo()</c> directly — they
/// proved the stack worked, never that anything reached it. Same failure shape as BP-29.
/// </para>
///
/// <para>
/// These tests assert the property the old ones could not: that an edit made the way a designer makes
/// it is reversed by the undo the editor actually runs.
/// </para>
/// </summary>
public sealed class BlueprintUndoUnificationTests
{
    private const string HealthFqn = "Hrot.Blueprints.Tests.Editor.BlueprintUndoUnificationTests+HealthTestComponent";

    private struct HealthTestComponent
    {
        public int Current;
        public int Max;
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the production wiring in miniature: a real <see cref="BlueprintCommandSink"/>, a real
    /// <see cref="UndoStack"/> over it, and a real <see cref="EditService"/> whose Q22-C2 transport
    /// points at that stack — exactly the closure <c>BlueprintDocumentFactory</c> installs, minus the
    /// ImGui-bound <c>GraphView</c> that merely forwards to <c>Undo.ApplyAndRecord</c>.
    /// </summary>
    private sealed class Harness
    {
        public BlueprintAsset       Asset       { get; }
        public Graph                Graph       { get; }
        public BlueprintCommandSink Sink        { get; }
        public UndoStack            Undo        { get; }
        public EditService          EditService { get; }
        public List<BlueprintAsset> DirtyLog    { get; } = new();
        public int                  StructureChangedCount;

        public Harness()
        {
            Asset = BlueprintAssetBuilder.Instance("UndoUnificationAsset")
                .WithGraph("EventGraph", GraphKind.Event, _ => { })
                .Build();
            Graph = Asset.Graphs[0];

            var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
            var model      = new BlueprintGraphModel(Asset, Graph);
            var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry());
            var validator  = new BlueprintLinkValidator(model, typeSystem);
            var history    = new CommandHistory();

            EditService = new EditService();
            Sink = new BlueprintCommandSink(
                Asset, Graph, model, catalog, validator, history, EditService,
                markDirty: a => DirtyLog.Add(a));

            Undo = new UndoStack(Sink);

            EditService.Context = new EditServiceContext(
                history,
                markDirty: a => DirtyLog.Add(a),
                onStructureChanged: _ => StructureChangedCount++,
                // The transport GraphView.Execute delegates to, verbatim.
                recordUndoable: (label, apply, undo) => Undo.ApplyAndRecord(
                    new BlueprintEditCommand(label, apply),
                    new BlueprintEditCommand(label, undo),
                    label));
        }

        public GetComponentNodeSession NewComponentSession(out GetComponentNode node)
        {
            node = new GetComponentNode { Id = Guid.NewGuid() };
            Graph.Nodes.Add(node);
            var drawer = new GetComponentNodeDrawer(EditService, new FixedTypeProvider(HealthFqn));
            return (GetComponentNodeSession)drawer.CreateSession(node, Asset);
        }

        public GetSharedNodeSession NewSharedSession(out GetSharedNode node)
        {
            node = new GetSharedNode { Id = Guid.NewGuid() };
            Graph.Nodes.Add(node);
            var drawer = new GetSharedNodeDrawer(EditService, new FixedTypeProvider(HealthFqn));
            return (GetSharedNodeSession)drawer.CreateSession(node, Asset);
        }
    }

    private sealed class FixedTypeProvider : IComponentTypeProvider, ISharedStructTypeProvider
    {
        private readonly string[] _fqns;
        public FixedTypeProvider(params string[] fqns) => _fqns = fqns;
        public IReadOnlyList<string> GetComponentTypeFqns()    => _fqns;
        public IReadOnlyList<string> GetSharedStructTypeFqns() => _fqns;
    }

    // ── 1. The assertion no test used to make ────────────────────────────────

    /// <summary>
    /// The headline: a drawer edit, then the undo the editor actually runs, restores the prior value.
    /// </summary>
    [Fact]
    public void DrawerEdit_IsReversedBy_TheEditorsUndo()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out var node);

        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.Equal(HealthFqn, node.ComponentTypeFqn);

        Assert.True(h.Undo.Undo(), "the drawer edit must be on the stack Ctrl+Z pops");

        Assert.NotEqual(HealthFqn, node.ComponentTypeFqn);
    }

    /// <summary>
    /// The whole multi-field bake is one entry, and undo reverses <em>all</em> of it — not just the
    /// field a single-key <c>SetNodeProperty</c> could have carried.
    /// </summary>
    [Fact]
    public void DrawerEdit_Undo_RestoresEveryFieldOfTheBake()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out var node);

        var fieldsBefore    = node.Fields;
        var isManagedBefore = node.IsManaged;

        session.SetComponentTypeFqnForTest(HealthFqn);
        Assert.NotNull(node.Fields);          // the bake produced per-field decls

        h.Undo.Undo();

        Assert.Equal(fieldsBefore,    node.Fields);
        Assert.Equal(isManagedBefore, node.IsManaged);
        Assert.Equal("",              node.ComponentTypeFqn);
    }

    [Fact]
    public void DrawerEdit_Redo_ReappliesTheBake()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out var node);

        session.SetComponentTypeFqnForTest(HealthFqn);
        h.Undo.Undo();
        Assert.True(h.Undo.Redo());

        Assert.Equal(HealthFqn, node.ComponentTypeFqn);
        Assert.NotNull(node.Fields);
    }

    // ── 2. One gesture, one entry ────────────────────────────────────────────

    [Fact]
    public void DrawerEdit_PushesExactlyOneUndoEntry()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out _);

        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.Equal(1, h.Undo.UndoCount);
    }

    /// <summary>
    /// Regression guard for the re-entrancy hazard this work uncovered: the sink used to record a
    /// property edit of its own while applying a command the stack had <em>already</em> recorded.
    /// Once both stacks were live that meant two entries per gesture — and on undo the inverse would
    /// land back in the same sink method and push a third. The sink applies; the stack records.
    /// </summary>
    [Fact]
    public void SinkAppliedCommand_PushesExactlyOneUndoEntry_NotTwo()
    {
        var h         = new Harness();
        var commentId = new CommentId(Guid.NewGuid());

        h.Undo.ApplyAndRecord(
            new GraphCommand.AddComment(
                commentId, "C", Vector2.Zero, new Vector2(10f, 10f),
                new Vector4(0f, 0f, 0f, 1f), true),
            new GraphCommand.RemoveComment(commentId),
            "Add Comment");

        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Single(h.Graph.Comments);

        Assert.True(h.Undo.Undo());
        Assert.Empty(h.Graph.Comments);
        Assert.Equal(0, h.Undo.UndoCount);
    }

    // ── 3. Mixed ordering — the entire reason for one stack (Q22-A1) ─────────

    /// <summary>
    /// A canvas edit and a drawer edit interleaved must undo in reverse chronological order. This is
    /// the property two stacks could not have without a global sequence number, and it is why A1 was
    /// chosen over A3's coordinator.
    /// </summary>
    [Fact]
    public void StructuralAndPropertyEdits_UndoInReverseChronologicalOrder()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out var node);

        // 1. property edit (drawer)
        session.SetComponentTypeFqnForTest(HealthFqn);

        // 2. structural edit (canvas)
        var commentId = new CommentId(Guid.NewGuid());
        h.Undo.ApplyAndRecord(
            new GraphCommand.AddComment(
                commentId, "C", Vector2.Zero, new Vector2(8f, 8f),
                new Vector4(1f, 1f, 1f, 1f), true),
            new GraphCommand.RemoveComment(commentId),
            "Add Comment");

        Assert.Equal(2, h.Undo.UndoCount);

        // The comment came last, so it goes first.
        h.Undo.Undo();
        Assert.Empty(h.Graph.Comments);
        Assert.Equal(HealthFqn, node.ComponentTypeFqn);

        h.Undo.Undo();
        Assert.NotEqual(HealthFqn, node.ComponentTypeFqn);
    }

    // ── 4. Every converted drawer reaches the stack ──────────────────────────

    [Fact]
    public void SharedNodeDrawer_SlotNameEdit_IsUndoable()
    {
        var h       = new Harness();
        var session = h.NewSharedSession(out var node);

        session.SetVariableIdForTest("rallyPoint");
        Assert.Equal("rallyPoint", node.VariableId);

        h.Undo.Undo();

        Assert.NotEqual("rallyPoint", node.VariableId);
    }

    /// <summary>A drawer edit still marks the asset dirty — recording performs the edit.</summary>
    [Fact]
    public void DrawerEdit_MarksTheAssetDirty()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out _);

        session.SetComponentTypeFqnForTest(HealthFqn);

        Assert.Contains(h.Asset, h.DirtyLog);
    }

    /// <summary>Undo re-projects the canvas, or the reverted node would keep its stale pins.</summary>
    [Fact]
    public void Undo_NotifiesStructureChanged_SoDerivedViewsReproject()
    {
        var h       = new Harness();
        var session = h.NewComponentSession(out _);

        session.SetComponentTypeFqnForTest(HealthFqn);
        int afterEdit = h.StructureChangedCount;

        h.Undo.Undo();

        Assert.True(h.StructureChangedCount > afterEdit,
            "undoing a pin-shape change must re-project the derived views too");
    }

    // ── 5. The silent-default trap (Q22 addendum gap 3) ──────────────────────

    /// <summary>
    /// <c>BlueprintCommandSink.Apply</c>'s <c>default:</c> arm returns <b>success</b> for unknown
    /// commands. Without an explicit <see cref="BlueprintEditCommand"/> case every undo would no-op
    /// while reporting that it worked — the exact failure class BP-11 removes. Asserting on the
    /// result alone cannot catch that, so this asserts the delegate ran.
    /// </summary>
    [Fact]
    public void Sink_HasAnExplicitCaseFor_BlueprintEditCommand()
    {
        var h   = new Harness();
        bool ran = false;

        var result = h.Sink.Apply(new BlueprintEditCommand("probe", () => ran = true));

        Assert.True(result.Success);
        Assert.True(ran,
            "the sink must run the carried mutation — a missing case would silently return success");
    }

    [Fact]
    public void EditService_WithoutATransport_FallsBackToApplyingTheEdit()
    {
        // No Context at all: the edit must still happen rather than being dropped.
        var svc   = new EditService();
        var asset = BlueprintAssetBuilder.Instance("NoContext").Build();
        int value = 0;

        svc.RecordPropertyEdit(asset, "set", apply: () => value = 7, undo: () => value = 0);

        Assert.Equal(7, value);
    }
}
