using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

public class InteractionStateTests
{
    [Fact]
    public void Default_ModeIsIdle()
    {
        var state = new InteractionState();
        state.Mode.Should().Be(InteractionMode.Idle);
    }

    [Fact]
    public void Hover_DefaultIsNone()
    {
        var state = new InteractionState();
        state.Hover.Kind.Should().Be(HoverKind.None);
    }

    [Fact]
    public void ResetToIdle_ClearsDragOverrides()
    {
        var state = new InteractionState();
        state.Mode = InteractionMode.DraggingNodes;
        state.DragOverridePositions[NodeId.NewId()] = new Vector2(10f, 20f);
        state.DragThresholdCrossed = true;
        state.PendingWire = new PendingWire { SourcePin = PinId.NewId() };

        state.ResetToIdle();

        state.Mode.Should().Be(InteractionMode.Idle);
        state.DragOverridePositions.Should().BeEmpty();
        state.DragThresholdCrossed.Should().BeFalse();
        state.PendingWire.Should().BeNull();
    }
}
