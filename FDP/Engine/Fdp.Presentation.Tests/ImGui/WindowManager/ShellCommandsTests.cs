using System;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Action;
using Xunit;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Unit tests for <see cref="ShellEditorCommands"/> — MTB-P2-T1 success conditions.
/// Pure logic tests; no ImGui context required.
/// </summary>
public class ShellCommandsTests
{
    [Fact]
    public void RegisteredCommand_IsReturnedByGetAndAll()
    {
        var shell = new ShellEditorCommands();
        bool wasInvoked = false;

        var descriptor = new EditorCommandDescriptor(
            Id: "test.command",
            DisplayName: "Test Command",
            Category: "Test",
            Description: "A test command",
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true);

        shell.Register(descriptor, _ => wasInvoked = true);

        // Get returns the registered descriptor.
        var fromGet = shell.Get("test.command");
        Assert.NotNull(fromGet);
        Assert.Equal("test.command", fromGet!.Id);
        Assert.Equal("Test Command", fromGet.DisplayName);

        // All contains the registered descriptor.
        Assert.Contains(shell.All, d => d.Id == "test.command");

        // Handler not called yet.
        Assert.False(wasInvoked);
    }

    [Fact]
    public void Invoke_CallsHandler_WhenEnabled()
    {
        var shell = new ShellEditorCommands();
        bool wasInvoked = false;

        var descriptor = new EditorCommandDescriptor(
            Id: "enabled.cmd",
            DisplayName: "Enabled",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => true);

        shell.Register(descriptor, _ => wasInvoked = true);

        var result = shell.Invoke("enabled.cmd");

        Assert.True(wasInvoked, "handler should have been called");
        Assert.True(result.Success, "result should indicate success");
    }

    [Fact]
    public void Invoke_NoOp_WhenDisabled()
    {
        var shell = new ShellEditorCommands();
        bool wasInvoked = false;

        var descriptor = new EditorCommandDescriptor(
            Id: "disabled.cmd",
            DisplayName: "Disabled",
            Category: null,
            Description: null,
            IconKey: null,
            DefaultKey: null,
            IsEnabled: () => false);

        shell.Register(descriptor, _ => wasInvoked = true);

        var result = shell.Invoke("disabled.cmd");

        Assert.False(wasInvoked, "handler must NOT be called when disabled");
        Assert.False(result.Success, "result must indicate failure/not-invoked");
    }

    [Fact]
    public void Invoke_UnknownCommand_ReturnsFailure()
    {
        var shell = new ShellEditorCommands();
        var result = shell.Invoke("nonexistent");
        Assert.False(result.Success);
    }

    [Fact]
    public void AvailabilityChanged_Fires_OnNotify()
    {
        var shell = new ShellEditorCommands();
        string? receivedId = null;
        shell.AvailabilityChanged += id => receivedId = id;

        shell.NotifyAvailabilityChanged("some.cmd");

        Assert.Equal("some.cmd", receivedId);
    }
}
