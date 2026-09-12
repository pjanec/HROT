#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
using Stride.Core.Diagnostics;

namespace HrotStrideApp;

/// <summary>
/// Process-wide logging bootstrap for the <c>editor_stride</c> (HrotStrideApp) process.
///
/// <para>
/// <b>Why this exists:</b> almost all remaining HrotStrideApp work runs only on the GPU
/// render path, which cannot be exercised headlessly in unit tests. A persistent NLog
/// file is therefore the primary debugging channel — it must capture our own diagnostics,
/// Stride engine warnings/errors, and (critically) any unhandled / GPU-path crash stack
/// trace. This mirrors the canonical HROT NLog setup in
/// <c>Hrot.ClusterRunner/Program.cs</c> (a <see cref="LoggingConfiguration"/> with a rolling
/// <see cref="FileTarget"/> under <c>&lt;BaseDirectory&gt;/logs</c>, rules Trace→Fatal).
/// </para>
///
/// <para>
/// <b>Log file:</b> <c>&lt;AppContext.BaseDirectory&gt;/logs/editor_stride.log</c> with rolling
/// archives <c>editor_stride.{#}.log</c> (10 archives, 50 MB each). The active file always
/// has the same name so it is easy to find; older runs roll into the numbered archives.
/// </para>
///
/// <para>
/// <b>WinExe-safe:</b> nothing here depends on a console window. The file target works whether
/// the project is built as <c>WinExe</c> or <c>Exe</c>; no console is required.
/// </para>
///
/// <para>
/// Call <see cref="Configure"/> once at process startup (before the game runs) and
/// <see cref="Shutdown"/> on exit. Both are idempotent.
/// </para>
/// </summary>
public static class StrideLogging
{
    private static readonly object s_lock = new();
    private static bool s_configured;

    /// <summary>NLog logger that the Stride <c>GlobalLogger</c> bridge forwards into.</summary>
    private static NLog.Logger? s_strideLogger;

    /// <summary>The forwarding delegate, kept so it can be detached on shutdown.</summary>
    private static Action<ILogMessage>? s_globalLoggerHandler;

    /// <summary>
    /// Builds the NLog configuration (rolling file target, Trace→Fatal), installs the
    /// Stride <c>GlobalLogger</c> → NLog bridge, and registers unhandled-exception capture.
    /// Idempotent — safe to call more than once; only the first call has any effect.
    /// </summary>
    public static void Configure()
    {
        lock (s_lock)
        {
            if (s_configured)
                return;
            s_configured = true;

            // ── 1. NLog file target (mirrors Hrot.ClusterRunner/Program.cs) ────────
            string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);

            var logConfig = new LoggingConfiguration();

            var fileTarget = new FileTarget("logFile")
            {
                Layout =
                    "[${longdate}] [${level:uppercase=true}] [${logger:shortName=true}] " +
                    "${message} ${exception:format=tostring}",
                FileName         = Path.Combine(logDir, "editor_stride.log"),
                ArchiveFileName  = Path.Combine(logDir, "editor_stride.{#}.log"),
                ArchiveNumbering = ArchiveNumberingMode.Rolling,
                MaxArchiveFiles  = 10,
                ArchiveAboveSize = 50 * 1024 * 1024,
                KeepFileOpen     = true,
                ConcurrentWrites = false,
            };

            // Capture everything Trace→Fatal to the file (matches ClusterRunner's file rule
            // intent; ClusterRunner uses Debug→Fatal for its file, but the task asks for
            // Trace→Fatal here so even the most verbose diagnostics are captured).
            logConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget);

            LogManager.Configuration = logConfig;

            // ── 2. Bridge Stride GlobalLogger → NLog ──────────────────────────────
            s_strideLogger = LogManager.GetLogger("Stride");

            // GlobalMessageLogged is Action<ILogMessage> (VERIFIED via reflection against
            // Stride.Core 4.2.1.2487). Forward each engine message at the mapped level.
            s_globalLoggerHandler = ForwardStrideMessage;
            GlobalLogger.GlobalMessageLogged += s_globalLoggerHandler;

            // Ensure Info+ flows from the engine. Logger.MinimumLevelEnabled is a *static*
            // property gating which message types the engine emits at all (VERIFIED).
            // Default leaves Verbose/Debug off; lower it to Verbose so we get the most
            // useful asset/render/physics diagnostics into the file.
            if (Stride.Core.Diagnostics.Logger.MinimumLevelEnabled > LogMessageType.Verbose)
                Stride.Core.Diagnostics.Logger.MinimumLevelEnabled = LogMessageType.Verbose;

            // ── 3. Capture unhandled exceptions (critical for GPU-path crashes) ────
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException     += OnUnobservedTaskException;

            var startupLog = LogManager.GetCurrentClassLogger();
            startupLog.Info("NLog file logging initialized. Log file: {0}",
                Path.Combine(logDir, "editor_stride.log"));
        }
    }

    /// <summary>
    /// Flushes and shuts NLog down so the log file is complete on exit.
    /// Also detaches the Stride <c>GlobalLogger</c> bridge and exception handlers.
    /// Idempotent.
    /// </summary>
    public static void Shutdown()
    {
        lock (s_lock)
        {
            if (!s_configured)
                return;
            s_configured = false;

            if (s_globalLoggerHandler != null)
            {
                GlobalLogger.GlobalMessageLogged -= s_globalLoggerHandler;
                s_globalLoggerHandler = null;
            }
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException     -= OnUnobservedTaskException;

            try { LogManager.Flush(); } catch { /* best effort */ }
            LogManager.Shutdown();
        }
    }

    // ── Stride → NLog forwarding ───────────────────────────────────────────────

    /// <summary>
    /// Forwards a single Stride <see cref="ILogMessage"/> to the NLog "Stride" logger at the
    /// mapped level. Stride's <see cref="LogMessageType"/> maps to NLog levels as:
    /// Verbose/Debug→Debug, Info→Info, Warning→Warn, Error→Error, Fatal→Fatal.
    /// </summary>
    private static void ForwardStrideMessage(ILogMessage message)
    {
        var logger = s_strideLogger;
        if (logger == null || message == null)
            return;

        LogLevel level = message.Type switch
        {
            LogMessageType.Debug   => LogLevel.Debug,
            LogMessageType.Verbose => LogLevel.Debug,
            LogMessageType.Info    => LogLevel.Info,
            LogMessageType.Warning => LogLevel.Warn,
            LogMessageType.Error   => LogLevel.Error,
            LogMessageType.Fatal   => LogLevel.Fatal,
            _                      => LogLevel.Info,
        };

        if (!logger.IsEnabled(level))
            return;

        string text = message.Text ?? string.Empty;
        string module = message.Module ?? "?";

        // Stride carries exception detail as a flattened ExceptionInfo (Message/StackTrace/
        // TypeFullName), not a System.Exception. Append it to the text so the file keeps it.
        var ex = message.ExceptionInfo;
        if (ex != null)
            text = $"{text}{Environment.NewLine}{ex.TypeFullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";

        logger.Log(level, "[{0}] {1}", module, text);
    }

    // ── Unhandled-exception capture ────────────────────────────────────────────

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var log = LogManager.GetCurrentClassLogger();
        if (e.ExceptionObject is Exception ex)
            log.Fatal(ex, "Unhandled AppDomain exception (terminating={0}).", e.IsTerminating);
        else
            log.Fatal("Unhandled AppDomain exception (non-Exception object: {0}, terminating={1}).",
                e.ExceptionObject, e.IsTerminating);

        // The process is about to die — make sure the stack trace reaches disk.
        try { LogManager.Flush(); } catch { /* best effort */ }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var log = LogManager.GetCurrentClassLogger();
        log.Fatal(e.Exception, "Unobserved TaskScheduler exception.");
        // Mark observed so it does not escalate further; we've recorded it.
        e.SetObserved();
        try { LogManager.Flush(); } catch { /* best effort */ }
    }
}
