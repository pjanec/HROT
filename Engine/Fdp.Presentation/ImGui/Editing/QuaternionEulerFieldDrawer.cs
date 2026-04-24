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
        var (yaw, pitch, roll) = SimMath.ToYawPitchRollDeg(q);
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
}
