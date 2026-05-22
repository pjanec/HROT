using System.Numerics;

namespace NodeEditor.Primitives;

/// <summary>
/// Axis-aligned rectangle in canvas (or screen) coordinates. Immutable.
/// <c>Min</c> is the top-left corner, <c>Size</c> is non-negative.
/// </summary>
public readonly record struct RectF(Vector2 Min, Vector2 Size)
{
    public Vector2 Max => Min + Size;
    public Vector2 Center => Min + Size * 0.5f;
    public float Width => Size.X;
    public float Height => Size.Y;

    public bool Contains(Vector2 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y;

    public bool Intersects(RectF other) =>
        !(Max.X < other.Min.X || other.Max.X < Min.X ||
          Max.Y < other.Min.Y || other.Max.Y < Min.Y);

    public bool FullyContains(RectF other) =>
        other.Min.X >= Min.X && other.Min.Y >= Min.Y &&
        other.Max.X <= Max.X && other.Max.Y <= Max.Y;

    public RectF Expand(float amount) => new(
        Min - new Vector2(amount, amount),
        Size + new Vector2(amount * 2, amount * 2));

    public static RectF FromMinMax(Vector2 min, Vector2 max) =>
        new(min, max - min);

    public static RectF FromCenterSize(Vector2 center, Vector2 size) =>
        new(center - size * 0.5f, size);

    public static RectF Empty => default;
}
