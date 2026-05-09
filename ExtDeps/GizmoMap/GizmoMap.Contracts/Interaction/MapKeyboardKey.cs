namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // Backend-facing keyboard key identifier. Values intentionally match common
    // platform constants (e.g. Raylib KeyboardKey) so the presentation layer can
    // cast directly. Extend as needed; new values do not break existing gizmos.
    public enum MapKeyboardKey : int
    {
        Escape = 256,
        Enter  = 257,
        Tab    = 258,
        Delete = 261,
    }
}
