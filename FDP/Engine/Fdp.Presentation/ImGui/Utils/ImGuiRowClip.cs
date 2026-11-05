using ImGuiNET;

namespace Fdp.Presentation.Utils;

/// <summary>
/// ⭐ CE-2403 (R-2084) — the ONE wrapper over <c>ImGuiListClipper</c>: a list of N equal-height rows builds only the rows on screen.
/// 📐 The entity inspector built a label (several strings) for every entity, up to 1000, every frame — ≈ 320 KB of garbage per frame on
/// Crossfire — while ~30 rows are visible. The clipper is native memory (no managed allocation).
/// <code>
/// using var clip = new ImGuiRowClip(rows.Count);
/// while (clip.Step())
///     for (int i = clip.Start; i &lt; clip.End; i++) DrawRow(rows[i]);
/// </code>
/// ⚠ The rows must be of equal height (the first row is measured), and nothing between <c>Step()</c> calls may draw outside the rows.
/// </summary>
public unsafe ref struct ImGuiRowClip
{
    private ImGuiListClipperPtr _clipper;

    public ImGuiRowClip(int count, float rowHeight = -1f)
    {
        _clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        _clipper.Begin(count, rowHeight);
    }

    /// <summary>Advances to the next range of rows to build; false when done.</summary>
    public bool Step() => _clipper.Step();

    /// <summary>The first row of the current range.</summary>
    public int Start => _clipper.DisplayStart;

    /// <summary>One past the last row of the current range.</summary>
    public int End => _clipper.DisplayEnd;

    public void Dispose()
    {
        if (_clipper.NativePtr == null) return;
        _clipper.End();
        _clipper.Destroy();
        _clipper = default;
    }
}
