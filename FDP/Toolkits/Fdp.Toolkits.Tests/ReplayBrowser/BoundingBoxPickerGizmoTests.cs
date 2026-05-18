using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// SR-T30..SR-T31: BoundingBoxPickerGizmo interaction state-machine tests.
    /// </summary>
    public class BoundingBoxPickerGizmoTests
    {
        // ── SR-T30: drag from (10,10) to (20,30), left-release commits ────────

        [Fact]
        public void SR_T30_LeftPress_Drag_Release_CallsOnCompleteWithCorrectBounds()
        {
            BoundingBox2D? completed = null;
            int removeCount = 0;

            var gizmo = new BoundingBoxPickerGizmo(
                bbox => completed = bbox,
                () => removeCount++);

            // Left-press at (10, 10)
            gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(10f, 10f, 0f));

            // Drag to (20, 30)
            gizmo.OnDragUpdate(new Vector3(20f, 30f, 0f));

            // Left-release at (20, 30)
            gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(20f, 30f, 0f));

            Assert.NotNull(completed);
            Assert.Equal(new Vector2(10f, 10f), completed!.Value.Min);
            Assert.Equal(new Vector2(20f, 30f), completed.Value.Max);
            Assert.Equal(1, removeCount);
        }

        // ── SR-T30b: drag reversed direction (start > end) min/max are correct

        [Fact]
        public void SR_T30b_DragReversed_MinMaxAreCorrect()
        {
            BoundingBox2D? completed = null;
            var gizmo = new BoundingBoxPickerGizmo(bbox => completed = bbox, () => { });

            // Start at top-right, drag to bottom-left
            gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(20f, 30f, 0f));
            gizmo.OnDragUpdate(new Vector3(5f, 8f, 0f));
            gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(5f, 8f, 0f));

            Assert.NotNull(completed);
            Assert.Equal(new Vector2(5f, 8f), completed!.Value.Min);
            Assert.Equal(new Vector2(20f, 30f), completed.Value.Max);
        }

        // ── SR-T31: Escape cancels -- onComplete not called, onRemove called once

        [Fact]
        public void SR_T31_Escape_CancelsWithoutCallingOnComplete()
        {
            bool completeCalled = false;
            int removeCount = 0;

            var gizmo = new BoundingBoxPickerGizmo(
                _ => completeCalled = true,
                () => removeCount++);

            // Start dragging
            gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(10f, 10f, 0f));
            gizmo.OnDragUpdate(new Vector3(20f, 20f, 0f));

            // Press Escape to cancel
            gizmo.OnKeyEvent(MapKeyboardKey.Escape, true);

            Assert.False(completeCalled);
            Assert.Equal(1, removeCount);
        }

        // ── SR-T31b: Right-press cancels -- onComplete not called, onRemove once

        [Fact]
        public void SR_T31b_RightPress_CancelsWithoutCallingOnComplete()
        {
            bool completeCalled = false;
            int removeCount = 0;

            var gizmo = new BoundingBoxPickerGizmo(
                _ => completeCalled = true,
                () => removeCount++);

            // Start dragging
            gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(5f, 5f, 0f));
            gizmo.OnDragUpdate(new Vector3(15f, 15f, 0f));

            // Right-press to cancel
            gizmo.OnMouseEvent(MapMouseButton.Right, true, new Vector3(15f, 15f, 0f));

            Assert.False(completeCalled);
            Assert.Equal(1, removeCount);
        }

        // ── SR-T31c: No drag active -- left-release does not fire onComplete ─

        [Fact]
        public void SR_T31c_LeftRelease_WithoutPriorPress_DoesNotFire()
        {
            bool completeCalled = false;
            int removeCount = 0;

            var gizmo = new BoundingBoxPickerGizmo(
                _ => completeCalled = true,
                () => removeCount++);

            // Release without a prior press
            gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(10f, 10f, 0f));

            Assert.False(completeCalled);
            Assert.Equal(0, removeCount);
        }

        // ── SR-T31d: Props sanity ─────────────────────────────────────────────

        [Fact]
        public void SR_T31d_Properties_HaveExpectedValues()
        {
            var gizmo = new BoundingBoxPickerGizmo(_ => { }, () => { });

            Assert.True(gizmo.RequiresExclusiveFocus);
            Assert.True(gizmo.WantsRawInput);
        }
    }
}
