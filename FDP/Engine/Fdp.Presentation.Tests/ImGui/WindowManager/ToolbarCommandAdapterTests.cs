using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Unit tests for <see cref="ToolbarCommandAdapter"/> — MTB-P2-T3 success conditions.
/// Uses <see cref="ToolbarCommandAdapter.GetState"/> for headless logic tests;
/// registration tests use <see cref="MainToolbarManager.GetVisibleItemPlan"/> to verify entries.
/// </summary>
public class ToolbarCommandAdapterTests
{
    /// <summary>
    /// A simple recording fake implementation of <see cref="IEditorCommands"/>.
    /// </summary>
    private sealed class FakeCommandSet : IEditorCommands
    {
        private readonly Dictionary<string, (EditorCommandDescriptor Descriptor, Action<EditorCommandContext> Action)> _commands
            = new(StringComparer.Ordinal);

        public event System.Action<string>? AvailabilityChanged;

        public IReadOnlyList<EditorCommandDescriptor> All
        {
            get
            {
                var list = new List<EditorCommandDescriptor>();
                foreach (var kv in _commands)
                    list.Add(kv.Value.Descriptor);
                return list;
            }
        }

        public EditorCommandDescriptor? Get(string commandId)
            => _commands.TryGetValue(commandId, out var c) ? c.Descriptor : null;

        public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null)
        {
            if (!_commands.TryGetValue(commandId, out var cmd))
                return new EditorCommandResult(false, $"Unknown command: {commandId}");

            if (!cmd.Descriptor.IsEnabled())
                return new EditorCommandResult(false, "Command not enabled.");

            cmd.Action(ctx ?? default);
            return new EditorCommandResult(true, null);
        }

        public void Register(EditorCommandDescriptor descriptor, Action<EditorCommandContext> action)
        {
            _commands[descriptor.Id] = (descriptor, action);
        }

        public void NotifyAvailabilityChanged(string commandId)
            => AvailabilityChanged?.Invoke(commandId);
    }

    /// <summary>
    /// A fake icon provider that always returns a dummy icon.
    /// </summary>
    private sealed class FakeIconProvider : IIconProvider
    {
        private readonly IconHandle _handle = new(
            IntPtr.Zero, 64, 64,
            new System.Numerics.Vector2(0, 0),
            new System.Numerics.Vector2(1, 1));

        public bool TryGet(string key, out IconHandle icon)
        {
            icon = _handle;
            return true;
        }
    }

    /// <summary>
    /// A fake icon provider that never resolves any key.
    /// </summary>
    private sealed class MissingIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle icon)
        {
            icon = default;
            return false;
        }
    }

    // ── MTB-P2-T3: Click_InvokesCommand ─────────────────────────────────────

    [Fact]
    public void Click_InvokesCommand()
    {
        var commands = new FakeCommandSet();
        bool wasInvoked = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "toolbar.cmd",
            DisplayName: "Toolbar Cmd",
            Category: null,
            Description: null,
            IconKey: "some/icon",
            DefaultKey: null,
            IsEnabled: () => true),
            _ => wasInvoked = true);

        var state = ToolbarCommandAdapter.GetState(commands, "toolbar.cmd");

        Assert.True(state.IsEnabled);
        Assert.NotNull(state.OnClick);

        // Simulate a click via the OnClick action.
        state.OnClick!();
        Assert.True(wasInvoked, "command should be invoked on click");
    }

    // ── MTB-P2-T3: Enabled_And_Toggled_TrackDescriptor ─────────────────────

    [Fact]
    public void Enabled_And_Toggled_TrackDescriptor()
    {
        var commands = new FakeCommandSet();
        bool isEnabled = true;
        bool isChecked = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "toggle.cmd",
            DisplayName: "Toggle",
            Category: null,
            Description: null,
            IconKey: "debug/step",
            DefaultKey: null,
            IsEnabled: () => isEnabled,
            IsChecked: () => isChecked),
            _ => { });

        // Initially enabled, not toggled.
        var state1 = ToolbarCommandAdapter.GetState(commands, "toggle.cmd");
        Assert.True(state1.IsEnabled);
        Assert.False(state1.IsToggled);
        Assert.NotNull(state1.OnClick);

        // Disable and toggle on.
        isEnabled = false;
        isChecked = true;

        var state2 = ToolbarCommandAdapter.GetState(commands, "toggle.cmd");
        Assert.False(state2.IsEnabled);
        Assert.True(state2.IsToggled);
        Assert.Null(state2.OnClick); // OnClick should be null when disabled

        // Re-enable, keep toggled.
        isEnabled = true;
        isChecked = true;

        var state3 = ToolbarCommandAdapter.GetState(commands, "toggle.cmd");
        Assert.True(state3.IsEnabled);
        Assert.True(state3.IsToggled);
        Assert.NotNull(state3.OnClick);
    }

    // ── Disabled click does not invoke ─────────────────────────────────────

    [Fact]
    public void Disabled_StateHasNoClickAction()
    {
        var commands = new FakeCommandSet();
        bool wasInvoked = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "disabled.cmd",
            DisplayName: "Disabled",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => false),
            _ => wasInvoked = true);

        var state = ToolbarCommandAdapter.GetState(commands, "disabled.cmd");

        Assert.False(state.IsEnabled);
        Assert.Null(state.OnClick); // OnClick must be null when disabled
        Assert.False(wasInvoked, "command must not have been invoked");
    }

    // ── Missing icon falls back to text, no throw ──────────────────────────

    [Fact]
    public void MissingIcon_FallsBackToText_NoThrow()
    {
        var commands = new FakeCommandSet();
        commands.Register(new EditorCommandDescriptor(
            Id: "noicon.cmd",
            DisplayName: "No Icon",
            Category: null,
            Description: null,
            IconKey: "unknown/key",
            DefaultKey: null,
            IsEnabled: () => true),
            _ => { });

        var iconProvider = new MissingIconProvider();

        // Verify the provider indeed doesn't resolve the key.
        Assert.False(iconProvider.TryGet("unknown/key", out _));

        // The GetState method works regardless of icon availability
        // (it doesn't depend on IIconProvider at all).
        var state = ToolbarCommandAdapter.GetState(commands, "noicon.cmd");
        Assert.True(state.IsEnabled);
        Assert.NotNull(state.OnClick);

        // Registration with missing icon should also succeed (no throw).
        var toolbar = new MainToolbarManager();
        var ex = Record.Exception(() =>
        {
            ToolbarCommandAdapter.Register(toolbar, commands, "noicon.cmd", iconProvider, 10);
        });
        Assert.Null(ex);

        // Verify the entry was registered.
        var plan = toolbar.GetVisibleItemPlan("");
        Assert.Contains(plan, p => p.Id == "noicon.cmd" && !p.IsSeparator);
    }

    // ── Registration creates visible entry ────────────────────────────────

    [Fact]
    public void Register_CreatesVisibleEntry()
    {
        var commands = new FakeCommandSet();
        commands.Register(new EditorCommandDescriptor(
            Id: "visible.cmd",
            DisplayName: "Visible",
            Category: null,
            Description: "A visible command",
            IconKey: "test/icon",
            DefaultKey: null,
            IsEnabled: () => true),
            _ => { });

        var toolbar = new MainToolbarManager();
        var iconProvider = new FakeIconProvider();

        ToolbarCommandAdapter.Register(toolbar, commands, "visible.cmd", iconProvider, 100, "combat");

        // Entry should appear in the visible plan for matching perspective.
        var plan = toolbar.GetVisibleItemPlan("combat");
        Assert.Contains(plan, p => p.Id == "visible.cmd" && !p.IsSeparator);

        // Entry should NOT appear for non-matching perspective.
        var defaultPlan = toolbar.GetVisibleItemPlan("Default");
        Assert.DoesNotContain(defaultPlan, p => p.Id == "visible.cmd");
    }

    // ── Unknown command throws ────────────────────────────────────────────

    [Fact]
    public void Register_UnknownCommand_ThrowsInvalidOperationException()
    {
        var commands = new FakeCommandSet();
        var toolbar = new MainToolbarManager();
        var iconProvider = new FakeIconProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => ToolbarCommandAdapter.Register(toolbar, commands, "nonexistent", iconProvider, 10));

        Assert.Contains("nonexistent", ex.Message);
    }

    // ── Tooltip includes description and shortcut ──────────────────────────

    [Fact]
    public void State_WithNoChecked_HasIsToggledFalse()
    {
        var commands = new FakeCommandSet();
        commands.Register(new EditorCommandDescriptor(
            Id: "plain.cmd",
            DisplayName: "Plain",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true,
            IsChecked: null),  // no IsChecked
            _ => { });

        var state = ToolbarCommandAdapter.GetState(commands, "plain.cmd");

        Assert.True(state.IsEnabled);
        Assert.False(state.IsToggled, "IsToggled should be false when IsChecked is null");
        Assert.NotNull(state.OnClick);
    }

    // ── MTB2-T3: ResolveTooltip uses DynamicDisplayName ──────────────────────

    [Fact]
    public void ToolbarTooltip_UsesDynamicDisplayName_WhenSet()
    {
        var commands = new FakeCommandSet();

        // With DynamicDisplayName set, first line is the dynamic value.
        commands.Register(new EditorCommandDescriptor(
            Id: "dyn.tip",
            DisplayName: "Static Label",
            Category: null,
            Description: "A description",
            IconKey: null,
            DefaultKey: new KeyBinding(EditorKey.S, KeyModifiers.Ctrl),
            IsEnabled: () => true,
            DynamicDisplayName: () => "Dynamic Label"),
            _ => { });

        var tooltip = ToolbarCommandAdapter.ResolveTooltip(commands, "dyn.tip");

        // First line should be the dynamic value, not DisplayName.
        Assert.StartsWith("Dynamic Label", tooltip);
        Assert.Contains("A description", tooltip);
        Assert.Contains("Ctrl+S", tooltip);

        // With DynamicDisplayName null, first line should be DisplayName.
        commands = new FakeCommandSet();
        commands.Register(new EditorCommandDescriptor(
            Id: "static.tip",
            DisplayName: "Static Label",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true,
            DynamicDisplayName: null),
            _ => { });

        var tooltip2 = ToolbarCommandAdapter.ResolveTooltip(commands, "static.tip");
        Assert.Equal("Static Label", tooltip2);
    }
}
