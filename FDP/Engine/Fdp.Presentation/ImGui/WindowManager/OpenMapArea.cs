using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐ CE-1140 / CE-1145 / CE-1132 — the part of the window where the map is actually SEEN: the dockspace's central node (its passthrough
/// hole), which the host's frame loop reports right after it lays the dockspace out (<see cref="ReportCentralNodeOf"/>). The map is drawn
/// under the docked panels, so a window corner is often hidden; an overlay that wants a visible corner (the FPS HUD, the frame counter,
/// the RTS legend) asks for this instead. Null ⇒ unknown (a host without a dockspace, a test): use the whole view.
/// </summary>
public static class OpenMapArea
{
    /// <summary>The visible map rectangle in window pixels (X, Y, Width, Height), or null.</summary>
    public static (float X, float Y, float W, float H)? Current { get; set; }

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igDockBuilderGetCentralNode(uint nodeId);

    /// <summary>
    /// Reads the central node of the dockspace <paramref name="dockspaceId"/> and stores its rectangle in <see cref="Current"/>.
    /// ⚠ It reads <c>ImGuiDockNode.Pos / Size</c> at their offsets in Dear ImGui 1.91 (the version ImGui.NET 1.91.6 bundles): a
    /// rectangle outside the window or empty is rejected, so a layout change can only fall back to "unknown", never mis-place.
    /// </summary>
    public static void ReportCentralNodeOf(uint dockspaceId, Vector2 windowSize)
    {
        try
        {
            var node = igDockBuilderGetCentralNode(dockspaceId);
            if (node == IntPtr.Zero) { Current = null; return; }
            float x = ReadFloat(node, PosOffset), y = ReadFloat(node, PosOffset + 4);
            float w = ReadFloat(node, PosOffset + 8), h = ReadFloat(node, PosOffset + 12);
            Current = IsSane(x, y, w, h, windowSize) ? (x, y, w, h) : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { Current = null; }
    }

    /// <summary>The byte offset of <c>ImGuiDockNode.Pos</c> (ID, 5 flag words, 3 node pointers, a vector, a tab-bar pointer before it).</summary>
    internal const int PosOffset = 72;

    public static bool IsSane(float x, float y, float w, float h, Vector2 window)
        => float.IsFinite(x) && float.IsFinite(y) && w >= 64f && h >= 64f && x >= -1f && y >= -1f
           && x + w <= window.X + 1f && y + h <= window.Y + 1f;

    private static float ReadFloat(IntPtr p, int offset) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, offset));

    /// <summary>The visible map's corner (<paramref name="left"/> / <paramref name="top"/>) inset by <paramref name="margin"/>, for a
    /// block of <paramref name="width"/> × <paramref name="height"/> — in a view of <paramref name="viewport"/> when the area is unknown.</summary>
    public static Vector2 Corner(Vector2 viewport, bool left, bool top, float width, float height, float margin = 12f)
    {
        var (x, y, w, h) = Current ?? (0f, 0f, viewport.X, viewport.Y);
        return new Vector2(left ? x + margin : x + w - margin - width, top ? y + margin : y + h - margin - height);
    }
}
