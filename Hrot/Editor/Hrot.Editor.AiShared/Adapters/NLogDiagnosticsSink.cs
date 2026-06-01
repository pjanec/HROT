using NLog;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// <see cref="IDiagnosticsSink"/> that routes NodeEdit diagnostics to the
/// engine's NLog logger.
/// <list type="table">
/// <listheader><term>NodeEdit severity</term><description>NLog level</description></listheader>
/// <item><term>Trace</term><description>Trace</description></item>
/// <item><term>Debug</term><description>Debug</description></item>
/// <item><term>Info</term><description>Info</description></item>
/// <item><term>Warning</term><description>Warn</description></item>
/// <item><term>Error</term><description>Error</description></item>
/// </list>
/// Null exceptions are handled silently; non-null exceptions are attached via
/// <c>LogEventInfo.Exception</c>.
/// </summary>
public sealed class NLogDiagnosticsSink : IDiagnosticsSink
{
    private readonly Logger _logger;

    /// <summary>
    /// Creates the sink using <paramref name="logger"/> for output.
    /// Allows test-injection of a custom logger or logger factory.
    /// </summary>
    public NLogDiagnosticsSink(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates the sink using a logger named after the calling assembly.
    /// </summary>
    public NLogDiagnosticsSink()
        : this(LogManager.GetCurrentClassLogger()) { }

    /// <inheritdoc/>
    public void Log(DiagnosticSeverity severity, string message, Exception? exception = null)
    {
        var level = MapLevel(severity);
        if (!_logger.IsEnabled(level)) return;

        var evt = LogEventInfo.Create(level, _logger.Name, exception, null, message);
        _logger.Log(typeof(NLogDiagnosticsSink), evt);
    }

    // ── Pure static helper (unit-testable) ───────────────────────────────────

    /// <summary>
    /// Maps a <see cref="DiagnosticSeverity"/> to the corresponding
    /// <see cref="LogLevel"/>.  Pure function — no logger state needed.
    /// </summary>
    public static LogLevel MapLevel(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Trace   => LogLevel.Trace,
        DiagnosticSeverity.Debug   => LogLevel.Debug,
        DiagnosticSeverity.Info    => LogLevel.Info,
        DiagnosticSeverity.Warning => LogLevel.Warn,
        DiagnosticSeverity.Error   => LogLevel.Error,
        _                          => LogLevel.Info,
    };
}
