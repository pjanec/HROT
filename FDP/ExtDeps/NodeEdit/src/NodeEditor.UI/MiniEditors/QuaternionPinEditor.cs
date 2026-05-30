using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>Quaternion</c> pins. Exposes yaw, pitch, and roll in
/// degrees following the host's Z-Y-X convention (SimMath), and strictly decouples
/// transient UI state from the model to prevent gimbal-lock aliasing during edits.
/// Applies continuous wrapping to prevent infinite numerical escalation.
/// </summary>
public sealed class QuaternionPinEditor : IPinDefaultValueEditor
{
    private sealed class EditorState
    {
        public Quaternion ModelValue;
        public Vector3 EulerDeg; // X = Roll, Y = Pitch, Z = Yaw
    }

    // Maintain stable UI state per-widget to break the alias cycle
    private static readonly Dictionary<uint, EditorState> s_states = new();

    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        uint id = ImGui.GetID("##quat");

        if (!s_states.TryGetValue(id, out var state))
        {
            state = new EditorState();
            s_states[id] = state;
        }

        var q = value is Quaternion qv ? qv : Quaternion.Identity;

        // Synchronize state only if the model changed externally (Undo, Hot Reload, etc.)
        if (q != state.ModelValue)
        {
            state.ModelValue = q;
            Decompose(q, out float y, out float p, out float r);
            state.EulerDeg = new Vector3(r, p, y);
        }

        float yawDeg = state.EulerDeg.Z;
        float pitchDeg = state.EulerDeg.Y;
        float rollDeg = state.EulerDeg.X;

        bool changed = false;

        float gap = 4f * ImGui.GetIO().FontGlobalScale;
        float fieldWidth = (ctx.MaxWidth - (gap * 2f)) / 3f;

        ImGui.PushItemWidth(MathF.Max(fieldWidth, 1f));

        bool cY = false, cP = false, cR = false;
        if (DragFloatWithExpression.Render("##yaw", ref yawDeg, out cY, 0.5f, "Y:%.1f")) changed = true;
        ImGui.SameLine(0f, gap);

        if (DragFloatWithExpression.Render("##pitch", ref pitchDeg, out cP, 0.5f, "P:%.1f")) changed = true;
        ImGui.SameLine(0f, gap);

        if (DragFloatWithExpression.Render("##roll", ref rollDeg, out cR, 0.5f, "R:%.1f")) changed = true;

        ImGui.PopItemWidth();
        committed = cY || cP || cR;

        if (changed)
        {
            // Enforce strict mathematical boundaries during interaction
            yawDeg = WrapYaw(yawDeg);
            pitchDeg = WrapDegrees(pitchDeg);
            rollDeg = WrapDegrees(rollDeg);

            state.EulerDeg = new Vector3(rollDeg, pitchDeg, yawDeg);
            state.ModelValue = Compose(yawDeg, pitchDeg, rollDeg);
            value = state.ModelValue;
            return true;
        }

        return false;
    }

    private static Quaternion Compose(float yawDeg, float pitchDeg, float rollDeg)
    {
        float y = yawDeg * MathF.PI / 180f;
        float p = pitchDeg * MathF.PI / 180f;
        float r = rollDeg * MathF.PI / 180f;

        // Host convention (SimMath): Z-Y-X (Yaw, Pitch, Roll)
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, y)
             * Quaternion.CreateFromAxisAngle(Vector3.UnitY, p)
             * Quaternion.CreateFromAxisAngle(Vector3.UnitX, r);
    }

    private static void Decompose(Quaternion q, out float yawDeg, out float pitchDeg, out float rollDeg)
    {
        // Extract Euler angles for Z-Y-X rotation using the rotation matrix formulation
        var mat = Matrix4x4.CreateFromQuaternion(q);

        float pitchRad = MathF.Asin(Math.Clamp(-mat.M31, -1f, 1f));
        float yawRad, rollRad;

        // Prevent gimbal lock instability when cos(pitch) approaches 0
        if (MathF.Abs(mat.M31) < 0.999f)
        {
            yawRad = MathF.Atan2(mat.M21, mat.M11);
            rollRad = MathF.Atan2(mat.M32, mat.M33);
        }
        else
        {
            yawRad = MathF.Atan2(-mat.M12, mat.M22);
            rollRad = 0f;
        }

        yawDeg = WrapYaw(yawRad * 180f / MathF.PI);
        pitchDeg = WrapDegrees(pitchRad * 180f / MathF.PI);
        rollDeg = WrapDegrees(rollRad * 180f / MathF.PI);
    }

    private static float WrapDegrees(float deg)
    {
        float wrapped = deg % 360f;
        if (wrapped > 180f) wrapped -= 360f;
        else if (wrapped <= -180f) wrapped += 360f;
        return wrapped;
    }

    private static float WrapYaw(float deg)
    {
        float wrapped = deg % 360f;
        return wrapped < 0f ? wrapped + 360f : wrapped;
    }
}
