using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>Quaternion</c> pins. Exposes yaw, pitch, and roll in
/// degrees (matching Unreal Blueprint UX) and converts to/from the quaternion
/// representation internally. Each axis uses <see cref="DragFloatWithExpression"/>.
/// </summary>
public sealed class QuaternionPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        var q = value is Quaternion qv ? qv : Quaternion.Identity;

        // Convert quaternion to yaw/pitch/roll degrees.
        ToYawPitchRoll(q, out float yawDeg, out float pitchDeg, out float rollDeg);

        bool changed = false;
        float fieldWidth = ctx.MaxWidth / 3f - 2f;
        bool cY = false, cP = false, cR = false;

        ImGui.PushItemWidth(fieldWidth);

        if (DragFloatWithExpression.Render("##yaw", ref yawDeg, out cY, 0.5f, "Y:%.1f")) changed = true;
        ImGui.SameLine(0f, 2f);

        if (DragFloatWithExpression.Render("##pitch", ref pitchDeg, out cP, 0.5f, "P:%.1f")) changed = true;
        ImGui.SameLine(0f, 2f);

        if (DragFloatWithExpression.Render("##roll", ref rollDeg, out cR, 0.5f, "R:%.1f")) changed = true;

        ImGui.PopItemWidth();
        committed = cY || cP || cR;

        if (changed)
        {
            value = Quaternion.CreateFromYawPitchRoll(
                yawDeg * MathF.PI / 180f,
                pitchDeg * MathF.PI / 180f,
                rollDeg * MathF.PI / 180f);
            return true;
        }

        return false;
    }

    // ── conversion ────────────────────────────────────────────────────────────

    private static void ToYawPitchRoll(Quaternion q, out float yawDeg, out float pitchDeg, out float rollDeg)
    {
        // Roll (X): rotation around X-axis
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinrCosp, cosrCosp);

        // Pitch (Y): rotation around Y-axis
        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinp)
            : MathF.Asin(sinp);

        // Yaw (Z): rotation around Z-axis
        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(sinyCosp, cosyCosp);

        yawDeg = WrapDegrees(yaw * 180f / MathF.PI);
        pitchDeg = WrapDegrees(pitch * 180f / MathF.PI);
        rollDeg = WrapDegrees(roll * 180f / MathF.PI);
    }

    private static float WrapDegrees(float deg)
    {
        // Wrap to (-180, 180]
        while (deg > 180f) deg -= 360f;
        while (deg <= -180f) deg += 360f;
        return deg;
    }
}
