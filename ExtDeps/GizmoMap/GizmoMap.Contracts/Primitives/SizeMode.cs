namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public enum SizeMode : byte
    {
        WorldMeters   = 0,
        ScreenPixels  = 1,
        ScreenPercent = 2  // Offset values are 0.0-1.0 fractions of viewport dimensions
    }
}
