using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Preview
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE PLACE THAT KNOWS WHAT A PREVIEW SAVES.</b>
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §4 ④/⑤ · §4c.
    ///
    /// <para>🔒 <b>User, `2026-08-23`:</b> <i>"a preview is a dry run … for repeated runs of the same we
    /// would like to have same ids"</i>, and <i>"the preview could work also in distributed env so no
    /// hardwiring directly just for editor, reset must be cluster wide"</i>.</para>
    ///
    /// <para>⭐⭐⭐ <b>Why it lives HERE, in <c>Fdp.Toolkits</c>, and not in either handler.</b> 📐 Measured
    /// `2026-08-23`: there are <b>TWO</b> preview handlers —
    /// <c>Fdp.Toolkit.Orchestration.Handlers.ReferencePreviewHandler</c>, registered on <b>five</b>
    /// production <c>ClusterSlave</c>s *(IG, CGF ×2, SimHost, ExCon)* and driven by the 2PC broadcast, and
    /// <c>Hrot.Network.Orchestration.PreviewClusterOpHandler</c>, registered on <b>none</b> and driven
    /// directly by the editor. ⇒ ⛔⛔ <b>putting the bracket in either one would give that path the fix and
    /// leave the other without it</b> — for the editor-only handler that is exactly the hardwiring the
    /// user's steer forbids. ⭐ Both assemblies can reach this one, so there is ONE implementation of
    /// "what preview saves" even while <c>HN-016</c>'s duplicate handlers stand.</para>
    ///
    /// <para>⭐⭐ <b>How it is cluster-wide with NO new protocol.</b> Both handlers answer
    /// <c>PrepareState(LoadingPreview / UnloadingPreview)</c>: the master broadcasts, and <b>every node
    /// commits locally</b>. ⇒ each node captures and restores <b>its own</b> state. ⛔ Nothing here talks to
    /// another node, and ⛔ nothing here touches a central id authority — 📌 §4c: the central allocator
    /// stays where it is, for fresh allocations.</para>
    /// </summary>
    public sealed class PreviewStateBracket
    {
        private readonly IReadOnlyList<IPreviewRewindable> _participants;
        private Dictionary<IPreviewRewindable, object>? _captured;

        /// <summary>
        /// ⭐ The participants, in the order they will be restored.
        /// </summary>
        /// <param name="participants">
        /// ⚠ <b>An EMPTY list is legal and means "this node has no non-ECS state to rewind"</b> — that is
        /// true of ExCon, IG and the CGF skeleton, which pass a null repo too. ⛔ It is not an error and
        /// must not log like one.
        /// </param>
        public PreviewStateBracket(IEnumerable<IPreviewRewindable> participants)
            => _participants = (participants ?? throw new ArgumentNullException(nameof(participants)))
                               .Where(p => p != null).ToList();

        /// <summary>⭐ Exposed for rails: a rail must reach the CONSTRUCTED object, not the wiring source.</summary>
        public IReadOnlyList<string> ParticipantNames => _participants.Select(p => p.Name).ToList();

        /// <summary>
        /// ⭐⭐ <b>Names every participant that could not give a snapshot on the last <see cref="Capture"/>.</b>
        /// <para>⛔⛔ This is the honest answer to §4c's boundary: a pooled allocator with nothing to restore
        /// makes this preview NON-reproducible, and the operator/agent must be able to learn that rather
        /// than discover it from surprising ids. ⚠ Empty after a clean capture.</para>
        /// </summary>
        public IReadOnlyList<string> UnrestorableParticipants { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// ⭐ Called on preview ENTER, beside the repository snapshot.
        /// </summary>
        public void Capture()
        {
            var captured = new Dictionary<IPreviewRewindable, object>();
            var unrestorable = new List<string>();

            foreach (var p in _participants)
            {
                object? token;
                try
                {
                    token = p.Capture();
                }
                catch (Exception ex)
                {
                    // ⛔ A participant that throws must not abort the preview — the repo snapshot is the
                    //   load-bearing half. ⭐ But it IS reported as unrestorable, never swallowed.
                    FdpLog<PreviewStateBracket>.Warn(
                        $"[Preview] '{p.Name}' threw while capturing: {ex.Message}. " +
                        "This preview will not be reproducible.");
                    unrestorable.Add(p.Name);
                    continue;
                }

                if (token is null) unrestorable.Add(p.Name);
                else               captured[p] = token;
            }

            _captured = captured;
            UnrestorableParticipants = unrestorable;

            if (unrestorable.Count > 0)
                FdpLog<PreviewStateBracket>.Warn(
                    $"[Preview] {unrestorable.Count} of {_participants.Count} participant(s) cannot be "
                    + $"restored: {string.Join(", ", unrestorable)}. Ids will NOT repeat on the next preview.");
            else if (_participants.Count > 0)
                FdpLog<PreviewStateBracket>.Info(
                    $"[Preview] captured {captured.Count} non-ECS participant(s): "
                    + string.Join(", ", captured.Keys.Select(k => k.Name)));
        }

        /// <summary>
        /// ⭐ Called on preview EXIT, beside the repository rewind.
        /// <para>⚠ <b>Restore order is REVERSE of the participant order</b>, so a participant that depends on
        /// another being already-restored can be declared after it. 📐 Today none do — stated because the
        /// order is otherwise an invisible accident, and the fourth participant will care.</para>
        /// </summary>
        public void Restore()
        {
            if (_captured is null)
            {
                // ⚠ Exit without enter: the handler already warns about the missing repo snapshot; do not
                //   double-log, but do not silently pretend a restore happened either.
                return;
            }

            foreach (var p in _participants.Reverse())
            {
                if (!_captured.TryGetValue(p, out var token)) continue;   // was unrestorable at capture
                try
                {
                    p.Restore(token);
                }
                catch (Exception ex)
                {
                    FdpLog<PreviewStateBracket>.Warn(
                        $"[Preview] '{p.Name}' threw while restoring: {ex.Message}. " +
                        "State outside the repository may be stale.");
                }
            }

            FdpLog<PreviewStateBracket>.Info(
                $"[Preview] restored {_captured.Count} non-ECS participant(s).");
            _captured = null;
        }

        /// <summary>⭐ Preview aborted: drop the capture without touching anything.</summary>
        public void Discard() => _captured = null;
    }
}
