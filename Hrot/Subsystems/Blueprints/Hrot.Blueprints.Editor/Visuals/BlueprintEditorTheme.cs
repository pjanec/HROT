using System.Numerics;

namespace Hrot.Blueprints.Editor.Visuals;

/// <summary>Theme color constants for Blueprint editor visual extensions.</summary>
public static class BlueprintEditorTheme
{
    public static readonly Vector4 WhenAttachmentBg  = new(0.20f, 0.30f, 0.45f, 1.0f);
    public static readonly Vector4 EqsReadBg         = new(0.20f, 0.40f, 0.30f, 1.0f);
    public static readonly Vector4 EqsSpawnBg        = new(0.30f, 0.40f, 0.30f, 1.0f);
    public static readonly Vector4 CrossAssetBg      = new(0.35f, 0.30f, 0.45f, 1.0f);
    public static readonly Vector4 WhenFiringPulse   = new(0.95f, 0.85f, 0.20f, 1.0f);
}
