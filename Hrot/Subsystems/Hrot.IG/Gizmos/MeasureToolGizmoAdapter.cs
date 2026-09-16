using System;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.IG.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> step 4a — the settings checkbox becomes a BRIDGE to the arbiter, not a
    /// second implementation of Measure.</b> 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.9c.
    ///
    /// <para>🔴 <b>What it used to be, and why that was a defect.</b> It constructed its OWN
    /// <see cref="MeasureGizmo"/> and registered it straight on <see cref="GlobalGizmoManager"/>. ⇒ IG had
    /// <b>TWO</b> Measure implementations — this one, and the shared <c>ScenarioToolRegistrations</c> arm
    /// that <c>IgApplication</c>'s Measure action reaches through <c>Activate</c>. 🔒 Ruling 9 forbids two
    /// implementations of one concept, and this one was also §4.8's bypass: its gizmo took focus while
    /// <see cref="ToolController"/> still believed some other tool held it.</para>
    ///
    /// <para>🔴🔴 <b>THE DEFECT THAT MADE IT URGENT — a dead toggle.</b> 📐 Measured:
    /// <c>MeasureGizmo.RequiresExclusiveFocus</c> is <see langword="true"/>, so
    /// <c>GlobalGizmoManager.CancelInteractiveTools()</c> sweeps it — calling <c>OnCancel()</c> and
    /// <c>Dispose()</c>, ⛔ <b>both of which are EMPTY on <c>MeasureGizmo</c></b>. ⇒ the <c>onRemove</c>
    /// callback never fired, this adapter's <c>_wasActive</c> stayed <see langword="true"/> while the gizmo
    /// was gone, and Measure was <b>dead until the operator cycled the setting off and on</b>. ⚠ Pre-existing
    /// via the terminal-disconnect cancel, but newly reachable from ORDINARY TOOL SWITCHING once the
    /// controller began cancelling the other arbiter.</para>
    ///
    /// <para>⭐⭐ <b>The three-way test (<c>2026-08-17</c>) applied:</b> this is a duplicate <b>SURFACE</b>
    /// (a settings checkbox with a unit selector), not duplicate <b>CODE</b> — ⇒ <b>KEEP the surface, route
    /// the implementation.</b> ⛔ Deleting it would cost IG a capability. The units it used to PUSH now
    /// arrive as a PULL source through <c>MapInteractionContext.MeasureUnits</c>.</para>
    ///
    /// <para>⭐ Call <see cref="Update"/> once per frame from <c>IgApplication</c>, as before.</para>
    /// </summary>
    internal sealed class MeasureToolGizmoAdapter : IDisposable
    {
        private readonly GizmoSettingsRegistry _settings;
        private readonly ToolController?       _tools;
        private readonly GlobalGizmoManager?   _manager;

        private readonly uint _activeHash;
        private readonly uint _unitsHash;

        private bool _wasActive;

        /// <param name="manager">
        /// ⚠ Retained ONLY for the no-arbiter fallback, and for <see cref="ReadUnits"/>'s host to exist.
        /// ⛔ No longer the thing this adapter registers on when an arbiter is present.
        /// </param>
        /// <param name="tools">
        /// 🔒 The host's arbiter. ⛔ Optional so the existing rails and any unconverted host still work —
        /// but a production caller that HAS it must PASS it.
        /// </param>
        public MeasureToolGizmoAdapter(
            GlobalGizmoManager    manager,
            GizmoSettingsRegistry settings,
            ToolController?       tools = null)
        {
            _manager  = manager  ?? throw new ArgumentNullException(nameof(manager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tools    = tools;

            _activeHash = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Active);
            _unitsHash  = GizmoSettingsRegistry.ComputeHash(MeasureToolGizmoSettings.Units);

            // ⭐⭐⭐ THE DEAD-TOGGLE FIX, and it is the whole reason this adapter needs the arbiter rather
            //   than merely avoiding it. When ANY other tool arms, the controller cancels Measure — so the
            //   checkbox must follow, or it would read "on" over a tool that is gone and the operator
            //   would have to cycle it. ⛔ The old code had no way to learn this at all.
            if (_tools != null)
                _tools.ActiveModalChanged += OnActiveModalChanged;
        }

        /// <summary>⭐ The live unit preference — handed to the SHARED arm as a pull source.</summary>
        public MeasureDisplayUnits ReadUnits()
            => _settings.Read(_unitsHash).IntValue == 1
                 ? MeasureDisplayUnits.Kilometers
                 : MeasureDisplayUnits.Meters;

        /// <summary>
        /// Call once per frame (from <c>IgApplication.Update</c>) before <c>canvas.Update()</c>.
        /// ⭐ Edge-triggered on the setting, exactly as before — ⛔ what changed is what each edge DOES.
        /// </summary>
        public void Update()
        {
            bool active = _settings.Read(_activeHash).BoolValue;

            if (active && !_wasActive)
            {
                Arm();
            }
            else if (!active && _wasActive)
            {
                Disarm();
            }
            // ⛔ No "refresh units" branch any more — the gizmo PULLS them (MapInteractionContext.MeasureUnits).

            _wasActive = active;
        }

        private void Arm()
        {
            if (_tools != null)
            {
                _tools.Activate(ScenarioToolIds.Measure);
                return;
            }

            // ⚠ No arbiter wired ⇒ say so rather than silently arming a second implementation. 🔒 R-137
            //   says a unification may not cost a capability, but the capability here is IG's, and IG
            //   always composes a pack — this branch exists for rails and for a host mid-conversion.
            ToolReport.Say(null,
                "the Measure setting was switched on but this host wired no ToolController, so the "
              + "measure tool cannot be armed through the arbiter (UXI-07 step 4a).");
        }

        private void Disarm()
        {
            // ⭐ Only cancel if MEASURE is what is armed. ⛔ Cancelling unconditionally would tear down
            //   whatever tool displaced it — the checkbox turning itself off (see OnActiveModalChanged)
            //   must not become a way to cancel the operator's current tool.
            if (_tools != null && _tools.ActiveModal?.Id == ScenarioToolIds.Measure)
                _tools.Cancel();
        }

        /// <summary>
        /// ⭐⭐ Keeps the checkbox honest when something else takes the modal slot.
        /// ⚠⚠ It writes the SETTING, and <see cref="Update"/> is edge-triggered on that same setting — so
        /// <c>_wasActive</c> is updated here too, or the next frame would read a FALSE→FALSE edge as
        /// "nothing happened" while the flag still said true.
        /// </summary>
        private void OnActiveModalChanged(ToolDescriptor? active)
        {
            if (active?.Id == ScenarioToolIds.Measure) return;
            if (!_settings.Read(_activeHash).BoolValue) return;

            _settings.Write(_activeHash, GizmoSettingValue.From(false));
            _wasActive = false;
        }

        public void Dispose()
        {
            if (_tools != null)
                _tools.ActiveModalChanged -= OnActiveModalChanged;
        }
    }
}
