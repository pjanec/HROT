using System;
using System.Threading;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Xunit;

namespace Hrot.Presentation.Tests.UiFrame;

/// <summary>
/// ⭐ <c>CE-1039</c> — the frame harness keeps ONE window for the test process (Linux / GLX) instead of opening and closing one per
/// rail: a fresh window's GL context came up unusable about once per few hundred creations under Xvfb, and the host died with
/// SIGSEGV in <c>InitWindow → rlglInit</c>. The risky part is the thread hop — a GL context is current on one thread, and xUnit runs
/// rails on different pool threads — so three sessions here run on three new threads, each drawing and reading GL state back.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class OneWindowPerProcessTests
{
    [SkippableFact]
    public void CE1039_SessionsOnDifferentThreads_ShareOneWindow_AndEachCanDraw()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Skip.IfNot(OperatingSystem.IsLinux(), "the window is kept only where GLX hands the context between threads");

        int before = -1;
        for (int i = 0; i < 3; i++)
        {
            Exception? error = null;
            int frames = 0; float width = 0;
            var t = new Thread(() =>
            {
                try
                {
                    using var frame = UiFrameHarness.Begin();
                    frame.StepN(2, () =>
                    {
                        ImGui.Begin($"CE-1039 #{i}");
                        ImGui.Text("one window, many threads");
                        width = ImGui.GetWindowWidth();
                        ImGui.End();
                    });
                    frames = frame.FramesRendered;
                }
                catch (Exception e) { error = e; }
            });
            t.Start();
            t.Join();

            Assert.Null(error);
            Assert.Equal(2, frames);
            Assert.True(width > 0f, "ImGui laid the window out (the GL context was usable on this thread)");
            Assert.True(UiFrameHarness.KeepsWindow, "the window is kept between sessions");
            if (before < 0) before = UiFrameHarness.WindowsCreated;
            else Assert.Equal(before, UiFrameHarness.WindowsCreated);
        }
        Assert.Equal(1, before);
    }
}
