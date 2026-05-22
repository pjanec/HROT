using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Mutable comment model.</summary>
public sealed class FakeCommentModel : ICommentModel
{
    public CommentId Id       { get; }
    public string    Text     { get; set; }
    public Vector2   Position { get; set; }
    public Vector2   Size     { get; set; }
    public Vector4   Color    { get; set; } = new Vector4(0.4f, 0.6f, 1.0f, 1.0f);
    public int       ZOrder   { get; set; }
    public bool      MoveWithContents { get; set; } = true;

    public FakeCommentModel(CommentId id, string text, Vector2 position, Vector2 size)
    {
        Id       = id;
        Text     = text;
        Position = position;
        Size     = size;
    }
}
