using System;
using System.Text.Json.Nodes;
using Hrot.Editor.AiShared.Documents;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>N0</c> — the perspective, read and switched over HTTP.</b>
    /// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N0</c> · §6 *(the capture protocol)*.
    ///
    /// <para>⛔⛔ <b>Why this is the gap that blocked the whole net.</b> 📐 A panel publishes its view-model
    /// only when its DRAW runs, and <b>only the active perspective draws</b> ⇒ without a way to switch,
    /// three of the four editor perspectives were invisible to the harness and the BTree, HSM and Blueprint
    /// panels could not be captured at all.</para>
    ///
    /// <para>⭐⭐ <b>Deliberately thin, and the validation is DELEGATED.</b>
    /// <c>WindowManager.SwitchPerspective</c> already refuses a name no window claims *(the perspective
    /// batch's <c>A0</c>, <c>BP-488</c>)*, so this group's job is to REPORT that refusal as a 400 — ⛔ not
    /// to re-implement the rule. 📌 Two answers to *"is this a real perspective?"* is how they drift.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ⭐⭐⭐ LATE-BOUND, and it has to be. 📐 Measured 2026-08-23: DebugApiService is constructed in
        //    EditorSubsystem.Initialize (~:1767) while the WindowManager does not exist until
        //    RegisterWindows (:2451) — the shell creates it, and Initialize runs first. ⇒ a constructor
        //    parameter is not available to be passed.
        // ⛔⛔ THIS IS THE SILENT-DEFAULT SHAPE (CLAUDE.md): a nullable field the composition root can
        //    forget to set. ⭐ Two controls, because the pattern has bitten this repo three times:
        //      ① Attach() is called on the line AFTER the switcher is constructed, not in some later
        //         block that a refactor can reorder away from it;
        //      ② a rail asserts it ON THE CONSTRUCTED OBJECT (the forwarding rail per dependency), so
        //         "the root held it and did not pass it" reddens instead of answering 503 forever.
        private IPerspectiveSwitcher? _perspectives;

        /// <summary>
        /// ⭐ Hands this service the perspective seam. Called from
        /// <c>EditorSubsystem.RegisterWindows</c>, where the window manager first exists.
        /// </summary>
        public void AttachPerspectives(IPerspectiveSwitcher perspectives)
            => _perspectives = perspectives ?? throw new ArgumentNullException(nameof(perspectives));

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasPerspectiveAccess => _perspectives != null;

        /// <summary>
        /// <c>GET /perspectives</c> — every perspective a registered window claims, plus the active one.
        /// </summary>
        /// <remarks>
        /// ⭐ <c>current</c> is reported alongside the list because the two can legitimately disagree with a
        /// caller's expectation: a switch to an unclaimed name is a logged no-op, so <c>current</c> is the
        /// only honest answer to <i>"did my switch take?"</i>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) GetPerspectives()
        {
            if (_perspectives == null)
                return (null, NoAccess, DebugApiHints.Panel);

            var list = new JsonArray();
            foreach (var p in _perspectives.GetPerspectives()) list.Add(p);

            return (new JsonObject
            {
                ["current"]      = _perspectives.CurrentPerspective,
                ["perspectives"] = list,
            }, null, null);
        }

        /// <summary>
        /// <c>POST /perspective {"name": "..."}</c> — switch, then report what actually happened.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>It re-reads <c>CurrentPerspective</c> after switching rather than assuming success</b>, and
        /// that is the whole point: <c>A0</c> makes an unknown name a no-op, so a caller that trusted a
        /// <c>200</c> would go on to capture the WRONG perspective's panels and call the result a golden.
        /// ⛔ A 400 names the claimed set so the caller does not have to guess.
        /// <para>⚠ <b>The switch takes effect for CAPTURE on the NEXT frame</b> — the panels register during
        /// their draw. 📄 §6: step a tick before reading, or you read the previous perspective.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) SwitchPerspective(JsonNode? body)
        {
            if (_perspectives == null)
                return (null, NoAccess, DebugApiHints.Panel);

            var name = body?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                return (null, "Body must be {\"name\": \"<perspective>\"}.", DebugApiHints.Panel);

            var claimed = _perspectives.GetPerspectives();
            // ⭐ Asked BEFORE switching so the 400 can name the set. ⛔ Not a second validation rule: the
            //   window manager still refuses independently, and this only decides the status code.
            bool known = false;
            foreach (var p in claimed)
                if (string.Equals(p, name, StringComparison.Ordinal)) { known = true; break; }

            if (!known)
                return (null,
                        $"'{name}' is not a perspective any registered window claims. Claimed: "
                        + $"[{string.Join(", ", claimed)}]. A perspective exists because a window claims it, "
                        + "so this list is derived, not configured.",
                        DebugApiHints.Panel);

            _perspectives.SwitchPerspective(name!);

            return (new JsonObject
            {
                ["current"] = _perspectives.CurrentPerspective,
                // ⚠ Stated, not implied: nothing is captured for the new perspective until it has DRAWN.
                ["note"]    = "the new perspective's panels publish on the next frame — step a tick before "
                            + "reading GET /panels",
            }, null, null);
        }

        private const string NoAccess =
            "No perspective access is wired into this editor. The DebugApiService is constructed before the "
            + "window manager exists, so EditorSubsystem.RegisterWindows must call AttachPerspectives(...). "
            + "This is a wiring defect, not a missing capability.";
    }
}
