namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // Backend-facing mouse button identifier. Decoupled from Raylib.MouseButton so the
    // interaction core has no presentation dependency.
    public enum MapMouseButton : int
    {
        Left   = 0,
        Right  = 1,
        Middle = 2,
    }
}
