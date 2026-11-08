using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ B-10 K4 (docs/UX/DESIGN_Input_And_Commands.md §6g) — the four surfaces read their input through the KEYMAP and nothing else: a raw device read (<c>IsKeyDown</c>, <c>IsKeyPressed</c>, <c>IsMouseButton…</c>,
/// the wheel, the mouse movement) in the FPS station, the fall screen, the audio layer, the Player's map keys and F3, or the timeline is a defect — it is a key the keymap, the Keyboard shortcuts window and a rebind
/// cannot see. ⛔ Not scanned, by design (design §6g): a widget's own text editing, the dumb terminal's raw forward, a gizmo's own cleanup. ⚠ A text scan: it cannot say the rows are RIGHT (the rails per surface do).
/// ✅ Red-proof: one raw read put back into any scanned file ⇒ this names the file and line.
/// </summary>
public sealed class NoRawKeyReadsTests
{
    private static readonly Regex Raw = new(@"\b(IsKeyDown|IsKeyPressed|IsKeyReleased|IsMouseButtonDown|IsMouseButtonPressed|IsMouseDown|IsMouseClicked|MouseWheelMove|MouseWheelH|GetMouseWheelMove|MouseDelta|KeyShift|KeyCtrl|KeyAlt)\b");

    // file → the lines (substrings) that may stay raw, and why
    private static readonly Dictionary<string, string[]> Files = new()
    {
        ["Hrot/Engine/Hrot.Presentation/Map3D/PossessionStation.cs"] = Array.Empty<string>(),
        ["Hrot/Engine/Hrot.Presentation/Map3D/FallScreen.cs"] = Array.Empty<string>(),
        // the default keyboard of the layer when no rail injects one: ONE raw read, the device adapter's source
        ["Hrot/Engine/Hrot.Presentation/Audio/AudioLayer.cs"] = new[] { "Raylib.IsKeyPressed(k)" },
        // the Player's undo: STOPPED (design §6g) — nothing records onto its stack, R-4120 says none in Live / Preview
        ["Hrot/Subsystems/Hrot.Player/PlayerApplication.cs"] = new[] { "KeyboardKey.LeftControl", "KeyboardKey.RightControl", "KeyboardKey.LeftShift", "KeyboardKey.RightShift", "KeyboardKey.Z", "KeyboardKey.Y", "WantCaptureKeyboard" },
        ["Hrot/Subsystems/Hrot.Player/UI/PerformanceOverlay.cs"] = Array.Empty<string>(),
    };

    [Fact]
    public void NoSurfaceReadsARawKey()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HROT.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = new List<string>();
        foreach (var (rel, allowed) in Files)
        {
            int n = 0;
            foreach (var line in File.ReadLines(Path.Combine(root!.FullName, rel)))
            {
                n++;
                var t = line.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("///")) continue;
                var code = line.Contains("//") ? line.Substring(0, line.IndexOf("//", StringComparison.Ordinal)) : line;
                if (!Raw.IsMatch(code)) continue;
                bool ok = false;
                foreach (var a in allowed) if (code.Contains(a, StringComparison.Ordinal)) ok = true;
                if (!ok) offenders.Add($"{rel}:{n}  {t.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "raw device reads (declare a keymap row and read it through a KeymapReader):\n" + string.Join("\n", offenders));
    }
}
