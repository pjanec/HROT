using FluentAssertions;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Action;

public sealed class EditorCommandsImplTests
{
    private static EditorCommandDescriptor MakeDescriptor(string id, bool enabled = true) =>
        new(id, id, null, null, null, null, IsEnabled: () => enabled);

    [Fact]
    public void Register_Then_Get_ReturnsDescriptor()
    {
        var cmds = new EditorCommandsImpl();
        cmds.Register(MakeDescriptor("test.cmd"), _ => { });

        var d = cmds.Get("test.cmd");

        d.Should().NotBeNull();
        d!.Id.Should().Be("test.cmd");
    }

    [Fact]
    public void Invoke_DisabledCommand_ReturnsFalse()
    {
        var cmds = new EditorCommandsImpl();
        cmds.Register(MakeDescriptor("test.disabled", enabled: false), _ => { });

        var result = cmds.Invoke("test.disabled");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Invoke_UnknownCommand_ReturnsFalse()
    {
        var cmds = new EditorCommandsImpl();

        var result = cmds.Invoke("editor.unknown");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown");
    }

    [Fact]
    public void Invoke_Succeeds_CallsAction()
    {
        var cmds = new EditorCommandsImpl();
        bool called = false;
        cmds.Register(MakeDescriptor("test.ok"), _ => { called = true; });

        var result = cmds.Invoke("test.ok");

        result.Success.Should().BeTrue();
        called.Should().BeTrue();
    }
}
