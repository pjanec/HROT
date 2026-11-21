using System;
using Xunit;

namespace Fdp.Presentation.Raylib.Tests;

/// <summary>
/// ⭐⭐⭐ <b>A <see cref="FactAttribute"/> that skips itself when there is no display.</b>
/// 📄 <c>docs/DESIGN_Gizmo_Renderer_Seam.md</c> §8 <c>V2</c> · <c>CE-259aa</c>.
///
/// <para>⛔⛔ <b>Why this exists, measured 2026-09-10.</b> A test that calls <c>Raylib.InitWindow</c>
/// needs a GL context. Without one the native library does not throw — <b>it segfaults</b>, and a
/// segfault does not fail one test, it <b>aborts the whole run</b>. Everything ordered after it is
/// silently never executed and the summary line still reads like a normal result.</para>
///
/// <para>📐 That is exactly how <c>CE-259aa</c> hid ~95 of this project's ~185 tests. Two DIFFERENT
/// crash sites were involved: the renderer doubles (fixed by <c>R0..R4</c>) and
/// <c>FdpApplicationTests</c>, which opens a real window. Fixing the first only revealed the second.</para>
///
/// <para>⭐ Why conditional rather than a plain <c>Skip</c>: the test is <b>valid and valuable</b> on a
/// machine with a display — it pins the <c>OnLoad → OnUpdate → OnDrawWorld → OnDrawUI → OnUnload</c>
/// lifecycle order. A hard skip would delete real coverage to satisfy a container. This keeps it, and
/// reports honestly as <c>Skipped</c> where it cannot run — ⛔ never as a pass, and never as a crash.</para>
///
/// <para>⚠ <b>Use this for ANY test that opens a window or issues a Raylib draw call.</b> A crash is
/// the worst possible outcome: a skip costs one line in the summary, a crash costs every test after it.
/// ⭐ Tests that only need a headless <b>ImGui</b> context want <c>ImGuiTestFixture</c> and the
/// <c>"ImGui Sequential"</c> collection instead — that one really is emulatable.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresDisplayFactAttribute : FactAttribute
{
    public RequiresDisplayFactAttribute()
    {
        if (!HasDisplay)
        {
            Skip = "Requires a display. Raylib.InitWindow needs a GL context; without one the native "
                 + "library SEGFAULTS and aborts the entire test run rather than failing this test "
                 + "(CE-259aa). Set DISPLAY (or run on Windows) to execute it. "
                 + "See docs/DESIGN_Gizmo_Renderer_Seam.md §8 V2.";
        }
    }

    /// <summary>⭐ Windows always has one; on Linux/macOS require X11 or Wayland to be present.</summary>
    private static bool HasDisplay =>
        OperatingSystem.IsWindows()
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
}
