using System;
using System.Collections.Generic;

namespace Fdp.Presentation.Fonts;

/// <summary>
/// Process-wide ambient registry that publishes the fonts baked by
/// <see cref="EditorFontService"/> so that consumers which cannot easily be
/// threaded through the composition root (most notably the NodeEdit canvas
/// <c>IEditorTheme</c> adapter) can resolve them without a direct reference to
/// the presentation shell.
///
/// <para>The shell repopulates this on every (re)bake — including runtime DPI /
/// UI-scale changes — so readers must call <see cref="ResolveCanvasFont"/> fresh
/// each frame rather than caching the returned pointer across bakes.</para>
///
/// <para>All members are safe to read before any bake has run: the ladder starts
/// empty and <see cref="ResolveCanvasFont"/> returns <see cref="IntPtr.Zero"/>
/// (the documented "use the ImGui default font" fallback).</para>
/// </summary>
public static class EditorFontRegistry
{
    private static readonly object _gate = new();

    // Baked canvas faces keyed by their actual pixel size (already DPI/scale-multiplied),
    // sorted ascending so ResolveCanvasFont can pick the smallest that is >= the target.
    private static float[] _ladderSizes = Array.Empty<float>();
    private static nint[]  _ladderPtrs  = Array.Empty<nint>();

    /// <summary>
    /// Native pointer to the primary UI ("chrome") font — the implicit ImGui
    /// default (<c>Fonts[0]</c>). <see cref="IntPtr.Zero"/> until the first bake.
    /// </summary>
    public static nint DefaultFont { get; private set; }

    /// <summary>The scale factor (DPI × user multiplier) applied at the last bake.</summary>
    public static float CurrentScale { get; private set; } = 1f;

    /// <summary>True once <see cref="Publish"/> has run at least once.</summary>
    public static bool IsPopulated { get; private set; }

    /// <summary>
    /// Replace the published font set. Called by <see cref="EditorFontService"/>
    /// after every atlas (re)bake. The <paramref name="canvasLadder"/> keys are the
    /// baked pixel sizes (post-scale); values are native <c>ImFont*</c> pointers.
    /// </summary>
    public static void Publish(nint defaultFont, IReadOnlyDictionary<float, nint> canvasLadder, float scale)
    {
        if (canvasLadder is null) throw new ArgumentNullException(nameof(canvasLadder));

        var sizes = new List<float>(canvasLadder.Keys);
        sizes.Sort();
        var ptrs = new nint[sizes.Count];
        for (int i = 0; i < sizes.Count; i++) ptrs[i] = canvasLadder[sizes[i]];

        lock (_gate)
        {
            _ladderSizes = sizes.ToArray();
            _ladderPtrs  = ptrs;
            DefaultFont  = defaultFont;
            CurrentScale = scale;
            IsPopulated  = true;
        }
    }

    /// <summary>
    /// Resolve the best canvas font for <paramref name="targetPixelSize"/> using the
    /// "smallest baked size that is still &gt;= target, else the largest baked size"
    /// policy (avoids upscaling blur). Returns <see cref="IntPtr.Zero"/> when no
    /// ladder has been baked, signalling a fallback to the ImGui default font.
    /// </summary>
    public static nint ResolveCanvasFont(float targetPixelSize)
    {
        lock (_gate)
        {
            int n = _ladderSizes.Length;
            if (n == 0) return IntPtr.Zero;

            for (int i = 0; i < n; i++)
            {
                if (_ladderSizes[i] >= targetPixelSize)
                    return _ladderPtrs[i];
            }
            return _ladderPtrs[n - 1]; // target exceeds all baked sizes → largest
        }
    }

    /// <summary>Test/shutdown hook: clear all published state back to the pre-bake default.</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            _ladderSizes = Array.Empty<float>();
            _ladderPtrs  = Array.Empty<nint>();
            DefaultFont  = IntPtr.Zero;
            CurrentScale = 1f;
            IsPopulated  = false;
        }
    }
}
