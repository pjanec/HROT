using System.Numerics;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>ImGui color constants for editor drawer feedback.</summary>
public static class EditorColors
{
    public static readonly Vector4 Error   = new(0.9f, 0.2f, 0.2f, 1f);
    public static readonly Vector4 Warning = new(0.9f, 0.7f, 0.1f, 1f);
    public static readonly Vector4 Info    = new(0.5f, 0.8f, 1.0f, 1f);
}
