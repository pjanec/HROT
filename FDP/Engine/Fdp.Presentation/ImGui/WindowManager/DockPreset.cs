using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Presentation.WindowManager;

/// <summary>The four regions of a <see cref="DockPreset"/> around the map (the dockspace's open hole).</summary>
public enum DockRegion
{
    /// <summary>The left column, full height above the bottom band.</summary>
    Left,
    /// <summary>The right column's upper part.</summary>
    Right,
    /// <summary>The right column's lower part (a tab strip when several windows are placed there).</summary>
    RightLower,
    /// <summary>The bottom band, full width.</summary>
    Bottom,
}

/// <summary>
/// ⭐ R-4 / V4 (docs/UX/DESIGN_Review_Perspective.md §3 D10 / D11) — <b>a DOCK LAYOUT as data</b>: which window sits in which of four regions around the map, and how big the regions are.
/// The windows are named by id, so one definition (the Review layout's roles) can be given each host's own ids. ⭐ The geometry (<see cref="Regions"/>) is pure — a rail checks the
/// picture without ImGui — and <see cref="Apply"/> performs the same splits with Dear ImGui's DockBuilder on the dockspace's central node (the map's hole, always an empty leaf), so it
/// never disturbs the windows already docked around it.
/// </summary>
public sealed record DockPreset(
    IReadOnlyList<(string WindowId, DockRegion Region)> Placements,
    float LeftFraction = 0.18f, float RightFraction = 0.27f, float BottomFraction = 0.27f, float RightLowerFraction = 0.55f)
{
    /// <summary>The rectangles of the regions (and the map's remaining <c>Centre</c>) inside <paramref name="work"/> (x, y, w, h) — the same splits <see cref="Apply"/> makes.</summary>
    public (Vector4 Left, Vector4 Right, Vector4 RightLower, Vector4 Bottom, Vector4 Centre) Regions(Vector4 work)
    {
        float x = work.X, y = work.Y, w = work.Z, h = work.W;
        float bh = h * BottomFraction, ah = h - bh;                    // the band under everything
        float lw = w * LeftFraction;
        float rw = (w - lw) * RightFraction;                           // of what the left column left over
        float cw = w - lw - rw;
        float rlh = ah * RightLowerFraction;
        return (new Vector4(x, y, lw, ah),
                new Vector4(x + w - rw, y, rw, ah - rlh),
                new Vector4(x + w - rw, y + ah - rlh, rw, rlh),
                new Vector4(x, y + ah, w, bh),
                new Vector4(x + lw, y, cw, ah));
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="iniText"/> (an <c>imgui.ini</c>) already docks one of this preset's windows — the user (or a shipped layout) has placed them, so the preset
    /// must not rearrange them.
    /// </summary>
    public bool IsPlacedIn(string iniText)
    {
        if (string.IsNullOrEmpty(iniText)) return false;
        foreach (var (id, _) in Placements)
        {
            int at = iniText.IndexOf("[Window][###" + id + "]", StringComparison.Ordinal);
            if (at < 0) continue;
            int next = iniText.IndexOf("\n[", at + 1, StringComparison.Ordinal);
            var section = next < 0 ? iniText.AsSpan(at) : iniText.AsSpan(at, next - at);
            if (section.IndexOf("DockId=".AsSpan(), StringComparison.Ordinal) >= 0) return true;
        }
        return false;
    }

    // ── Dear ImGui DockBuilder (imgui_internal.h, exported by cimgui) ───────────────────────────────────────────────────────────

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igDockBuilderGetCentralNode(uint nodeId);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderSplitNode(uint nodeId, int splitDir, float sizeRatioForNodeAtDir, out uint outIdAtDir, out uint outIdAtOppositeDir);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string windowName, uint nodeId);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint nodeId);

    private const int DirLeft = 0, DirRight = 1, DirUp = 2, DirDown = 3;   // ImGuiDir

    /// <summary>
    /// Splits the central node of <paramref name="dockspaceId"/> into the four regions and docks every placed window into its region (tabbed where several share one). <see langword="false"/> when the native
    /// DockBuilder is unavailable (then nothing was done).
    /// </summary>
    public bool Apply(uint dockspaceId)
    {
        try
        {
            var node = igDockBuilderGetCentralNode(dockspaceId);
            uint target = node != IntPtr.Zero ? (uint)Marshal.ReadInt32(node, 0) : dockspaceId;
            igDockBuilderSplitNode(target, DirDown, BottomFraction, out uint bottom, out uint rest);
            igDockBuilderSplitNode(rest, DirLeft, LeftFraction, out uint left, out uint rest2);
            igDockBuilderSplitNode(rest2, DirRight, RightFraction, out uint right, out uint centre);
            igDockBuilderSplitNode(right, DirDown, RightLowerFraction, out uint rightLower, out uint rightUpper);
            foreach (var (id, region) in Placements)
            {
                uint to = region switch { DockRegion.Left => left, DockRegion.Right => rightUpper, DockRegion.RightLower => rightLower, _ => bottom };
                igDockBuilderDockWindow("###" + id, to);
            }
            igDockBuilderFinish(dockspaceId);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}
