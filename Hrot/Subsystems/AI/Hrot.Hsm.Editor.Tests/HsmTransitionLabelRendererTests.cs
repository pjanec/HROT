using System;
using System.Numerics;
using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Renderers;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmTransitionLabelRendererTests
{
    // ---- helper ----

    private static TransitionNode MakeTransition(
        string? eventName = null,
        string? guardFqn = null,
        string? actionFqn = null,
        byte priority = 128,
        ushort syncGroupId = 0,
        TransitionKind kind = TransitionKind.External)
    {
        var src = new StateNode("Src");
        var t = new TransitionNode
        {
            VisualId = Guid.NewGuid(),
            EventName = eventName,
            GuardFunction = guardFqn,
            ActionFunction = actionFqn,
            Priority = priority,
            SyncGroupId = syncGroupId,
            Kind = kind,
            Source = src,
            Target = src,
        };
        return t;
    }

    // ---- tests ----

    [Fact]
    public void FormatLabel_event_only_returns_event_name()
    {
        var t = MakeTransition(eventName: "OnSight");
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("OnSight");
    }

    [Fact]
    public void FormatLabel_event_and_action_returns_event_slash_action()
    {
        var t = MakeTransition(eventName: "Fire", actionFqn: "MyNs.MyClass.Reload");
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("Fire/Reload");
    }

    [Fact]
    public void FormatLabel_event_and_guard_returns_event_brackets_guard()
    {
        var t = MakeTransition(eventName: "OnSight", guardFqn: "GuardNs.Checks.AmmoOk");
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("OnSight[AmmoOk]");
    }

    [Fact]
    public void FormatLabel_full_all_parts_combined()
    {
        var t = MakeTransition(eventName: "OnFire", guardFqn: "G.AmmoOk", actionFqn: "A.StashWeapon");
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("OnFire[AmmoOk]/StashWeapon");
    }

    [Fact]
    public void FormatLabel_no_event_no_guard_no_action_returns_unnamed()
    {
        var t = MakeTransition();
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("<unnamed>");
    }

    [Fact]
    public void FormatLabel_nondefault_priority_appends_badge()
    {
        var t = MakeTransition(eventName: "Hit", priority: 200);
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("Hit (P:200)");
    }

    [Fact]
    public void FormatLabel_sync_group_appends_badge()
    {
        var t = MakeTransition(eventName: "Hit", syncGroupId: 3);
        HsmTransitionLabelRenderer.FormatLabel(t).Should().Be("Hit [SG:3]");
    }

    // ── ComputeArrowheadGeometry tests ──────────────────────────────────────

    [Fact]
    public void ComputeArrowheadGeometry_tip_is_at_target()
    {
        var source = new Vector2(0f, 0f);
        var target = new Vector2(100f, 0f);

        var result = HsmTransitionLabelRenderer.ComputeArrowheadGeometry(source, target, 7f, 5f);

        result.Should().NotBeNull();
        result!.Value.tip.Should().Be(target);
    }

    [Fact]
    public void ComputeArrowheadGeometry_tip_points_toward_target()
    {
        // Source → target goes right (+X). Tip must be closer to target than base vertices.
        var source = new Vector2(0f, 0f);
        var target = new Vector2(100f, 0f);

        var result = HsmTransitionLabelRenderer.ComputeArrowheadGeometry(source, target, 7f, 5f);

        result.Should().NotBeNull();
        var (tip, left, right) = result!.Value;

        // Both base vertices should be behind (lower X) the tip when going right.
        left.X.Should().BeLessThan(tip.X);
        right.X.Should().BeLessThan(tip.X);

        // Base vertices should be symmetric around the shaft axis (same X, opposite Y).
        left.X.Should().BeApproximately(right.X, 1e-4f);
        left.Y.Should().BeApproximately(-right.Y, 1e-4f);
    }

    [Fact]
    public void ComputeArrowheadGeometry_triangle_is_non_degenerate()
    {
        // A non-degenerate triangle has non-zero area.
        // Area = 0.5 * |cross(b-a, c-a)|
        var source = new Vector2(10f, 20f);
        var target = new Vector2(80f, 50f);

        var result = HsmTransitionLabelRenderer.ComputeArrowheadGeometry(source, target, 7f, 5f);

        result.Should().NotBeNull();
        var (tip, left, right) = result!.Value;

        var ab = left  - tip;
        var ac = right - tip;
        float cross = ab.X * ac.Y - ab.Y * ac.X;   // Z-component of 3D cross product
        MathF.Abs(cross).Should().BeGreaterThan(1e-4f, "triangle must have non-zero area");
    }

    [Fact]
    public void ComputeArrowheadGeometry_coincident_points_returns_null()
    {
        var pos = new Vector2(50f, 50f);

        var result = HsmTransitionLabelRenderer.ComputeArrowheadGeometry(pos, pos, 7f, 5f);

        result.Should().BeNull("coincident source and target give no valid direction");
    }

    [Fact]
    public void ComputeArrowheadGeometry_diagonal_direction_tip_at_target()
    {
        var source = new Vector2(0f, 0f);
        var target = new Vector2(30f, 40f);  // 3-4-5 triangle scaled ×10 → length 50

        var result = HsmTransitionLabelRenderer.ComputeArrowheadGeometry(source, target, 7f, 5f);

        result.Should().NotBeNull();
        result!.Value.tip.X.Should().BeApproximately(target.X, 1e-4f);
        result!.Value.tip.Y.Should().BeApproximately(target.Y, 1e-4f);
    }
}
