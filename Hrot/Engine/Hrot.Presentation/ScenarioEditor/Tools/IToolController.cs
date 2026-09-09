using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-07</c> <c>A1</c> — the ONE arbiter of "which tool is active", per subsystem.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4 (the class + sequence diagrams) · <c>Q27</c> answers.
    ///
    /// <para>⛔⛔ <b>The defect this exists to close, REPRODUCED <c>2026-09-09</c>:</b> there are two focus
    /// arbiters — <c>GlobalGizmoManager</c> and <c>DataDrivenGizmoSystem</c> — each guarding exclusivity
    /// only within itself, both handed the SAME <c>FdpEventBus</c> by
    /// <c>MapInteractionPack.cs:92-100</c>. Measured: two "exclusive" tools hold focus at once and ONE
    /// <c>GizmoMouseEvent</c> reaches BOTH. The raw-input half is worse and DETERMINISTIC — the pack's
    /// fixed group order means <c>GlobalGizmoManager</c> always emits its <c>InputCaptureBinding</c> first,
    /// so the terminal's first-one-wins <c>break</c> always favours it.</para>
    ///
    /// <para>🔒 <b>Why it is urgent and not merely tidy</b> — the Windows lane measured it in production:
    /// wiring <c>CanvasMapPickAdapter</c> to a LIVE manager on SimHost breaks <c>hill-attack-close</c>
    /// (<c>CE-254</c>). ⇒ closing this is the precondition for that fix, not a nicety.</para>
    ///
    /// <para>⭐⭐ <b>The safety property:</b> nothing changes while only ONE modal tool is active — which is
    /// every case that works today. The controller acts only when a SECOND modal would take focus.
    /// ⇒ it makes concurrency impossible rather than making anything newly live.</para>
    ///
    /// <para>⚠⚠ <b>TWO DELIBERATE EXCEPTIONS to that property, both landed in step 2 and both recorded in
    /// the design at §4.7.</b> An earlier version of this paragraph claimed <i>"this makes NOTHING newly
    /// live"</i> without qualification — ⛔ that is no longer true, and pretending otherwise would hide a
    /// user-visible change:
    /// <list type="number">
    /// <item>🔒 <b><c>Select</c> is now the NULL MODAL TOOL and clears both arbiters</b> (<c>Q27</c>). It was
    /// an empty <c>break</c> — the toolbar button did nothing — and it is how an operator leaves a tool.</item>
    /// <item>⭐ <b>A modal tool armed on entity A is torn down when the same tool arms on B.</b> Before, both
    /// kept an injected gizmo; only one could ever hold focus, so the second was drawable-but-inert.</item>
    /// </list></para>
    /// </summary>
    public interface IToolController
    {
        /// <summary>The modal tool currently holding focus, or <c>null</c>. Top of the stack.</summary>
        ToolDescriptor? ActiveModal { get; }

        /// <summary>
        /// The entity <see cref="ActiveModal"/> armed on, or <see cref="Entity.Null"/> for a non-entity tool
        /// (and when nothing is armed). ⭐ Part of the identity of "the active tool" — see <see cref="ArmedTool"/>.
        /// </summary>
        Entity ActiveModalTarget { get; }

        /// <summary>Bottom → top. One entry today; <c>PushModal</c> is what grows it.</summary>
        IReadOnlyList<ArmedTool> ModalStack { get; }

        /// <summary>Modeless tools, unaffected by the modal stack (<c>Q27</c> ruling C).</summary>
        IReadOnlyCollection<ToolDescriptor> ActiveModeless { get; }

        /// <summary>
        /// Activate a registered tool, REPLACING the current modal. Returns <c>false</c> when the tool is
        /// unknown or the host cannot service it — ⛔ never silently.
        /// </summary>
        bool Activate(string toolId, Entity target = default);

        /// <summary>
        /// ⚠ <b>Declared, deliberately NOT implemented in this slice.</b> The suspend/resume stack is the
        /// next increment; <c>Q27</c>'s ruling and the <c>MapCanvas.PushTool</c> history live in
        /// <c>UX_Feature_Tool_Model.md</c>. Implementations throw <see cref="NotSupportedException"/>
        /// rather than silently behaving like <see cref="Activate"/> — ⭐ absent-and-explained beats
        /// present-and-broken (ruling 49).
        /// </summary>
        IDisposable PushModal(string toolId, Entity target = default);

        /// <summary>Cancel the active modal tool. Modeless and stateless gizmos are untouched.</summary>
        void Cancel();

        /// <summary>Raised whenever <see cref="ActiveModal"/> changes. The toolbar binds here (step 5).</summary>
        event Action<ToolDescriptor?>? ActiveModalChanged;
    }
}
