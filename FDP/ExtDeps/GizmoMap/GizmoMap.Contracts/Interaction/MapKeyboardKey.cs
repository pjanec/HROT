namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // Backend-facing keyboard key identifier. Values intentionally match common
    // platform constants (e.g. Raylib KeyboardKey) so the presentation layer can
    // cast directly. Extend as needed; new values do not break existing gizmos.
    [System.Flags]
    public enum MapKeyboardKey : int
    {
        Escape = 256,
        Enter  = 257,
        Tab    = 258,
        Delete = 261,

        LeftShift    = 340,
        LeftControl  = 341,
        LeftAlt      = 342,
        RightShift   = 344,
        RightControl = 345,
        RightAlt     = 346,

        ShiftMask = 1 << 28,
        CtrlMask  = 1 << 29,
        AltMask   = 1 << 30,
    }
}
