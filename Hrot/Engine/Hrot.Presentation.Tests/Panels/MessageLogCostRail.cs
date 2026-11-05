using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core.Logging;
using Fdp.Presentation.Panels;
using Fdp.Testing;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ W-P1 (CE-4234) — the Message Log's cost must be BOUNDED as the log grows. Measured live (<c>HROT_FRAME_STATS=1</c>, per-window timing): <c>fdp_message_log</c> was the largest UI window by far and grew with the run
/// (4 → 26 ms/frame in a crossfire round) because the global NLog source kept every line forever, its panel filtered ALL of them and built a Selectable + popup + 3 texts per message for ALL of them every frame, and its
/// window built a throw-away view model of all of them on top. Now: a source keeps at most <see cref="MessageLogLimits.MaxRetained"/> lines, the panel builds only the rows in (or a viewport from) the visible band, and
/// the window builds the view model only when a snapshot is captured.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MessageLogCostRail
{
    private sealed class Source : IMessageLogSource
    {
        private readonly MessageLogEntry[] _entries;
        public int Calls;
        public Source(int n)
        {
            _entries = Enumerable.Range(0, n).Select(i => new MessageLogEntry(DateTime.UtcNow, LogSeverity.Info, "Rail",
                i % 50 == 0 ? string.Join("\n", Enumerable.Range(0, 12).Select(l => $"stack line {l} of message {i}")) : $"message {i}", Array.Empty<LogChunk>())).ToArray();
        }
        public string SourceId => "rail";
        public string DisplayName => "Rail";
        public IReadOnlyList<MessageLogEntry> GetMessages() { Calls++; return _entries; }
        public void Clear() { }
#pragma warning disable CS0067
        public event Action<MessageLogEntry>? OnMessageAdded;
#pragma warning restore CS0067
    }

    private static long LastBytesPerFrame;

    private static (int Rows, double MsPerFrame, int Calls, double ViewModelMs) Draw(int n)
    {
        var src = new Source(n);
        var registry = new MessageLogRegistry(); registry.RegisterSource(src);
        var panel = new MessageLogPanel(registry);
        using var frame = UiFrameHarness.Begin(900, 600);
        void Step() => frame.Step(() =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(900, 600));
            ImGui.Begin("Message Log", ImGuiWindowFlags.NoCollapse);
            panel.DrawContent();
            ImGui.End();
        });
        for (int i = 0; i < 6; i++) Step();            // settle
        src.Calls = 0;
        var sw = Stopwatch.StartNew();
        const int frames = 20;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++) Step();
        LastBytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / frames;
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        int calls = src.Calls / frames;
        var vm0 = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) panel.BuildViewModel("p", "message-log");   // what the window used to build EVERY frame and throw away
        return (panel.RowsDrawn, ms, calls, vm0.Elapsed.TotalMilliseconds / 5);
    }

    [SkippableFact]
    public void TheRowsBuiltPerFrame_AreBounded_WhateverTheLogHolds()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var small = Draw(200); var big = Draw(20_000);
        Console.WriteLine($"[MessageLogCostRail] 200 messages: {small.Rows} rows, {small.MsPerFrame:0.0} ms/frame · 20 000 messages: {big.Rows} rows, {big.MsPerFrame:0.0} ms/frame (+ {big.ViewModelMs:0.0} ms of throw-away view model)");
        Assert.True(big.Rows <= 150, $"only the visible band is built ({big.Rows} rows of 20 000)");
        Assert.True(big.Rows >= 10, "...and it is not empty");
        Assert.Equal(1, big.Calls);   // the source is read ONCE a frame (the throw-away view model no longer reads it too)
        Assert.True(small.Rows <= 200);
    }

    /// <summary>
    /// ⭐ CE-2403 (R-2084) — 20 000 messages: the panel copies them into a list it reuses and filters into a reused index list, so a steady
    /// frame's garbage is the visible rows' text only — not a snapshot array plus an index list sized to the whole log (≈ 120 KB/frame live).
    /// 🔴 Red-proof: <c>new List&lt;int&gt;(messages.Count)</c> per frame ⇒ ≥ 80 KB and the bound fails.
    /// </summary>
    [SkippableFact]
    public void ASteadyFrame_AllocatesOnlyTheVisibleRowsText_WhateverTheLogHolds()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Draw(20_000);
        Console.WriteLine($"[MessageLogCostRail] 20 000 messages: {LastBytesPerFrame} B/frame");
        Assert.True(LastBytesPerFrame < 48 * 1024, $"{LastBytesPerFrame} B/frame");
    }

    [Fact]
    public void ASource_KeepsAtMostMaxRetained_DroppingTheOldestBlock()
    {
        var buffer = new List<MessageLogEntry>();
        MessageLogEntry E(int i) => new(DateTime.UtcNow, LogSeverity.Info, "x", "m" + i, Array.Empty<LogChunk>());
        for (int i = 0; i < 25_000; i++) MessageLogLimits.Add(buffer, E(i));
        Assert.InRange(buffer.Count, MessageLogLimits.MaxRetained - MessageLogLimits.TrimBlock, MessageLogLimits.MaxRetained);
        Assert.Equal("m24999", buffer[^1].Message);          // the newest is kept
        Assert.NotEqual("m0", buffer[0].Message);            // the oldest is not
    }

    [Fact]
    public void EveryMessageSource_AppendsThroughTheBound_AndTheWindowBuildsItsViewModelOnlyWhenCapturing()
    {
        string root = TestRepo.Root;
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { root }.Concat(p).ToArray()));
        foreach (var rel in new[] { new[] { "FDP", "Engine", "Fdp.Core", "Logging", "NLogMessageLogTarget.cs" }, new[] { "FDP", "Engine", "Fdp.Core", "Logging", "AiBehaviorLogTarget.cs" },
                                    new[] { "FDP", "Engine", "Fdp.Core", "Logging", "HotReloadMessageLogSource.cs" }, new[] { "FDP", "Toolkits", "Fdp.Toolkits", "Behavior", "Events", "BehaviorFaultLog.cs" } })
        {
            var text = Src(rel);
            Assert.Contains("MessageLogLimits.Add(_messages", text);
            Assert.DoesNotContain("_messages.Add(", text);
        }
        Assert.Contains("if (PanelSnapshot.CaptureEnabled) BuildAndPublish();", Src("FDP", "Engine", "Fdp.Presentation", "ImGui", "Windows", "MessageLogWindow.cs"));
    }
}
