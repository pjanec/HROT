using System;
using Fdp.Core.Logging;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐ <b>ONE sentence shape, and one fallback, for every "this tool did nothing" message.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7d.
    ///
    /// <para>🔒 <b>Ruling 49 applied to a tool:</b> <i>"nothing happened"</i> is indistinguishable from
    /// <i>"not implemented"</i> to the operator holding the mouse, so every refusal carries the TOOL and
    /// the REASON.</para>
    ///
    /// <para>⛔⛔ <b>Why this exists rather than three near-copies.</b> 📐 Measured <c>2026-09-09</c> during
    /// the unification pass: the same class of event was phrased three ways and logged under two
    /// categories — <c>ScenarioToolRegistrations</c> said <i>"tool 'Edit' did nothing — nothing is
    /// selected."</i>, <c>ToolController</c> said <i>"tool 'x' is not registered on this host"</i> (no verb,
    /// no full stop), and each re-implemented the same <c>sink ?? log</c> fallback. ⇒ an operator reading
    /// the log saw two unrelated-looking messages for one situation, and a fourth caller would have
    /// invented a fifth phrasing.</para>
    ///
    /// <para>⭐ The sink is a parameter rather than ambient state because each layer already receives its
    /// own <c>reportUnserviceable</c> — a rail injects a recorder, production leaves it null and gets the
    /// FDP log.</para>
    /// </summary>
    public static class ToolReport
    {
        /// <summary>
        /// Say that <paramref name="tool"/> did nothing, and why.
        /// </summary>
        /// <param name="sink">Where the message goes; <see langword="null"/> ⇒ the FDP log.</param>
        /// <param name="tool">The tool's name or id — whatever the caller knows it by.</param>
        /// <param name="reason">
        /// The half that varies, phrased to complete the sentence: <c>"nothing is selected"</c>,
        /// <c>"this host composes no spawn adapter"</c>. ⛔ No leading capital, no trailing full stop —
        /// this method owns the punctuation so every message reads the same.
        /// </param>
        public static void Unserviceable(Action<string>? sink, object tool, string reason)
            => Say(sink, $"tool '{tool}' did nothing — {reason}.");

        /// <summary>
        /// The raw channel, for a message that is not about one tool (the drain's <i>"no arbiter wired"</i>).
        /// ⭐ Prefer <see cref="Unserviceable"/> whenever a tool can be named.
        /// </summary>
        public static void Say(Action<string>? sink, string message)
        {
            if (sink != null) sink(message);
            else FdpLog<ToolController>.Info("[Tools] {0}", message);
        }
    }
}
