using System.Numerics;
using FluentAssertions;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Theme;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmPseudostateTests
{
    // ---- StateNode.IsPseudostate ----

    [Fact]
    public void IsPseudostate_true_for_history_state()
    {
        var s = new StateNode("H") { IsHistory = true };
        s.IsPseudostate.Should().BeTrue();
    }

    [Fact]
    public void IsPseudostate_true_for_deep_history_state()
    {
        var s = new StateNode("H*") { IsDeepHistory = true };
        s.IsPseudostate.Should().BeTrue();
    }

    [Fact]
    public void IsPseudostate_true_for_final_state()
    {
        var s = new StateNode("F") { IsFinal = true };
        s.IsPseudostate.Should().BeTrue();
    }

    [Fact]
    public void IsPseudostate_false_for_normal_state()
    {
        var s = new StateNode("A");
        s.IsPseudostate.Should().BeFalse();
    }

    [Fact]
    public void IsPseudostate_false_for_initial_state()
    {
        var s = new StateNode("A") { IsInitial = true };
        s.IsPseudostate.Should().BeFalse();
    }

    // ---- HsmKinds.Pseudostate ----

    [Fact]
    public void HsmKinds_Pseudostate_constant_has_expected_value()
    {
        HsmKinds.Pseudostate.Should().Be("hsm.pseudostate");
    }

    // ---- HsmEditorTheme ----

    [Fact]
    public void HsmEditorTheme_Custom_category_returns_transparent()
    {
        var theme = new HsmEditorTheme();
        var color = theme.GetCategoryHeaderColor(NodeCategory.Custom);
        color.Should().Be(Vector4.Zero);
    }

    [Fact]
    public void HsmEditorTheme_non_Custom_category_returns_non_transparent()
    {
        var theme = new HsmEditorTheme();
        var color = theme.GetCategoryHeaderColor(NodeCategory.Function);
        // Alpha channel should be non-zero (opaque color from DefaultTheme).
        color.W.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void IsPseudostateKind_true_for_Final_kind()
    {
        var kind = new NodeKindKey(HsmKinds.Final);
        HsmEditorTheme.IsPseudostateKind(kind).Should().BeTrue();
    }

    [Fact]
    public void IsPseudostateKind_true_for_History_kind()
    {
        var kind = new NodeKindKey(HsmKinds.History);
        HsmEditorTheme.IsPseudostateKind(kind).Should().BeTrue();
    }

    [Fact]
    public void IsPseudostateKind_true_for_DeepHistory_kind()
    {
        var kind = new NodeKindKey(HsmKinds.DeepHistory);
        HsmEditorTheme.IsPseudostateKind(kind).Should().BeTrue();
    }

    [Fact]
    public void IsPseudostateKind_false_for_simple_state_kind()
    {
        var kind = new NodeKindKey(HsmKinds.Simple);
        HsmEditorTheme.IsPseudostateKind(kind).Should().BeFalse();
    }
}
