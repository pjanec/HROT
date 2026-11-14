using System;
using System.Collections.Generic;
using Fdp.Core.Logging;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Windows;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>MessageLogWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> (the "plain panel has no id, the
/// HOST registers" gotcha — <c>MessageLogPanel</c> is the plain panel, <c>MessageLogWindow</c> the host).
///
/// <para>⭐⭐ Mirrors <c>ThePilotPanelDumpsWhatItDrawsTests</c> — same BUILD/CAPTURE shape, same headless
/// rationale. ⚠ <c>PanelSnapshot</c> is process-global static state; every case resets it.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class MessageLogWindowDumpsItsTabsTests : IDisposable
{
    public MessageLogWindowDumpsItsTabsTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    /// <summary>Minimal <see cref="IMessageLogSource"/> stub — a fixed list, no live event wiring needed
    /// for these rails.</summary>
    private sealed class FakeSource : IMessageLogSource
    {
        private readonly List<MessageLogEntry> _entries;

        public FakeSource(string sourceId, string displayName, params MessageLogEntry[] entries)
        {
            SourceId    = sourceId;
            DisplayName = displayName;
            _entries    = new List<MessageLogEntry>(entries);
        }

        public string SourceId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<MessageLogEntry> GetMessages() => _entries;
        public void Clear() => _entries.Clear();
#pragma warning disable CS0067
        public event Action<MessageLogEntry>? OnMessageAdded;
#pragma warning restore CS0067
    }

    private static MessageLogEntry Entry(string logger, string message, LogSeverity severity = LogSeverity.Info)
        => new(DateTime.UtcNow, severity, logger, message, Array.Empty<LogChunk>());

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The window is instrumented the moment it is CONSTRUCTED — before it has ever drawn.
    /// ⛔ Would go red if <c>DeclareInstrumented</c> drifted into the draw.
    /// </summary>
    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("fdp_message_log", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new MessageLogWindow(new MessageLogRegistry());

        Assert.Contains("fdp_message_log", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("fdp_message_log", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("fdp_message_log"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ Build headless (no ImGui context — <c>SimulateDrawClientArea</c> is the BUILD+CAPTURE portion
    /// only), read the model over the snapshot, assert a real field: the tab's filtered message.
    /// </summary>
    [Fact]
    public void AfterABuild_TheDumpCarriesTheSourcesMessage()
    {
        PanelSnapshot.CaptureEnabled = true;
        var registry = new MessageLogRegistry();
        registry.RegisterSource(new FakeSource("nlog", "General", Entry("Sim", "hello world")));
        var window = new MessageLogWindow(registry);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("fdp_message_log");
        Assert.NotNull(vm);
        Assert.Equal("fdp_message_log", vm!.PanelId);
        Assert.Equal(MessageLogWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        var tabs = dump["tabs"]!.AsArray();
        Assert.Single(tabs);
        Assert.Equal("nlog",   tabs[0]!["sourceId"]!.GetValue<string>());
        Assert.Equal(1,        tabs[0]!["filteredCount"]!.GetValue<int>());
        var rows = tabs[0]!["filteredMessages"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("hello world", rows[0]!["message"]!.GetValue<string>());
    }

    /// <summary>⭐⭐ A hidden severity removes the row from the dump — the SAME filter
    /// <c>DrawMessageList</c> applies, exercised headless.</summary>
    [Fact]
    public void AHiddenSeverity_IsFilteredOutOfTheDump()
    {
        PanelSnapshot.CaptureEnabled = true;
        var registry = new MessageLogRegistry();
        registry.RegisterSource(new FakeSource("nlog", "General", Entry("Sim", "noisy", LogSeverity.Trace)));
        var window = new MessageLogWindow(registry);

        window.SimulateDrawClientArea();   // Trace is hidden by default (TabState ctor)

        var dump = PanelSnapshot.TryGet("fdp_message_log")!.Dump();
        var tab = dump["tabs"]!.AsArray()[0]!;
        Assert.Equal(1, tab["totalCount"]!.GetValue<int>());
        Assert.Equal(0, tab["filteredCount"]!.GetValue<int>());
        Assert.Contains(tab["hiddenSeverities"]!.AsArray(), n => n!.GetValue<string>() == "Trace");
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    /// <summary>⭐ Production default: capture OFF ⇒ nothing published, panel still known instrumented.</summary>
    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var registry = new MessageLogRegistry();
        registry.RegisterSource(new FakeSource("nlog", "General", Entry("Sim", "hello")));
        var window = new MessageLogWindow(registry);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("fdp_message_log", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.Single(vm.Tabs);
    }
}
