using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Unit tests for <see cref="MenuCommandAdapter"/> — MTB-P2-T2 success conditions.
/// Pure logic tests; no ImGui context required.
/// </summary>
public class MenuCommandAdapterTests
{
    /// <summary>
    /// A simple recording fake implementation of <see cref="IEditorCommands"/>
    /// for use in adapter tests.
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

    [Fact]
    public void RegistersItem_AtPath_OnClickInvokesCommand()
    {
        var commands = new FakeCommandSet();
        bool wasInvoked = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "test.cmd",
            DisplayName: "Test",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true),
            _ => wasInvoked = true);

        var menu = new GlobalMenuRegistry();
        MenuCommandAdapter.Register(menu, commands, "test.cmd", "File/Test");

        // Verify the node was created at the correct path.
        Assert.True(menu.Root.Children.ContainsKey("File"));
        var fileNode = menu.Root.Children["File"];
        Assert.True(fileNode.Children.ContainsKey("Test"));
        var leaf = fileNode.Children["Test"];

        // Leaf should have an OnClick action.
        Assert.NotNull(leaf.OnClick);

        // Invoking OnClick should invoke the command.
        leaf.OnClick!();
        Assert.True(wasInvoked);
    }

    [Fact]
    public void Checkable_ReflectsIsChecked()
    {
        var commands = new FakeCommandSet();
        bool isChecked = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "toggle.cmd",
            DisplayName: "Toggle",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true,
            IsChecked: () => isChecked),
            _ => { });

        var menu = new GlobalMenuRegistry();
        MenuCommandAdapter.Register(menu, commands, "toggle.cmd", "View/Toggle");

        // Verify it's a checkable item.
        var leaf = MenuCommandAdapter.FindNode(menu.Root, "View/Toggle");
        Assert.NotNull(leaf);
        Assert.NotNull(leaf!.GetCheckedState);

        // Initially false.
        Assert.False(leaf.GetCheckedState!());

        // Flip the backing state.
        isChecked = true;
        Assert.True(leaf.GetCheckedState!());
    }

    [Fact]
    public void Disabled_ItemNotInvoked_WhenIsEnabledFalse()
    {
        var commands = new FakeCommandSet();
        bool wasInvoked = false;
        bool isEnabled = false;

        commands.Register(new EditorCommandDescriptor(
            Id: "disabled.cmd",
            DisplayName: "Disabled",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => isEnabled),
            _ => wasInvoked = true);

        var menu = new GlobalMenuRegistry();
        MenuCommandAdapter.Register(menu, commands, "disabled.cmd", "Edit/Disabled");

        var leaf = MenuCommandAdapter.FindNode(menu.Root, "Edit/Disabled");
        Assert.NotNull(leaf);
        Assert.NotNull(leaf!.OnClick);

        // Invoking OnClick should NOT invoke the command when disabled.
        leaf.OnClick!();
        Assert.False(wasInvoked, "command must not be invoked when IsEnabled returns false");

        // Enable and try again — it should work.
        isEnabled = true;
        leaf.OnClick!();
        Assert.True(wasInvoked, "command should be invoked when IsEnabled returns true");
    }

    [Fact]
    public void Shortcut_IsSet_FromDefaultKey()
    {
        var commands = new FakeCommandSet();
        commands.Register(new EditorCommandDescriptor(
            Id: "save.cmd",
            DisplayName: "Save",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: new KeyBinding(EditorKey.S, KeyModifiers.Ctrl),
            IsEnabled: () => true),
            _ => { });

        var menu = new GlobalMenuRegistry();
        MenuCommandAdapter.Register(menu, commands, "save.cmd", "File/Save");

        var leaf = MenuCommandAdapter.FindNode(menu.Root, "File/Save");
        Assert.NotNull(leaf);
        Assert.Equal("Ctrl+S", leaf!.Shortcut);
    }

    [Fact]
    public void GetEnabled_Tracks_IsEnabled()
    {
        var commands = new FakeCommandSet();
        bool isEnabled = true;

        commands.Register(new EditorCommandDescriptor(
            Id: "dyn.cmd",
            DisplayName: "Dynamic",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => isEnabled),
            _ => { });

        var menu = new GlobalMenuRegistry();
        MenuCommandAdapter.Register(menu, commands, "dyn.cmd", "Tools/Dynamic");

        var leaf = MenuCommandAdapter.FindNode(menu.Root, "Tools/Dynamic");
        Assert.NotNull(leaf);
        Assert.NotNull(leaf!.GetEnabled);

        // Initially enabled.
        Assert.True(leaf.GetEnabled!());

        // Disable.
        isEnabled = false;
        Assert.False(leaf.GetEnabled!());
    }

    [Fact]
    public void Register_UnknownCommand_ThrowsInvalidOperationException()
    {
        var commands = new FakeCommandSet();
        var menu = new GlobalMenuRegistry();

        var ex = Assert.Throws<InvalidOperationException>(
            () => MenuCommandAdapter.Register(menu, commands, "nonexistent", "File/Nowhere"));

        Assert.Contains("nonexistent", ex.Message);
    }
}
