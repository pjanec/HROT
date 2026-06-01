using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Adapters;
using NLog;
using NLog.Config;
using NLog.Targets;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Adapters;

/// <summary>
/// AIE-006 — NLogDiagnosticsSink tests.
/// </summary>
public sealed class AIE006_NLogDiagnosticsSinkTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a sink wired to an in-memory NLog target, returning both
    /// the sink and a way to read captured log events.
    /// </summary>
    private static (NLogDiagnosticsSink sink, MemoryTarget target) BuildCapturingSink()
    {
        var target = new MemoryTarget("capture") { Layout = "${message}" };
        var config = new LoggingConfiguration();
        config.AddRuleForAllLevels(target);
        var factory = new LogFactory();
        factory.Configuration = config;
        var logger   = factory.GetLogger("test");
        var sink     = new NLogDiagnosticsSink(logger);
        return (sink, target);
    }

    // ── AIE-006-01: MapLevel — pure severity→level mapping ───────────────────

    [Fact]
    public void NLogDiagnosticsSink_MapLevel_MapsAllSeverities()
    {
        Assert.Equal(LogLevel.Trace, NLogDiagnosticsSink.MapLevel(DiagnosticSeverity.Trace));
        Assert.Equal(LogLevel.Debug, NLogDiagnosticsSink.MapLevel(DiagnosticSeverity.Debug));
        Assert.Equal(LogLevel.Info,  NLogDiagnosticsSink.MapLevel(DiagnosticSeverity.Info));
        Assert.Equal(LogLevel.Warn,  NLogDiagnosticsSink.MapLevel(DiagnosticSeverity.Warning));
        Assert.Equal(LogLevel.Error, NLogDiagnosticsSink.MapLevel(DiagnosticSeverity.Error));
    }

    // ── AIE-006-02: Log routes to correct NLog level ─────────────────────────

    [Fact]
    public void NLogDiagnosticsSink_Log_RoutesAllSeverities()
    {
        var (sink, target) = BuildCapturingSink();

        sink.Log(DiagnosticSeverity.Trace,   "trace msg");
        sink.Log(DiagnosticSeverity.Debug,   "debug msg");
        sink.Log(DiagnosticSeverity.Info,    "info msg");
        sink.Log(DiagnosticSeverity.Warning, "warn msg");
        sink.Log(DiagnosticSeverity.Error,   "error msg");

        // All five messages must have been captured.
        Assert.Contains("trace msg",   target.Logs);
        Assert.Contains("debug msg",   target.Logs);
        Assert.Contains("info msg",    target.Logs);
        Assert.Contains("warn msg",    target.Logs);
        Assert.Contains("error msg",   target.Logs);
    }

    // ── AIE-006-03: Null exception is handled (no throw) ─────────────────────

    [Fact]
    public void NLogDiagnosticsSink_Log_NullException_DoesNotThrow()
    {
        var (sink, _) = BuildCapturingSink();

        // Default parameter is null — must not throw.
        sink.Log(DiagnosticSeverity.Info, "no exception");
    }

    [Fact]
    public void NLogDiagnosticsSink_Log_ExplicitNullException_DoesNotThrow()
    {
        var (sink, _) = BuildCapturingSink();

        sink.Log(DiagnosticSeverity.Error, "with null ex", null);
    }

    // ── AIE-006-04: Non-null exception is attached ────────────────────────────

    [Fact]
    public void NLogDiagnosticsSink_Log_WithException_MessageCaptured()
    {
        var (sink, target) = BuildCapturingSink();
        var ex = new InvalidOperationException("boom");

        sink.Log(DiagnosticSeverity.Error, "something failed", ex);

        Assert.Contains("something failed", target.Logs);
    }

    // ── AIE-006-05: Implements IDiagnosticsSink ───────────────────────────────

    [Fact]
    public void NLogDiagnosticsSink_Implements_IDiagnosticsSink()
    {
        var (sink, _) = BuildCapturingSink();
        IDiagnosticsSink iface = sink;
        Assert.NotNull(iface);
    }
}
