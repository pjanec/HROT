using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using NLog;

namespace Hrot.AI.Behaviors.Logging
{
    /// <summary>
    /// Static structured-logging utility for BTree and HSM behavior nodes.
    ///
    /// <para>Emits to the <c>AI.Behavior</c> NLog logger, which is routed to
    /// <c>AiBehaviorLogTarget</c> (the dedicated "AI Behaviors" tab) by the
    /// process-wide NLog configuration in <c>Program.cs</c>.  Logs are also
    /// written to the rolling file target via the global catch-all rule.</para>
    ///
    /// <para>Hot-path design: level checks run first so string allocation and ECS
    /// component access are skipped entirely when the level is disabled.</para>
    ///
    /// <para>Structured message format (queryable in Elastic/Kibana/file logs):
    /// <c>Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}</c></para>
    ///
    /// <para>For cold-path parameter parsers that have no entity context, use the
    /// <c>ParseWarn</c> / <c>ParseError</c> overloads.</para>
    /// </summary>
    public static class BehaviorLog
    {
        private static readonly Logger s_log = LogManager.GetLogger("AI.Behavior");

        // ── Level probes (allow call sites to skip string construction) ─────

        /// <summary>Returns <c>true</c> when Warn-level AI behavior logging is active.</summary>
        public static bool IsDebugEnabled => s_log.IsDebugEnabled;

        /// <summary>Returns <c>true</c> when Warn-level AI behavior logging is active.</summary>
        public static bool IsTraceEnabled => s_log.IsTraceEnabled;

        /// <summary>Returns <c>true</c> when Warn-level AI behavior logging is active.</summary>
        public static bool IsWarnEnabled  => s_log.IsWarnEnabled;

        /// <summary>Returns <c>true</c> when Error-level AI behavior logging is active.</summary>
        public static bool IsErrorEnabled => s_log.IsErrorEnabled;

        // ── BTreeContext overloads ────────────────────────────────────────────

        /// <summary>
        /// Logs a debug message from a BTree node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Debug(ref BTreeContext ctx, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsDebugEnabled) return;
            LogWithContext(LogLevel.Debug, ref ctx, message, actionName);
        }

        /// <summary>
        /// Logs a trace message from a BTree node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Trace(ref BTreeContext ctx, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsTraceEnabled) return;
            LogWithContext(LogLevel.Trace, ref ctx, message, actionName);
        }

        /// <summary>
        /// Logs a warning from a BTree node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Warn(ref BTreeContext ctx, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsWarnEnabled) return;
            LogWithContext(LogLevel.Warn, ref ctx, message, actionName);
        }

        /// <summary>
        /// Logs an error from a BTree node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Error(ref BTreeContext ctx, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsErrorEnabled) return;
            LogWithContext(LogLevel.Error, ref ctx, message, actionName);
        }

        // ── Entity + EntityRepository overloads (for HSM / SharedAiAction) ──

        /// <summary>
        /// Logs a debug message from an HSM or shared-AI node that receives <c>Entity</c>
        /// and <c>EntityRepository</c> directly instead of a <c>BTreeContext</c>.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Debug(Entity self, EntityRepository repo, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsDebugEnabled) return;
            LogWithEntity(LogLevel.Debug, self, repo, message, actionName);
        }

        /// <summary>
        /// Logs a trace message from an HSM or shared-AI node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Trace(Entity self, EntityRepository repo, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsTraceEnabled) return;
            LogWithEntity(LogLevel.Trace, self, repo, message, actionName);
        }

        /// <summary>
        /// Logs a warning from an HSM or shared-AI node that receives <c>Entity</c>
        /// and <c>EntityRepository</c> directly instead of a <c>BTreeContext</c>.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Warn(Entity self, EntityRepository repo, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsWarnEnabled) return;
            LogWithEntity(LogLevel.Warn, self, repo, message, actionName);
        }

        /// <summary>
        /// Logs an error from an HSM or shared-AI node.
        /// <c>[CallerMemberName]</c> automatically captures the method name as the node name.
        /// </summary>
        public static void Error(Entity self, EntityRepository repo, string message,
            [CallerMemberName] string actionName = "")
        {
            if (!s_log.IsErrorEnabled) return;
            LogWithEntity(LogLevel.Error, self, repo, message, actionName);
        }

        // ── Cold-path parse overloads (no entity context) ────────────────────

        /// <summary>
        /// Logs a warning from a cold-path parameter parser.
        /// No entity context is available; only the caller method name is captured.
        /// </summary>
        public static void ParseWarn(string message,
            [CallerMemberName] string callerName = "")
        {
            if (!s_log.IsWarnEnabled) return;
            s_log.Warn("Node:[{ActionName}] | {UserMessage}", callerName, message);
        }

        /// <summary>
        /// Logs an error from a cold-path parameter parser.
        /// No entity context is available; only the caller method name is captured.
        /// </summary>
        public static void ParseError(string message,
            [CallerMemberName] string callerName = "")
        {
            if (!s_log.IsErrorEnabled) return;
            s_log.Error("Node:[{ActionName}] | {UserMessage}", callerName, message);
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static void LogWithContext(LogLevel level, ref BTreeContext ctx,
            string message, string actionName)
        {
            int behaviorHash = 0;
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
                behaviorHash = ctx.World.GetComponent<BehaviorState>(ctx.Self).ActiveBehaviorHash;

            s_log.Log(level,
                "Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}",
                ctx.Self.Index, behaviorHash, actionName, message);
        }

        private static void LogWithEntity(LogLevel level, Entity self, EntityRepository repo,
            string message, string actionName)
        {
            int behaviorHash = 0;
            if (repo.HasComponent<BehaviorState>(self))
                behaviorHash = repo.GetComponent<BehaviorState>(self).ActiveBehaviorHash;

            s_log.Log(level,
                "Entity:[{EntityId}] Behavior:[{BehaviorHash}] Node:[{ActionName}] | {UserMessage}",
                self.Index, behaviorHash, actionName, message);
        }
    }
}
