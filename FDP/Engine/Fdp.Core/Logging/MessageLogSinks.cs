using System.Collections.Generic;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-001</c> — the sink list a diagnostics reader should be given.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §2.1.
    ///
    /// <para>⭐⭐ <b>One helper because there are TWO composition roots and one rule.</b> 📐 A node's
    /// <c>DebugApiService</c> is built either by <c>EditorSubsystem</c> *(the full surface)* or by
    /// <c>Hrot.ClusterRunner/Program.cs</c> *(the cluster-limited one)*, and **neither passed any sinks**
    /// — so <c>GET /logs</c> answered <c>[]</c> on every host while the same records fed the on-screen
    /// Message Log. ⛔ Two hand-written sink lists would be two places for that to drift back.</para>
    ///
    /// <para>⭐⭐⭐ <b>Why the two <c>SharedInstance</c> targets and not just the registry.</b> 📐 Measured:
    /// <see cref="MessageLogRegistry"/> is an INSTANCE, reached through
    /// <c>WindowManager.MessageLogRegistry</c> — ⛔ so a **headless node has no registry at all**, which is
    /// exactly the SimHost case this route exists to serve. ⭐ The two NLog targets are process-wide statics
    /// that <c>Program.Main</c> installs as logging rules for **every** mode, headless included ⇒ they are
    /// populated on a node that has no window. ⚠ That is why they are included unconditionally rather than
    /// only when a registry is absent.</para>
    ///
    /// <para>⚠ <b>The registry is still read when present, and it is not redundant:</b> it carries the
    /// host-specific sources — <c>HotReloadMessageLogSource</c> and anything a subsystem registered from
    /// <c>RegisterWindows</c> — which no static can know about.</para>
    /// </summary>
    public static class MessageLogSinks
    {
        /// <summary>
        /// Builds the sink list for <c>GET /logs</c>: everything <paramref name="registry"/> holds, plus the
        /// process-wide NLog targets, de-duplicated by reference.
        /// </summary>
        /// <param name="registry">
        /// The host's registry when it has one *(<c>WindowManager.MessageLogRegistry</c>)*, or
        /// <see langword="null"/> on a headless node. ⛔ <see langword="null"/> is a normal case, not a
        /// degraded one — the statics below still answer.
        /// </param>
        /// <returns>
        /// A snapshot list. ⚠ The SOURCES are live objects, so records logged after this call are still
        /// visible; only the SET of sources is fixed here.
        /// </returns>
        public static IReadOnlyList<IMessageLogSource> ForDiagnostics(MessageLogRegistry? registry = null)
        {
            var sinks = new List<IMessageLogSource>(4);

            void Add(IMessageLogSource? s)
            {
                // ⛔ Reference equality, deliberately: the registry is normally SEEDED with
                //   NLogMessageLogTarget.SharedInstance (MessageLogHostWiring.CreateAndRegister does exactly
                //   that), so without this the editor host would read the global NLog ring TWICE and every
                //   line would appear duplicated in GET /logs.
                if (s != null && !sinks.Contains(s)) sinks.Add(s);
            }

            if (registry != null)
                foreach (var source in registry.Sources) Add(source);

            Add(NLogMessageLogTarget.SharedInstance);
            Add(AiBehaviorLogTarget.SharedInstance);

            return sinks;
        }
    }
}
