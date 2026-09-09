using System.Numerics;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Tests for Unreal-style comment box support: <see cref="BlueprintGraphModel"/> projecting
/// <see cref="GraphComment"/> onto <c>ICommentModel</c>, <see cref="BlueprintCommandSink"/>
/// handling <c>GraphCommand.AddComment/UpdateComment/RemoveComment</c>, and the mandatory
/// save/reload round-trip guard (mirrors <c>BlueprintRerouteTests</c>'s waypoint round-trip).
/// </summary>
public sealed class BlueprintCommentTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static (BlueprintAsset asset, Graph graph) BuildEmptyGraph()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("cc000000-0000-0000-0000-000000000001"),
            Name     = "CommentTestAsset",
            Dispatch = BlueprintDispatchKind.Library,
        };

        var graph = new Graph
        {
            Id   = new Guid("cc000000-0005-0005-0005-000000000005"),
            Name = "Main",
            Kind = GraphKind.Function,
        };
        asset.Graphs.Add(graph);

        return (asset, graph);
    }

    private static (BlueprintCommandSink sink,
                    BlueprintGraphModel  model,
                    List<BlueprintAsset> dirtyLog)
        MakeSink(BlueprintAsset asset, Graph graph)
    {
        var typeSystem  = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model       = new BlueprintGraphModel(asset, graph);
        var catalog     = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator   = new BlueprintLinkValidator(model, typeSystem);
        var history     = new CommandHistory();
        var dirtyLog    = new List<BlueprintAsset>();
        var editService = new EditService
        {
            Context = new EditServiceContext(history, a => dirtyLog.Add(a))
        };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editService,
            markDirty: a => dirtyLog.Add(a));

        return (sink, model, dirtyLog);
    }

    // ── BlueprintGraphModel projection ─────────────────────────────────────────

    [Fact]
    public void BlueprintGraphModel_ProjectsAssetComment_IntoComments()
    {
        var (asset, graph) = BuildEmptyGraph();
        var commentId = new Guid("cc000000-0010-0010-0010-000000000010");
        graph.Comments.Add(new GraphComment
        {
            Id     = commentId,
            Text   = "Hello world",
            X      = 10f,
            Y      = 20f,
            W      = 200f,
            H      = 100f,
            ColorR = 0.1f,
            ColorG = 0.2f,
            ColorB = 0.3f,
            ColorA = 1f,
            ZOrder = 3,
            MoveWithContents = false,
        });

        var model = new BlueprintGraphModel(asset, graph);

        Assert.Single(model.Comments);
        var projected = model.FindComment(new CommentId(commentId));
        Assert.NotNull(projected);
        Assert.Equal("Hello world", projected!.Text);
        Assert.Equal(new Vector2(10f, 20f), projected.Position);
        Assert.Equal(new Vector2(200f, 100f), projected.Size);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1f), projected.Color);
        Assert.Equal(3, projected.ZOrder);
        Assert.False(projected.MoveWithContents);
    }

    [Fact]
    public void BlueprintGraphModel_NoComments_EmptyCollection()
    {
        var (asset, graph) = BuildEmptyGraph();
        var model = new BlueprintGraphModel(asset, graph);

        Assert.Empty(model.Comments);
    }

    // ── AddComment ────────────────────────────────────────────────────────────

    [Fact]
    public void AddComment_AddsGraphCommentToAsset()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, model, dirtyLog) = MakeSink(asset, graph);
        var commentId = new CommentId(Guid.NewGuid());

        var result = sink.Apply(new GraphCommand.AddComment(
            commentId, "New Comment", new Vector2(1f, 2f), new Vector2(300f, 150f),
            new Vector4(0.29f, 0.56f, 0.88f, 1f), true));

        Assert.True(result.Success);
        Assert.Single(graph.Comments);
        Assert.Equal(commentId.Value, graph.Comments[0].Id);
        Assert.Equal("New Comment", graph.Comments[0].Text);
        Assert.Contains(asset, dirtyLog);

        var projected = model.FindComment(commentId);
        Assert.NotNull(projected);
        Assert.Equal("New Comment", projected!.Text);
    }

    // ── UpdateComment ─────────────────────────────────────────────────────────

    [Fact]
    public void UpdateComment_RenameOnly_LeavesOtherFieldsUntouched()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, _) = MakeSink(asset, graph);
        var commentId = new CommentId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddComment(
            commentId, "Original", new Vector2(5f, 5f), new Vector2(100f, 100f),
            new Vector4(1f, 1f, 1f, 1f), true));

        var result = sink.Apply(new GraphCommand.UpdateComment(
            commentId, "Renamed", null, null, null, null, null));

        Assert.True(result.Success);
        var comment = graph.Comments.Single(c => c.Id == commentId.Value);
        Assert.Equal("Renamed", comment.Text);
        Assert.Equal(5f, comment.X);
        Assert.Equal(5f, comment.Y);
        Assert.Equal(100f, comment.W);
        Assert.Equal(100f, comment.H);
        Assert.True(comment.MoveWithContents);
    }

    [Fact]
    public void UpdateComment_MoveAndResize_UpdatesRect()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, _) = MakeSink(asset, graph);
        var commentId = new CommentId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddComment(
            commentId, "C", new Vector2(0f, 0f), new Vector2(50f, 50f),
            new Vector4(1f, 1f, 1f, 1f), true));

        sink.Apply(new GraphCommand.UpdateComment(
            commentId, null, new Vector2(40f, 60f), new Vector2(300f, 250f), null, null, null));

        var comment = graph.Comments.Single(c => c.Id == commentId.Value);
        Assert.Equal(40f, comment.X);
        Assert.Equal(60f, comment.Y);
        Assert.Equal(300f, comment.W);
        Assert.Equal(250f, comment.H);
    }

    [Fact]
    public void UpdateComment_ZOrderAndColor_ApplyIndependently()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, _) = MakeSink(asset, graph);
        var commentId = new CommentId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddComment(
            commentId, "C", Vector2.Zero, new Vector2(10f, 10f),
            new Vector4(0f, 0f, 0f, 1f), true));

        sink.Apply(new GraphCommand.UpdateComment(
            commentId, null, null, null, new Vector4(0.8f, 0.1f, 0.1f, 1f), 7, false));

        var comment = graph.Comments.Single(c => c.Id == commentId.Value);
        Assert.Equal(7, comment.ZOrder);
        Assert.False(comment.MoveWithContents);
        Assert.Equal(0.8f, comment.ColorR, precision: 3);
        Assert.Equal(0.1f, comment.ColorG, precision: 3);
        Assert.Equal(0.1f, comment.ColorB, precision: 3);
    }

    [Fact]
    public void UpdateComment_UnknownId_SafeNoOp()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.UpdateComment(
            new CommentId(Guid.NewGuid()), "x", null, null, null, null, null));

        Assert.True(result.Success);
        Assert.Empty(dirtyLog);
    }

    // ── RemoveComment ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveComment_ExistingId_RemovesFromAsset()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, model, _) = MakeSink(asset, graph);
        var commentId = new CommentId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddComment(
            commentId, "C", Vector2.Zero, new Vector2(10f, 10f),
            new Vector4(0f, 0f, 0f, 1f), true));

        var result = sink.Apply(new GraphCommand.RemoveComment(commentId));

        Assert.True(result.Success);
        Assert.Empty(graph.Comments);
        Assert.Null(model.FindComment(commentId));
    }

    [Fact]
    public void RemoveComment_UnknownId_SafeNoOp()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, dirtyLog) = MakeSink(asset, graph);

        var result = sink.Apply(new GraphCommand.RemoveComment(new CommentId(Guid.NewGuid())));

        Assert.True(result.Success);
        Assert.Empty(dirtyLog);
    }

    // ── Undo/redo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// BP-11: undo for a comment comes from the editor's <see cref="UndoStack"/>, not from the sink.
    /// The sink is the <em>applier</em>; the stack is the recorder, and the caller
    /// (<c>CanvasCommands.AddComment</c>) supplies the inverse. This test used to drive
    /// <c>CommandHistory.Undo()</c> — a stack nothing in the UI reached, which is precisely what
    /// BP-11 removed. Having the sink record as well would push a second entry per gesture and, on
    /// undo, a third when the inverse landed back in the same method.
    /// </summary>
    [Fact]
    public void AddComment_Undo_RemovesComment()
    {
        var (asset, graph) = BuildEmptyGraph();
        var (sink, _, _)   = MakeSink(asset, graph);
        var undo = new UndoStack(sink);

        var commentId = new CommentId(Guid.NewGuid());
        undo.ApplyAndRecord(
            new GraphCommand.AddComment(
                commentId, "C", Vector2.Zero, new Vector2(10f, 10f),
                new Vector4(0f, 0f, 0f, 1f), true),
            new GraphCommand.RemoveComment(commentId),
            "Add Comment");
        Assert.Single(graph.Comments);

        Assert.True(undo.Undo());

        Assert.Empty(graph.Comments);
    }

    // ── Round-trip (MANDATORY) ────────────────────────────────────────────────

    /// <summary>
    /// MANDATORY ROUND-TRIP GUARD: serializes a BlueprintAsset carrying a comment box through the
    /// real <see cref="BlueprintJsonServices"/>, deserializes it, and asserts every field survived.
    /// Mirrors <c>BlueprintRerouteTests.LinkWaypoints_SerializeAndDeserialize_...</c>: proves
    /// <see cref="GraphComment"/>'s flat float X/Y/W/H/Color* PROPERTIES round-trip (unlike a raw
    /// <see cref="Vector2"/>/<see cref="Vector4"/>, whose components are FIELDS).
    /// </summary>
    [Fact]
    public void GraphComment_SerializeAndDeserialize_SurvivesRoundTrip()
    {
        var commentId = new Guid("dd000000-0001-0001-0001-000000000001");
        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("dd000000-0000-0000-0000-000000000001"),
            Name     = "CommentRoundTrip",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   =
            [
                new Graph
                {
                    Id       = new Guid("dd000000-0099-0001-0001-000000000001"),
                    Name     = "Main",
                    Kind     = GraphKind.Function,
                    Comments =
                    [
                        new GraphComment
                        {
                            Id     = commentId,
                            Text   = "Round trip me",
                            X      = 12.5f,
                            Y      = 34.25f,
                            W      = 400f,
                            H      = 220f,
                            ColorR = 0.29f,
                            ColorG = 0.56f,
                            ColorB = 0.88f,
                            ColorA = 1f,
                            ZOrder = 5,
                            MoveWithContents = false,
                        },
                    ],
                },
            ],
        };

        var json         = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(deserialized);
        var deserializedComment = deserialized!.Graphs[0].Comments.Single();
        Assert.Equal(commentId, deserializedComment.Id);
        Assert.Equal("Round trip me", deserializedComment.Text);
        Assert.Equal(12.5f, deserializedComment.X, precision: 3);
        Assert.Equal(34.25f, deserializedComment.Y, precision: 3);
        Assert.Equal(400f, deserializedComment.W, precision: 3);
        Assert.Equal(220f, deserializedComment.H, precision: 3);
        Assert.Equal(0.29f, deserializedComment.ColorR, precision: 3);
        Assert.Equal(0.56f, deserializedComment.ColorG, precision: 3);
        Assert.Equal(0.88f, deserializedComment.ColorB, precision: 3);
        Assert.Equal(1f, deserializedComment.ColorA, precision: 3);
        Assert.Equal(5, deserializedComment.ZOrder);
        Assert.False(deserializedComment.MoveWithContents);

        // Byte-stable: re-serialized JSON must equal the first serialization.
        var json2 = BlueprintJsonServices.Serialize(deserialized);
        Assert.Equal(json, json2);

        // Also verify the round-tripped asset projects correctly via BlueprintGraphModel.
        var model = new BlueprintGraphModel(deserialized, deserialized.Graphs[0]);
        Assert.Single(model.Comments);
        var projected = model.FindComment(new CommentId(commentId));
        Assert.NotNull(projected);
        Assert.Equal("Round trip me", projected!.Text);
    }

    /// <summary>A graph WITHOUT comments must still round-trip cleanly (empty list, not omitted).</summary>
    [Fact]
    public void GraphWithoutComments_RoundTrip_CommentsStillEmpty()
    {
        var (asset, _) = BuildEmptyGraph();

        var json         = BlueprintJsonServices.Serialize(asset);
        var deserialized = BlueprintJsonServices.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.Graphs[0].Comments);
        Assert.Empty(deserialized.Graphs[0].Comments);
    }
}
