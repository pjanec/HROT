using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class OutputLaneMaskInferenceTests
{
    // Test actions used for reflection-based lane dictionary tests.
    private static class TestActions
    {
        [HsmAction(Lane = CommandLane.Animation)]
        public static void AnimAction() { }

        [HsmAction(Lane = CommandLane.Navigation)]
        public static void NavAction() { }

        [HsmAction(Lane = CommandLane.None)]
        public static void NoLaneAction() { }

        [HsmAction]  // Lane defaults to None
        public static void DefaultLaneAction() { }
    }

    // ---- tests ----

    [Fact]
    public void BuildLaneDictionary_includes_animation_lane_method()
    {
        var dict = HsmOutputLaneMaskInferrer.BuildLaneDictionary(
            new[] { typeof(TestActions).Assembly });
        var animFqn = typeof(TestActions).FullName + ".AnimAction";

        dict.Should().ContainKey(animFqn!);
        dict[animFqn!].Should().Be(CommandLane.Animation);
    }

    [Fact]
    public void BuildLaneDictionary_excludes_none_lane_methods()
    {
        var dict = HsmOutputLaneMaskInferrer.BuildLaneDictionary(
            new[] { typeof(TestActions).Assembly });
        var noLaneFqn = typeof(TestActions).FullName + ".NoLaneAction";
        var defaultFqn = typeof(TestActions).FullName + ".DefaultLaneAction";

        dict.Should().NotContainKey(noLaneFqn!);
        dict.Should().NotContainKey(defaultFqn!);
    }

    [Fact]
    public void ComputeMask_single_animation_action_returns_bit0()
    {
        var dict = HsmOutputLaneMaskInferrer.BuildLaneDictionary(
            new[] { typeof(TestActions).Assembly });
        var animFqn = typeof(TestActions).FullName + ".AnimAction";
        var state = new StateNode("S") { OnEntryAction = animFqn };

        var mask = HsmOutputLaneMaskInferrer.ComputeMask(state, dict);

        mask.Should().Be(0x01);
    }

    [Fact]
    public void ComputeMask_two_actions_different_lanes_returns_or()
    {
        var dict = HsmOutputLaneMaskInferrer.BuildLaneDictionary(
            new[] { typeof(TestActions).Assembly });
        var animFqn = typeof(TestActions).FullName + ".AnimAction";
        var navFqn = typeof(TestActions).FullName + ".NavAction";
        var state = new StateNode("S")
        {
            OnEntryAction = animFqn,
            ActivityAction = navFqn,
        };

        var mask = HsmOutputLaneMaskInferrer.ComputeMask(state, dict);

        mask.Should().Be(0x03);
    }

    [Fact]
    public void ComputeMask_no_actions_returns_zero()
    {
        var state = new StateNode("S");
        var emptyMap = new Dictionary<string, CommandLane>();

        var mask = HsmOutputLaneMaskInferrer.ComputeMask(state, emptyMap);

        mask.Should().Be(0x00);
    }
}
