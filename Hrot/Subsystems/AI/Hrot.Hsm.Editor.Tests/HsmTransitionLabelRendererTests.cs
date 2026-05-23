using System;
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
}
