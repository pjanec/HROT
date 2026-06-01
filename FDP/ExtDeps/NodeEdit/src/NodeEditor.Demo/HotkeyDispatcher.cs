using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo;

/// <summary>
/// Reads hotkey bindings from registered commands and dispatches them
/// each frame via the editor's IInputSource.
/// </summary>
public sealed class HotkeyDispatcher
{
    private readonly IInputSource        _input;
    private readonly IEditorCommands     _commands;

    public HotkeyDispatcher(IInputSource input, IEditorCommands commands)
    {
        _input    = input;
        _commands = commands;
    }

    /// <summary>Call once per frame before rendering.</summary>
    public void ProcessThisFrame()
    {
        // Yield host hotkeys exclusively when the user is actively typing in a text field.
        if (ImGui.GetIO().WantTextInput)
            return;

        var mods = _input.Modifiers;

        foreach (var desc in _commands.All)
        {
            if (desc.DefaultKey is not { } binding) continue;
            if (binding.Modifiers != mods) continue;
            if (!_input.IsKeyPressed(binding.Key, allowRepeat: false)) continue;

            _commands.Invoke(desc.Id, null);
        }
    }
}
