namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // Backend-facing mouse button identifier. Decoupled from Raylib.MouseButton so the
    // interaction core has no presentation dependency.
    [System.Flags]
    public enum MapMouseButton : int
    {
        Left   = 0,
        Right  = 1,
        Middle = 2,

        ShiftMask = 1 << 28,
        CtrlMask  = 1 << 29,
        AltMask   = 1 << 30,
    }
}
