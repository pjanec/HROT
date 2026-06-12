using System;
using System.Collections.Generic;
using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Dialogs;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Dialogs;

/// <summary>
/// Headless tests for <see cref="SaveAsBrowserDialog"/> — exercises the public seams
/// without an ImGui rendering context.
/// </summary>
public sealed class SaveAsBrowserDialogTests
{
    [Fact]
    public void Open_SetsIsOpen_True_AndClose_Cancels()
    {
        var dialog = new SaveAsBrowserDialog();
        SaveAsResult? captured = null;

        dialog.IsOpen.Should().BeFalse("dialog starts closed");

        var request = new SaveAsRequest
        {
            Title = "Test",
            GetFolderTree = () => new CategoryNode("root", Array.Empty<CategoryNode>()),
        };

        dialog.Open(request, r => captured = r);
        dialog.IsOpen.Should().BeTrue("Open must set IsOpen");

        dialog.Close();
        dialog.IsOpen.Should().BeFalse("Close must set IsOpen false");
        captured.Should().NotBeNull("Close must fire onChosen");
        captured!.Confirmed.Should().BeFalse("Close fires with Confirmed:false");
    }

    [Fact]
    public void ConfirmActive_NewName_FiresOnChosen_NoOverwrite_AndCloses()
    {
        var dialog = new SaveAsBrowserDialog();
        SaveAsResult? captured = null;

        var request = new SaveAsRequest
        {
            Title = "Test",
            GetFolderTree = () => new CategoryNode("root", Array.Empty<CategoryNode>()),
            NameExists = (name, dest) => false,
        };

        dialog.Open(request, r => captured = r);
        dialog.SetName("Foo");
        dialog.SetDestination("AI");

        var result = dialog.ConfirmActive();

        result.Confirmed.Should().BeTrue();
        result.Name.Should().Be("Foo");
        result.DestinationPath.Should().Be("AI");
        result.Overwrite.Should().BeFalse();

        captured.Should().NotBeNull("onChosen must be called for new name");
        captured!.Confirmed.Should().BeTrue();
        captured.Name.Should().Be("Foo");
        captured.DestinationPath.Should().Be("AI");
        captured.Overwrite.Should().BeFalse();

        dialog.IsOpen.Should().BeFalse("dialog must close after confirm");
    }

    [Fact]
    public void ConfirmActive_ExistingName_SetsPendingOverwrite_NoFire()
    {
        var dialog = new SaveAsBrowserDialog();
        SaveAsResult? captured = null;

        var request = new SaveAsRequest
        {
            Title = "Test",
            GetFolderTree = () => new CategoryNode("root", Array.Empty<CategoryNode>()),
            NameExists = (name, dest) => true,
        };

        dialog.Open(request, r => captured = r);
        dialog.SetName("Existing");
        dialog.SetDestination("SomeFolder");

        var result = dialog.ConfirmActive();

        result.Confirmed.Should().BeTrue("ConfirmActive returns Confirmed:true even when overwrite is pending");
        result.Name.Should().Be("Existing");
        result.Overwrite.Should().BeFalse("ConfirmActive does not set Overwrite:true");

        dialog.PendingOverwriteConfirm.Should().BeTrue("NameExists returned true");
        dialog.IsOpen.Should().BeTrue("dialog must stay open while pending overwrite");
        captured.Should().BeNull("onChosen must NOT be called yet");
    }

    [Fact]
    public void ConfirmOverwrite_AfterPending_FiresOnChosen_Overwrite_AndCloses()
    {
        var dialog = new SaveAsBrowserDialog();
        SaveAsResult? captured = null;

        var request = new SaveAsRequest
        {
            Title = "Test",
            GetFolderTree = () => new CategoryNode("root", Array.Empty<CategoryNode>()),
            NameExists = (name, dest) => true,
        };

        dialog.Open(request, r => captured = r);
        dialog.SetName("Existing");
        dialog.SetDestination("SomeFolder");

        // First, trigger the pending state.
        dialog.ConfirmActive();
        dialog.PendingOverwriteConfirm.Should().BeTrue("precondition");

        // Now confirm the overwrite.
        var result = dialog.ConfirmOverwrite();

        result.Confirmed.Should().BeTrue();
        result.Name.Should().Be("Existing");
        result.DestinationPath.Should().Be("SomeFolder");
        result.Overwrite.Should().BeTrue();

        captured.Should().NotBeNull("onChosen must be called for overwrite");
        captured!.Confirmed.Should().BeTrue();
        captured.Overwrite.Should().BeTrue();

        dialog.IsOpen.Should().BeFalse("dialog must close after overwrite confirm");
        dialog.PendingOverwriteConfirm.Should().BeFalse();
    }

    [Fact]
    public void ConfirmActive_InvalidName_DoesNotConfirm()
    {
        var dialog = new SaveAsBrowserDialog();
        SaveAsResult? captured = null;

        var request = new SaveAsRequest
        {
            Title = "Test",
            GetFolderTree = () => new CategoryNode("root", Array.Empty<CategoryNode>()),
            ValidateName = name => string.IsNullOrWhiteSpace(name) ? "Name cannot be empty." : null,
        };

        dialog.Open(request, r => captured = r);
        dialog.SetName("  "); // whitespace-only — invalid
        dialog.SetDestination("AI");

        var result = dialog.ConfirmActive();

        result.Confirmed.Should().BeFalse("invalid name must not confirm");
        captured.Should().BeNull("onChosen must not fire for invalid name");
        dialog.IsOpen.Should().BeTrue("dialog must stay open for invalid name");
    }
}
