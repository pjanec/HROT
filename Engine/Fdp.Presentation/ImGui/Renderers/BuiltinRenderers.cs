using System.Numerics;
using Fdp.Core;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Renderers;

/// <summary>Built-in renderer for <see cref="Vector2"/> — inline "[x, y]".</summary>
[ImGuiRenderer(typeof(Vector2))]
public sealed class Vector2Renderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var v = (Vector2)value;
        return $"[{v.X:G4}, {v.Y:G4}]";
    }

    public bool RenderValue(object value)
    {
        var v = (Vector2)value;
        ImGuiApi.Text($"[{v.X:G4}, {v.Y:G4}]");
        return true;
    }
}

/// <summary>Built-in renderer for <see cref="Vector3"/> — inline "[x, y, z]".</summary>
[ImGuiRenderer(typeof(Vector3))]
public sealed class Vector3Renderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var v = (Vector3)value;
        return $"[{v.X:G4}, {v.Y:G4}, {v.Z:G4}]";
    }

    public bool RenderValue(object value)
    {
        var v = (Vector3)value;
        ImGuiApi.Text($"[{v.X:G4}, {v.Y:G4}, {v.Z:G4}]");
        return true;
    }
}

/// <summary>Built-in renderer for <see cref="Vector4"/> — inline "[x, y, z, w]".</summary>
[ImGuiRenderer(typeof(Vector4))]
public sealed class Vector4Renderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var v = (Vector4)value;
        return $"[{v.X:G4}, {v.Y:G4}, {v.Z:G4}, {v.W:G4}]";
    }

    public bool RenderValue(object value)
    {
        var v = (Vector4)value;
        ImGuiApi.Text($"[{v.X:G4}, {v.Y:G4}, {v.Z:G4}, {v.W:G4}]");
        return true;
    }
}

/// <summary>
/// Built-in renderer for <see cref="Quaternion"/> — shows Euler angles in degrees (Yaw / Pitch / Roll).
/// Hover over to see raw XYZW components in a tooltip.
/// </summary>
[ImGuiRenderer(typeof(Quaternion))]
public sealed class QuaternionRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var q = (Quaternion)value;
        var (y, p, r) = SimMath.ToYawPitchRollDeg(q);
        return $"Y:{y:F1}° P:{p:F1}° R:{r:F1}°";
    }

    public bool RenderValue(object value)
    {
        var q = (Quaternion)value;
        var (yaw, pitch, roll) = SimMath.ToYawPitchRollDeg(q);
        ImGuiApi.Text($"Y:{yaw:F1}° P:{pitch:F1}° R:{roll:F1}°");
        if (ImGuiApi.IsItemHovered())
            ImGuiApi.SetTooltip($"Raw XYZW: ({q.X:F5}, {q.Y:F5}, {q.Z:F5}, {q.W:F5})");
        return true;
    }
}

/// <summary>
/// Built-in renderer for <see cref="Entity"/> — shows inline "[index, vGeneration]"
/// in the value column while keeping the node expandable so the caller can still
/// drill into <c>Index</c> and <c>Generation</c> fields.
///
/// <para><see cref="RenderValue"/> returns <c>false</c> so <see cref="ImGuiPropertyTree"/>
/// falls through to the default foldable behaviour.  The summary is shown in the
/// value cell by the tree renderer.</para>
/// </summary>
[ImGuiRenderer(typeof(Entity))]
public sealed class EntityRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var e = (Entity)value;
        return e.IsNull ? "[null]" : $"[{e.Index}, v{e.Generation}]";
    }

    /// <summary>
    /// Returns <c>false</c> — this renderer only provides a summary string;
    /// the default tree renderer handles the node and its children.
    /// </summary>
    public bool RenderValue(object value) => false;
}

[ImGuiRenderer(typeof(SimTransform))]
public sealed class SimTransformRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var st = (SimTransform)value;
        return $"Pos: [{st.Position.X:G4}, {st.Position.Y:G4}, {st.Position.Z:G4}] Rot: [{st.Rotation.X:G4}, {st.Rotation.Y:G4}, {st.Rotation.Z:G4}, {st.Rotation.W:G4}]";
    }

    public bool RenderValue(object value) => false;
}

[ImGuiRenderer(typeof(SimVelocity))]
public sealed class SimVelocityRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var sv = (SimVelocity)value;
        return $"Lin: [{sv.Linear.X:G4}, {sv.Linear.Y:G4}, {sv.Linear.Z:G4}] Ang: [{sv.Angular.X:G4}, {sv.Angular.Y:G4}, {sv.Angular.Z:G4}]";
    }

    public bool RenderValue(object value) => false;
}
