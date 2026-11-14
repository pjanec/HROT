using GizmoMap.Presentation;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    /// <summary>
    /// ⭐⭐⭐ Rails for <see cref="RawButtonGate"/> — <c>CE-259n</c>.
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7f.
    ///
    /// <para>🔴 <b>The operator-found defect these pin:</b> with a vertex-edit gizmo armed on the map,
    /// right-clicking a row in an ImGui PANEL ended the edit and destroyed the gizmo — because
    /// <c>DebugGizmoLayer</c> withheld the press (correctly) and sent the RELEASE anyway, and
    /// <c>VertexEditGizmo</c> treats a right-release as <i>"commit and exit"</i>.</para>
    ///
    /// <para>⚠ <b>Why the logic was EXTRACTED rather than fixed in place:</b> <c>DebugGizmoLayer.HandleInput</c>
    /// polls <c>Raylib.IsMouseButtonPressed/Released</c> directly, so the decision cannot be driven from a
    /// test. ⛔ Leaving it inline would have shipped the fix unrailed — and this is a defect that a green
    /// ~8 000-rail suite already failed to see once.</para>
    ///
    /// <para>🔒 <b>The invariant, in one line:</b> a release is delivered iff <b>its own press</b> was —
    /// not iff ImGui happens to want the mouse at release time.</para>
    /// </summary>
    public sealed class RawButtonGateTests
    {
        /// <summary>⭐ The ordinary case: press and release both on the map.</summary>
        [Fact]
        public void APressOnTheMapEarnsItsRelease()
        {
            var gate = new RawButtonGate();

            Assert.True(gate.OnPress(imguiCaptured: false));
            Assert.True(gate.HasOutstandingPress);
            Assert.True(gate.OnRelease());
            Assert.False(gate.HasOutstandingPress);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE DEFECT: a press swallowed by a panel must not hand its release to a map gizmo.</b>
        /// ⛔ This is the assertion the shipped code failed — it delivered the release unconditionally.
        /// </summary>
        [Fact]
        public void APressSwallowedByAPanelDoesNotEarnItsRelease()
        {
            var gate = new RawButtonGate();

            Assert.False(gate.OnPress(imguiCaptured: true));
            Assert.False(gate.HasOutstandingPress);
            Assert.False(gate.OnRelease());     // 🔴 the vertex edit survives the panel right-click
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE REGRESSION GUARD — and it is why the fix is NOT "gate the release on capture".</b>
        /// 🔒 The original comment was right: <i>"ALWAYS send released events to prevent stuck backend
        /// input queues."</i> A gizmo that took its press on the map and released over a panel MUST still
        /// get the release, or it hangs mid-drag forever. ⛔ Gating on capture-at-release-time would
        /// reintroduce exactly that hang; gating on the PRESS does not.
        /// </summary>
        [Fact]
        public void APressOnTheMapStillEarnsItsReleaseEvenIfThePointerEndsOverAPanel()
        {
            var gate = new RawButtonGate();

            gate.OnPress(imguiCaptured: false);   // started on the map …

            // … the pointer has since travelled over a panel. The gate does not care, and must not:
            // suppressing here is the stuck-drag bug.
            Assert.True(gate.OnRelease());
        }

        /// <summary>⚠ A release with no press at all — a gizmo armed while the button was already down.</summary>
        [Fact]
        public void AReleaseWithNoPressAtAllIsNeverDelivered()
        {
            var gate = new RawButtonGate();

            Assert.False(gate.OnRelease());
        }

        /// <summary>
        /// ⛔ A suppressed press must not arm a LATER release. ⭐ The release consumes the outstanding
        /// press either way, so the two cannot drift out of step across frames.
        /// </summary>
        [Fact]
        public void AnOutstandingPressIsConsumedByTheFirstReleaseOnly()
        {
            var gate = new RawButtonGate();

            gate.OnPress(imguiCaptured: false);
            Assert.True(gate.OnRelease());
            Assert.False(gate.OnRelease());     // a second release earns nothing
        }

        /// <summary>⭐ <c>Forget</c> drops an outstanding press without delivering a release.</summary>
        [Fact]
        public void ForgetDropsAnOutstandingPressWithoutDeliveringARelease()
        {
            var gate = new RawButtonGate();

            gate.OnPress(imguiCaptured: false);
            gate.Forget();

            Assert.False(gate.HasOutstandingPress);
            Assert.False(gate.OnRelease());
        }
    }
}
