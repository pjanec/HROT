namespace GizmoMap.Presentation
{
    /// <summary>
    /// ⭐⭐⭐ <b>Pairs a raw mouse RELEASE with the PRESS that earned it.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7f · <c>CE-259n</c>.
    ///
    /// <para>🔴 <b>The defect, found by an operator <c>2026-09-09</c>:</b> with a vertex-edit gizmo armed on
    /// the map, right-clicking a row in an ImGui PANEL ended the edit and destroyed the gizmo. The press
    /// was correctly withheld from the map (<c>ImGui.GetIO().WantCaptureMouse</c>), but
    /// <see cref="DebugGizmoLayer"/> sent the RELEASE anyway — so the gizmo received a release whose press
    /// it never saw, and <c>VertexEditGizmo.OnMouseEvent</c> treats a right-RELEASE as
    /// <i>"commit and exit"</i>.</para>
    ///
    /// <para>⚠⚠ <b>The ungated release was DELIBERATE and its reason is real</b> — the original comment:
    /// <i>"…but ALWAYS send released events to prevent stuck backend input queues."</i> A gizmo that took a
    /// press on the map and then released over a panel MUST still get its release, or it hangs mid-drag
    /// forever. ⛔ <b>So the fix is not to gate the release on capture</b> — that would reintroduce exactly
    /// the hang the comment prevents.</para>
    ///
    /// <para>⭐⭐ <b>The distinction that resolves both:</b> a release is legitimate when <b>its own press
    /// was delivered</b>, wherever the pointer has since travelled. ⇒ this gate remembers that one bit.
    /// 🔒 Press on map → release over a panel: <b>still delivered</b> (the hang stays fixed). Press
    /// swallowed by a panel → release: <b>suppressed</b> (the defect is fixed).</para>
    ///
    /// <para>⛔ One instance PER BUTTON, held across frames by the layer — the state is *"is a press of
    /// THIS button outstanding"*, which is meaningless as a per-frame local.</para>
    /// </summary>
    public struct RawButtonGate
    {
        private bool _pressDelivered;

        /// <summary><c>true</c> while a delivered press is still awaiting its release. Diagnostics/rails.</summary>
        public readonly bool HasOutstandingPress => _pressDelivered;

        /// <summary>
        /// Call on a raw press. Returns whether the press should be delivered — and records that answer,
        /// because the matching release depends on it.
        /// </summary>
        /// <param name="imguiCaptured"><c>ImGui.GetIO().WantCaptureMouse</c> for this frame.</param>
        public bool OnPress(bool imguiCaptured)
        {
            _pressDelivered = !imguiCaptured;
            return _pressDelivered;
        }

        /// <summary>
        /// Call on a raw release. Returns whether it should be delivered — <c>true</c> only when this
        /// button's press was delivered. ⭐ Consumes the outstanding press either way, so a suppressed
        /// press can never arm a later release.
        /// </summary>
        public bool OnRelease()
        {
            bool deliver    = _pressDelivered;
            _pressDelivered = false;
            return deliver;
        }

        /// <summary>
        /// Drop any outstanding press without delivering a release — for a teardown that ends the
        /// interaction by another route (the capture binding disappearing, a cancel).
        /// </summary>
        public void Forget() => _pressDelivered = false;
    }
}
