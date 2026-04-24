using System;
using System.Numerics;
using Fdp.Core;
using StructEdit.Core;

namespace Fdp.Presentation.Editing;

using ImGuiApi = ImGuiNET.ImGui;

public sealed class QuaternionEulerFieldDrawer : IImGuiFieldDrawer
{
    public Type TargetType => typeof(Quaternion);

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        Quaternion q = value is Quaternion qv ? qv : Quaternion.Identity;
        var (yaw, pitch, roll) = ToEulerDeg(q);
        Vector3 yprDeg = new Vector3(yaw, pitch, roll);

        bool ok = ImGuiApi.DragFloat3("Y/P/R (deg)##v", ref yprDeg, 0.5f, 0f, 0f, "%.2f", ImGuiNET.ImGuiSliderFlags.None);
        if (ok)
        {
            const float Deg2Rad = MathF.PI / 180f;
            value = SimMath.FromYawPitchRoll(
                yprDeg.X * Deg2Rad,
                yprDeg.Y * Deg2Rad,
                yprDeg.Z * Deg2Rad);
        }
        return ok;
    }

    private static (float yaw, float pitch, float roll) ToEulerDeg(Quaternion q)
    {
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(sinyCosp, cosyCosp);

        const float Rad2Deg = 180f / MathF.PI;
        return (yaw * Rad2Deg, pitch * Rad2Deg, roll * Rad2Deg);
    }
}
