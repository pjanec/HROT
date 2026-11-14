using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// Two generic picker features: a host-chosen DEFAULT (<see cref="PickerRequest.InitialSelectionId"/>)
/// and entries shown but not choosable (<see cref="PickerEntry.IsEnabled"/>).
/// Red-proof: make <c>ConfirmableSelection</c> skip the <c>IsEnabled</c> filter ⇒ the disabled rails fail.
/// </summary>
public sealed class PickerInitialSelectionAndDisabledTests
{
    private static PickerEntry E(string id, string? category, bool enabled = true)
        => new(id, id, enabled ? null : "why not", category, null, null, id, IsEnabled: enabled);

    private static PickerState StateWith(params PickerEntry[] entries)
    {
        var state = new PickerState();
        state.Reset("ctx.test", "", PickerSelectionMode.Single);
        state.AllEntries = entries;
        state.Refilter();
        return state;
    }

    [Fact]
    public void Initial_selection_selects_focuses_and_reveals_the_entry()
    {
        var state = StateWith(E("a", "X"), E("b", "Tech/Sub"), E("c", null));

        state.ApplyInitialSelection("b").Should().BeTrue();

        var idx = state.Filtered.FindIndex(r => r.Entry.Id == "b");
        state.SelectedFilteredIndices.Should().Equal(idx);
        state.KeyboardFocusIndex.Should().Be(idx);
        state.RevealCategory.Should().Be("Tech/Sub", "the tree opens every folder on the default's path");
        state.FocusLeafOnDraw.Should().Be(idx, "Enter must confirm the default, not expand row 0");
        state.ConfirmableSelection(out _).Select(r => r.Entry.Id).Should().Equal("b");
    }

    [Fact]
    public void An_unknown_or_disabled_default_changes_nothing()
    {
        var state = StateWith(E("a", null), E("off", null, enabled: false));

        state.ApplyInitialSelection("missing").Should().BeFalse();
        state.ApplyInitialSelection("off").Should().BeFalse();
        state.ApplyInitialSelection(null).Should().BeFalse();
        state.SelectedFilteredIndices.Should().BeEmpty();
        state.RevealCategory.Should().BeNull();
    }

    [Fact]
    public void A_disabled_entry_never_confirms_and_alone_keeps_the_picker_open()
    {
        var state = StateWith(E("on", null), E("off", null, enabled: false));
        var off = state.Filtered.FindIndex(r => r.Entry.Id == "off");

        state.SelectedFilteredIndices.Add(off);
        state.ConfirmableSelection(out var onlyDisabled).Should().BeEmpty();
        onlyDisabled.Should().BeTrue();

        // Mixed (multi-select): the enabled one is returned, the disabled one dropped.
        state.SelectedFilteredIndices.Add(state.Filtered.FindIndex(r => r.Entry.Id == "on"));
        state.ConfirmableSelection(out onlyDisabled).Select(r => r.Entry.Id).Should().Equal("on");
        onlyDisabled.Should().BeFalse();
    }

    [Fact]
    public void With_nothing_selected_the_focused_entry_confirms_unless_disabled()
    {
        var state = StateWith(E("off", null, enabled: false), E("on", null));

        state.KeyboardFocusIndex = state.Filtered.FindIndex(r => r.Entry.Id == "on");
        state.ConfirmableSelection(out var onlyDisabled).Select(r => r.Entry.Id).Should().Equal("on");
        onlyDisabled.Should().BeFalse();

        state.KeyboardFocusIndex = state.Filtered.FindIndex(r => r.Entry.Id == "off");
        state.ConfirmableSelection(out onlyDisabled).Should().BeEmpty();
        onlyDisabled.Should().BeTrue();
    }
}
