using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a comment box.</summary>
public interface ICommentModel
{
    /// <summary>Stable id.</summary>
    CommentId Id { get; }

    /// <summary>Comment text. May contain '\n' for multi-line.</summary>
    string Text { get; }

    /// <summary>Top-left canvas position.</summary>
    Vector2 Position { get; }

    /// <summary>Size in canvas units.</summary>
    Vector2 Size { get; }

    /// <summary>
    /// Color (RGBA). Header strip uses full alpha; body uses ~20% alpha.
    /// </summary>
    Vector4 Color { get; }

    /// <summary>Higher draws on top.</summary>
    int ZOrder { get; }

    /// <summary>If true, dragging the comment moves enclosed nodes too.</summary>
    bool MoveWithContents { get; }
}
