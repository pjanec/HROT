using System;
using Hrot.Editor.AiShared;
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
            Priority = priority,
            SyncGroupId = syncGroupId,
            Kind = kind,
            Source = src,
            Target = src,
            Guard  = BehaviorActionBinding.ForMethod(guardFqn),   // CE-417
            Action = BehaviorActionBinding.ForMethod(actionFqn),
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

    // ── CE-1000 LabelAnchor: the label sits beside the drawn arrow, on the outer side of its bend ──

    [Fact]
    public void CE1000_LabelAnchor_IsOnTheOuterSideOfTheBend()
    {
        // A→B left to right; NodeToNode bends to the left of travel = up (y down) in screen space.
        var path = NodeEditor.Core.Canvas.LinkPathBuilder.NodeToNode(
            new NodeEditor.Primitives.RectF(new Vector2(0, 0), new Vector2(100, 40)),
            new NodeEditor.Primitives.RectF(new Vector2(300, 0), new Vector2(100, 40)));
        var size = new Vector2(40, 12);
        var topLeft = HsmTransitionLabelRenderer.LabelAnchor(path, size);
        var mid = path.Point(0.5f);
        Assert.True(topLeft.Y + size.Y <= mid.Y, "label box must sit above an arc that bends up");
        Assert.InRange(topLeft.X + size.X * 0.5f, mid.X - 1f, mid.X + 1f);
    }

    [Fact]
    public void CE1000_LabelAnchor_OfAPair_Separate()
    {
        var a = new NodeEditor.Primitives.RectF(new Vector2(0, 0), new Vector2(100, 40));
        var b = new NodeEditor.Primitives.RectF(new Vector2(300, 0), new Vector2(100, 40));
        var size = new Vector2(40, 12);
        var ab = HsmTransitionLabelRenderer.LabelAnchor(NodeEditor.Core.Canvas.LinkPathBuilder.NodeToNode(a, b), size);
        var ba = HsmTransitionLabelRenderer.LabelAnchor(NodeEditor.Core.Canvas.LinkPathBuilder.NodeToNode(b, a), size);
        Assert.True(MathF.Abs(ab.Y - ba.Y) > size.Y, "the two labels of A→B and B→A must not overlap");
    }
}
