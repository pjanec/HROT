using System;
using System.Numerics;
using ImGuiNET;

namespace Fdp.Presentation.Icons;

public enum TransportShape
{
    Play,
    Pause,
    StepFwd,
    StepBack,
    Rewind,
    HistoryBack,
    HistoryFwd,
}

/// <summary>
/// Stateless utility for rendering scalable vector transport icons in ImGui.
/// </summary>
public static class TransportIconRenderer
{
    public static bool DrawButton(
        string id,
        float size,
        TransportShape shape,
        bool enabled,
        out bool isHeld,
        out bool isActivated)
    {
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = false;
        bool hovered = false;
        bool pressed = false;
        isActivated = false;

        if (enabled)
        {
            clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
            hovered = ImGui.IsItemHovered();
            pressed = ImGui.IsItemActive();
            isActivated = ImGui.IsItemActivated();
        }
        else
        {
            ImGui.Dummy(new Vector2(size, size));
        }

        isHeld = pressed;
        var dl = ImGui.GetWindowDrawList();

        if (hovered && enabled)
            dl.AddRectFilled(
                pos,
                pos + new Vector2(size, size),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f)),
                2f);

        var drawPos = pressed ? pos + new Vector2(1f, 1f) : pos;
        DrawShape(dl, shape, drawPos, size, dim: !enabled, hovered: hovered && enabled);

        if (hovered && enabled)
            dl.AddRect(
                pos,
                pos + new Vector2(size, size),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)),
                2f);

        return clicked;
    }

    private static void DrawShape(
        ImDrawListPtr dl,
        TransportShape shape,
        Vector2 pos,
        float size,
        bool dim,
        bool hovered)
    {
        float alpha = dim ? 0.28f : (hovered ? 1.0f : 0.85f);
        float pad = MathF.Round(size * 0.22f);
        float w = size - pad * 2f;
        float cy = pos.Y + size * 0.5f;

        uint white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
        uint play = ImGui.GetColorU32(new Vector4(0.20f, 0.78f, 0.20f, alpha));
        uint history = ImGui.GetColorU32(new Vector4(0.20f, 0.80f, 0.90f, alpha));

        switch (shape)
        {
            case TransportShape.Play:
                dl.AddTriangleFilled(
                    pos + new Vector2(pad, pad),
                    new Vector2(pos.X + size - pad, cy),
                    pos + new Vector2(pad, size - pad),
                    play);
                break;

            case TransportShape.Pause:
                {
                    float bw = MathF.Round(w * 0.18f);
                    dl.AddRectFilled(
                        pos + new Vector2(pad, pad),
                        new Vector2(pos.X + pad + bw, pos.Y + size - pad),
                        white);
                    dl.AddRectFilled(
                        new Vector2(pos.X + pad + bw * 2.2f, pos.Y + pad),
                        new Vector2(pos.X + pad + bw * 3.2f, pos.Y + size - pad),
                        white);
                }
                break;

            case TransportShape.StepFwd:
                {
                    float lineW = MathF.Round(w * 0.16f);
                    float lineX = pos.X + size - pad - lineW;
                    dl.AddTriangleFilled(
                        pos + new Vector2(pad, pad),
                        new Vector2(lineX, cy),
                        pos + new Vector2(pad, size - pad),
                        white);
                    dl.AddRectFilled(
                        new Vector2(lineX, pos.Y + pad),
                        new Vector2(lineX + lineW, pos.Y + size - pad),
                        white);
                }
                break;

            case TransportShape.StepBack:
                {
                    float lineW = MathF.Round(w * 0.16f);
                    float lineX = pos.X + pad;
                    dl.AddRectFilled(
                        new Vector2(lineX, pos.Y + pad),
                        new Vector2(lineX + lineW, pos.Y + size - pad),
                        white);
                    dl.AddTriangleFilled(
                        new Vector2(pos.X + size - pad, pos.Y + pad),
                        new Vector2(lineX + lineW, cy),
                        new Vector2(pos.X + size - pad, pos.Y + size - pad),
                        white);
                }
                break;

            case TransportShape.Rewind:
                {
                    float lineW = MathF.Round(w * 0.16f);
                    float lineX = pos.X + pad;
                    float triOffset = size * 0.2f;
                    dl.AddRectFilled(
                        new Vector2(lineX, pos.Y + pad),
                        new Vector2(lineX + lineW, pos.Y + size - pad),
                        white);
                    dl.AddTriangleFilled(
                        new Vector2(pos.X + size - pad, pos.Y + pad),
                        new Vector2(lineX + lineW, cy),
                        new Vector2(pos.X + size - pad, pos.Y + size - pad),
                        white);
                    dl.AddTriangleFilled(
                        new Vector2(pos.X + size - pad + triOffset, pos.Y + pad),
                        new Vector2(lineX + lineW + triOffset, cy),
                        new Vector2(pos.X + size - pad + triOffset, pos.Y + size - pad),
                        white);
                }
                break;

            case TransportShape.HistoryBack:
                dl.AddLine(new Vector2(pos.X + pad, cy), new Vector2(pos.X + size - pad, cy), history, 2f);
                dl.AddLine(new Vector2(pos.X + pad, cy), new Vector2(pos.X + pad + w * 0.3f, pos.Y + pad), history, 2f);
                dl.AddLine(new Vector2(pos.X + pad, cy), new Vector2(pos.X + pad + w * 0.3f, pos.Y + size - pad), history, 2f);
                break;

            case TransportShape.HistoryFwd:
                dl.AddLine(new Vector2(pos.X + pad, cy), new Vector2(pos.X + size - pad, cy), history, 2f);
                dl.AddLine(new Vector2(pos.X + size - pad, cy), new Vector2(pos.X + size - pad - w * 0.3f, pos.Y + pad), history, 2f);
                dl.AddLine(new Vector2(pos.X + size - pad, cy), new Vector2(pos.X + size - pad - w * 0.3f, pos.Y + size - pad), history, 2f);
                break;
        }
    }
}

