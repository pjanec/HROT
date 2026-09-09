using System.Runtime.CompilerServices;
using NLog;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// BP-108 — the sink generated blueprint code writes <c>Print String</c> messages to.
    ///
    /// <para>
    /// Logs under <c>AI.Behavior.Blueprint</c>, which matches the prefix-anchored
    /// <c>"AI.Behavior*"</c> NLog rule that routes into <see cref="AiBehaviorLogTarget"/> — the
    /// dedicated <b>"AI Behaviors"</b> tab in the editor's message log.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Why this lives in <c>Fdp.Core.Logging</c> and not next to <c>BehaviorLog</c>.</b> Two
    /// alternatives were ruled out against the code:
    /// <list type="bullet">
    ///   <item><b><c>FdpLog&lt;T&gt;</c></b> — its logger name is <c>typeof(T).FullName</c>, so a
    ///     generated blueprint class would log under <c>Hrot.AI.Behaviors.Generated.…</c>, which the
    ///     prefix-anchored rule does <b>not</b> match. The messages would simply never reach the tab.</item>
    ///   <item><b><c>Hrot.AI.Behaviors.Logging.BehaviorLog</c></b> — correct logger name, <b>wrong
    ///     assembly</b>. <c>Hrot.AI.Behaviors</c> is not guaranteed loaded when
    ///     <c>MetadataReferenceResolver.ForRuntimeAssemblies</c> snapshots the AppDomain, so generated
    ///     code referencing it fails <b>CS0246 on hot reload only</b> — an unattributable error, and
    ///     exactly the shape of BP-62.</item>
    /// </list>
    /// <c>CSharpEmitter.EmitUsings</c> emits <c>using Fdp.Core;</c> unconditionally and the capture
    /// target itself lives in this assembly, so if the sink can capture at all, this helper is loadable.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Always call through the matching <c>Is…Enabled</c> probe.</b> The emitter does this, and it
    /// is the entire performance story: a <c>Print String</c> node in a per-tick graph must cost one
    /// bool read when its level is off, never a formatted string.
    /// </para>
    /// </summary>
    public static class BlueprintLog
    {
        private static readonly Logger s_log = LogManager.GetLogger("AI.Behavior.Blueprint");

        // ── Level probes — guard every call site with these ──────────────────

        /// <summary>True when Trace-level blueprint logging is active.</summary>
        public static bool IsTraceEnabled => s_log.IsTraceEnabled;

        /// <summary>True when Debug-level blueprint logging is active.</summary>
        public static bool IsDebugEnabled => s_log.IsDebugEnabled;

        /// <summary>True when Info-level blueprint logging is active.</summary>
        public static bool IsInfoEnabled => s_log.IsInfoEnabled;

        /// <summary>True when Warn-level blueprint logging is active.</summary>
        public static bool IsWarnEnabled => s_log.IsWarnEnabled;

        /// <summary>True when Error-level blueprint logging is active.</summary>
        public static bool IsErrorEnabled => s_log.IsErrorEnabled;

        // ── Emit targets ─────────────────────────────────────────────────────

        /// <summary>Writes a Trace-level blueprint message.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string message) => s_log.Trace(message);

        /// <summary>Writes a Debug-level blueprint message.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string message) => s_log.Debug(message);

        /// <summary>Writes an Info-level blueprint message.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string message) => s_log.Info(message);

        /// <summary>Writes a Warn-level blueprint message.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warn(string message) => s_log.Warn(message);

        /// <summary>Writes an Error-level blueprint message.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string message) => s_log.Error(message);
    }
}
