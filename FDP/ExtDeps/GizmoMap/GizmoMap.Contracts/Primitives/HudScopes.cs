using System;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1d-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-41 / R3-4 / R5-4) — THE ONE reader of a
    /// <see cref="DebugPrimitiveShape.HudScope"/> run, beside the triage and <see cref="PrimitiveKind"/>: a scope marks the next
    /// <see cref="DebugPrimitive.HudScopeCount"/> primitives as the HUD of <see cref="DebugPrimitive.HudScopeSource"/> (a device, later a sight or a
    /// cockpit). ⭐ A run is drawn ONLY by a view bound to its source — <see cref="Keep"/>, applied once per frame before triage
    /// (<c>DebugGizmoLayer.FrameFor</c>); ⛔ every OTHER reader of a frame skips the WHOLE run (<see cref="Unscoped"/>, or <see cref="After"/> in an
    /// index loop) — otherwise the director's map, the CGF window, the Stride IG and every panel would draw every trainee's HUD.
    /// <para>Zero allocation: a frame with no scope is returned as it is (one byte compare per primitive); a filtered frame is copied into a
    /// caller-owned scratch array (grown, never shrunk). A scope whose count runs past the frame's end is clamped to it.</para>
    /// </summary>
    public static class HudScopes
    {
        /// <summary>No source: an unbound view (it keeps NO run).</summary>
        public const long None = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsScope(in DebugPrimitive p) => p.Shape == DebugPrimitiveShape.HudScope;

        /// <summary>A scope primitive {<paramref name="source"/>, <paramref name="count"/>} (the buffer stamps the count at the end of the run).</summary>
        public static DebugPrimitive Make(long source, int count = 0)
            => new() { Shape = DebugPrimitiveShape.HudScope, HudScopeSource = source, HudScopeCount = count };

        /// <summary>The index just past the scope at <paramref name="i"/> and its run (clamped to the frame); <paramref name="i"/> + 1 for a
        /// non-scope. ⭐ The index-loop form: <c>if (HudScopes.IsScope(f[i])) { i = HudScopes.After(f, i) - 1; continue; }</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int After(ReadOnlySpan<DebugPrimitive> frame, int i)
        {
            if (!IsScope(in frame[i])) return i + 1;
            int n = frame[i].HudScopeCount;
            if (n < 0) n = 0;
            long end = (long)i + 1 + n;
            return end > frame.Length ? frame.Length : (int)end;
        }

        /// <summary>True when the frame carries any scope (the cheap test before a copy).</summary>
        public static bool Any(ReadOnlySpan<DebugPrimitive> frame)
        {
            for (int i = 0; i < frame.Length; i++)
                if (frame[i].Shape == DebugPrimitiveShape.HudScope) return true;
            return false;
        }

        /// <summary>
        /// The frame as a view BOUND to <paramref name="boundSource"/> draws it: the bound source's run kept (its scope marker removed, so a reader
        /// downstream that skips runs keeps it), every other source's run removed whole; <see cref="None"/> ⇒ no run kept. The frame itself when it
        /// has no scope. 0 B steady (scratch grown, never shrunk).
        /// </summary>
        public static ReadOnlySpan<DebugPrimitive> Keep(ReadOnlySpan<DebugPrimitive> frame, long boundSource, ref DebugPrimitive[] scratch)
        {
            if (!Any(frame)) return frame;
            if (scratch == null || scratch.Length < frame.Length) scratch = new DebugPrimitive[Math.Max(frame.Length, (scratch?.Length ?? 0) * 2)];
            int n = 0;
            for (int i = 0; i < frame.Length;)
            {
                if (IsScope(in frame[i]))
                {
                    int end = After(frame, i);
                    if (boundSource != None && frame[i].HudScopeSource == boundSource)
                        for (int k = i + 1; k < end; k++) scratch[n++] = frame[k];
                    i = end;
                    continue;
                }
                scratch[n++] = frame[i++];
            }
            return new ReadOnlySpan<DebugPrimitive>(scratch, 0, n);
        }

        /// <summary>The frame as a reader bound to NO source sees it: every run removed whole (the shared skip for every non-view reader).</summary>
        public static ReadOnlySpan<DebugPrimitive> Unscoped(ReadOnlySpan<DebugPrimitive> frame, ref DebugPrimitive[] scratch)
            => Keep(frame, None, ref scratch);
    }
}
