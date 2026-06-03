using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Per-frame hotkey pump for the AI editor canvas perspectives.
///
/// <para>
/// Mirrors the NodeEdit demo's <c>HotkeyDispatcher</c>: it reads each registered
/// command's <see cref="EditorCommandDescriptor.DefaultKey"/> binding from an
/// <see cref="IEditorCommands"/> instance and invokes the command when the host
/// <see cref="IInputSource"/> reports the matching key chord this frame.
/// </para>
///
/// <para>
/// The dispatcher is intentionally pure (input + commands only) so it can be unit
/// tested headlessly. The window that drives it is responsible for the ImGui
/// "is the user typing in a text field?" gate (so command hotkeys do not steal
/// keystrokes from text inputs).
/// </para>
/// </summary>
public sealed class EditorHotkeyDispatcher
{
    private readonly IInputSource _input;

    /// <summary>
    /// Creates a dispatcher bound to the host input source.
    /// </summary>
    /// <param name="input">Per-frame host input snapshot.</param>
    public EditorHotkeyDispatcher(IInputSource input)
    {
        _input = input ?? throw new System.ArgumentNullException(nameof(input));
    }

    /// <summary>
    /// Evaluates every command's key binding against the current input snapshot and
    /// invokes the first matching enabled command. Call once per frame.
    /// </summary>
    /// <param name="commands">
    /// The active perspective's command set. When <c>null</c> the call is a no-op.
    /// </param>
    public void ProcessThisFrame(IEditorCommands? commands)
    {
        if (commands == null) return;

        var mods = _input.Modifiers;

        foreach (var desc in commands.All)
        {
            if (desc.DefaultKey is not { } binding) continue;
            if (binding.Modifiers != mods) continue;
            if (!_input.IsKeyPressed(binding.Key, allowRepeat: false)) continue;

            commands.Invoke(desc.Id, null);
        }
    }
}
