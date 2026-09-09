using System;
using System.IO;
using System.Threading;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Hrot.Editor.UiFrameRail;

/// <summary>
/// ⭐⭐ <b>One rendered window, for the life of one rail.</b> ⛔ Created only through
/// <see cref="UiFrameHarness.Begin"/>, which holds the process-wide gate Raylib's non-re-entrancy
/// requires.
/// </summary>
public sealed class UiFrameSession : IDisposable
{
    private readonly SemaphoreSlim _gate;
    private bool _disposed;

    /// <summary>⭐ How many frames <see cref="Step"/> has rendered. ⭐ A rail surface — a session that
    /// rendered zero frames asserted nothing, and that is worth being able to check.</summary>
    public int FramesRendered { get; private set; }

    internal UiFrameSession(int width, int height, SemaphoreSlim gate)
    {
        _gate = gate;

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        // ⛔ No VSync flag: a rail renders a handful of frames and must not wait on a refresh interval.
        Raylib.InitWindow(width, height, "hrot-uiframe-rail");
        if (!Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
            throw new InvalidOperationException("Raylib window did not become ready.");
        }

        rlImGui.Setup(true);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Renders ONE real frame, running <paramref name="draw"/> inside it.</b>
    ///
    /// <para>⭐ Assertions belong <b>inside</b> <paramref name="draw"/> — that is the whole point:
    /// <c>GetContentRegionAvail()</c> outside a frame is meaningless, and inside one it is the number
    /// the designer's dialog actually got.</para>
    ///
    /// <para>⚠ <b>An exception inside the callback must not leave ImGui mid-frame</b>, or every later
    /// rail in the process dies with a confusing error instead of this one. ⇒ the <c>finally</c> closes
    /// the frame before it propagates.</para>
    /// </summary>
    public void Step(Action draw)
    {
        if (draw is null) throw new ArgumentNullException(nameof(draw));
        ThrowIfDisposed();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.DarkGray);
        rlImGui.Begin();
        try     { draw(); }
        finally { rlImGui.End(); Raylib.EndDrawing(); FramesRendered++; }
    }

    /// <summary>
    /// ⭐⭐ <b>Renders <paramref name="frames"/> frames.</b> ⚠ ImGui needs a few frames to settle a popup
    /// — a modal opened in frame 0 is not laid out until 1–2 frames later, and a <c>WidthStretch</c>
    /// column inside an auto-resized window settles later still. ⭐ <b>3–6 is enough</b>; ⛔ this is not
    /// a game loop, and software GL is slow.
    /// </summary>
    public void StepN(int frames, Action draw)
    {
        for (int i = 0; i < frames; i++) Step(draw);
    }

    /// <summary>
    /// ⭐ <b>EVIDENCE, not a gate</b> — 📌 <c>R-124</c>: prefer measuring inside the frame; goldens drift
    /// with fonts and drivers. ⚠ Raylib writes relative to the process CWD, so an absolute path is
    /// resolved here and the directory created, ⛔ rather than silently writing nowhere.
    /// </summary>
    public void Screenshot(string path)
    {
        ThrowIfDisposed();

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(full)!);
            Raylib.TakeScreenshot(Path.GetFileName(full));
        }
        finally { Directory.SetCurrentDirectory(cwd); }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UiFrameSession));
    }

    /// <summary>⭐ Tears the window down and releases the process-wide gate. ⛔ Skipping either strands
    /// every later rail in the assembly.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
        finally { _gate.Release(); }
    }
}

/// <summary>
/// ⭐⭐ <b>The assertions, named so a rail reads as a claim about the UI.</b>
/// ⛔ Every one of these is only meaningful <b>inside</b> a <see cref="UiFrameSession.Step"/> callback.
/// </summary>
public static class UiProbe
{
    /// <summary>⭐ The width the CURRENT container has left — the number the `100b` defect got wrong.</summary>
    public static float AvailWidth() => ImGui.GetContentRegionAvail().X;

    /// <summary>⭐ The width the last submitted widget actually occupied.</summary>
    public static float LastItemWidth() => ImGui.GetItemRectSize().X;

    /// <summary>
    /// ⭐⭐ Whether a popup with this id is open <b>right now, in this frame</b>.
    /// ⚠ 📌 <c>100c</c>: the interesting question is not this frame but the NEXT one — a modal that
    /// reopens what <c>[x]</c> closed is <b>true, false, true</b> across three frames.
    /// </summary>
    public static bool IsPopupOpen(string id) => ImGui.IsPopupOpen(id);
}
